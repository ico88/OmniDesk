import { useCallback, useEffect, useState } from 'react';
import { CheckCircle2, RefreshCw, RotateCcw } from 'lucide-react';
import { api } from './api';

type Delivery = {
  notificationId: string;
  state: 'pending' | 'retry' | 'sending' | 'dead';
  attemptCount: number;
  nextAttemptAt: string;
  deadLetteredAt: string | null;
  lastErrorType: string | null;
  ticketNumber: number;
  kind: string;
  createdAt: string;
};
type Queue = { counts: { state: string; count: number }[]; items: Delivery[] };

export function EmailNotificationAdmin({ onMessage }: { onMessage: (message: string) => void }) {
  const [queue, setQueue] = useState<Queue>({ counts: [], items: [] });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const refresh = useCallback(async () => {
    setBusy(true);
    try { setQueue(await api<Queue>('/admin/email-notifications')); setError(''); }
    catch (e) { setError(e instanceof Error ? e.message : 'Coda email non disponibile'); }
    finally { setBusy(false); }
  }, []);
  useEffect(() => { void refresh(); }, [refresh]);

  async function retry(item: Delivery) {
    if (!window.confirm(`Rimettere in coda la notifica per OD-${String(item.ticketNumber).padStart(6, '0')}? L’email verrà inviata nuovamente.`)) return;
    setBusy(true); setError('');
    try {
      await api(`/admin/email-notifications/${item.notificationId}/retry`, 'POST');
      onMessage('Notifica rimessa in coda; il worker riproverà l’invio');
      await refresh();
    } catch (e) { setError(e instanceof Error ? e.message : 'Impossibile riprovare l’invio'); }
    finally { setBusy(false); }
  }

  const count = (state: string) => queue.counts.find(item => item.state === state)?.count ?? 0;
  return <section className="panel">
    <div className="section-heading"><div><span className="eyebrow">OPERAZIONI</span><h2>Consegne email ticket</h2></div>
      <button className="secondary compact" type="button" disabled={busy} onClick={() => void refresh()}><RefreshCw size={15}/>Aggiorna</button></div>
    <p>La coda non mostra indirizzi email o contenuti dei ticket. Il retry manuale è disponibile solo per consegne in dead letter.</p>
    <div className="ticket-summary"><article><small>In attesa</small><strong>{count('pending')}</strong></article><article><small>Retry automatico</small><strong>{count('retry')}</strong></article><article><small>Invio attivo</small><strong>{count('sending')}</strong></article><article className="urgent"><small>Dead letter</small><strong>{count('dead')}</strong></article></div>
    {error && <p role="alert">{error}</p>}
    {queue.items.length === 0 ? <p className="empty-state"><CheckCircle2 size={22}/>Non ci sono consegne email in attesa.</p> : <div className="table-wrap"><table><thead><tr><th>Ticket</th><th>Evento</th><th>Stato</th><th>Tentativi</th><th>Prossimo tentativo</th><th>Errore</th><th></th></tr></thead><tbody>{queue.items.map(item => <tr key={item.notificationId}>
      <td>OD-{String(item.ticketNumber).padStart(6, '0')}</td><td>{item.kind}</td><td><span className={`status-pill ${item.state === 'dead' ? 'danger' : item.state === 'sending' ? 'online' : 'offline'}`}>{item.state}</span></td>
      <td>{item.attemptCount}</td><td>{new Date(item.nextAttemptAt).toLocaleString('it-IT')}</td><td>{item.lastErrorType ?? '—'}</td>
      <td>{item.state === 'dead' && <button className="secondary compact" type="button" disabled={busy} onClick={() => void retry(item)}><RotateCcw size={14}/>Riprova</button>}</td>
    </tr>)}</tbody></table></div>}
  </section>;
}
