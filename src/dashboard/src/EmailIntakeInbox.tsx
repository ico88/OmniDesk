import { useCallback, useEffect, useRef, useState } from 'react';
import { Inbox, Mail, RefreshCw, Trash2 } from 'lucide-react';
import { api } from './api';
import './email-intake.css';

type Intake = {
  id: string;
  mailboxAddress: string;
  messageId: string;
  senderAddress: string;
  subject: string;
  bodyText: string;
  messageReceivedAt: string;
  receivedAt: string;
  state: 'pending';
};

const formatDate = (value: string) => new Date(value).toLocaleString('it-IT');

export function EmailIntakeInbox({ onMessage }: { onMessage: (message: string) => void }) {
  const [items, setItems] = useState<Intake[]>([]);
  const [busy, setBusy] = useState(false);
  const [busyId, setBusyId] = useState('');
  const [error, setError] = useState('');
  const requestVersion = useRef(0);

  const refresh = useCallback(async () => {
    const version = ++requestVersion.current;
    setBusy(true);
    try {
      const rows = await api<Intake[]>('/admin/email-intakes');
      if (version === requestVersion.current) setItems(rows);
      setError('');
    } catch (e) {
      if (version === requestVersion.current) setError(e instanceof Error ? e.message : 'Coda email non disponibile');
    } finally {
      if (version === requestVersion.current) setBusy(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    const timer = window.setInterval(() => void refresh(), 30_000);
    return () => window.clearInterval(timer);
  }, [refresh]);

  async function discard(item: Intake) {
    if (!window.confirm(`Scartare il messaggio “${item.subject}” da ${item.senderAddress}?`)) return;
    setBusyId(item.id);
    setError('');
    try {
      await api(`/admin/email-intakes/${item.id}/discard`, 'POST');
      requestVersion.current++;
      setBusy(false);
      setItems(current => current.filter(row => row.id !== item.id));
      onMessage('Messaggio email scartato e registrato nell’audit');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Impossibile scartare il messaggio');
    } finally {
      setBusyId('');
    }
  }

  return <section className="panel email-intake-inbox">
    <div className="section-heading">
      <div><span className="eyebrow">SERVICE DESK · INGRESSI NON VERIFICATI</span><h2><Inbox size={19}/>Email da valutare <span className="count">{items.length}</span></h2></div>
      <button className="secondary" type="button" disabled={busy} onClick={() => void refresh()}><RefreshCw size={16}/>Aggiorna</button>
    </div>
    <p>Il mittente non è stato autenticato. Valuta il contenuto prima di associarlo a una persona o a un ticket.</p>
    {error && <p role="alert" className="error">{error}</p>}
    {items.length === 0 ? <div className="empty-state"><Mail size={34}/><h3>{busy ? 'Caricamento messaggi…' : 'Nessun messaggio in attesa'}</h3><p>Gli ingressi email verificati dal webhook compariranno qui per la revisione manuale.</p></div> :
      <div className="email-intake-list">{items.map(item => <article className="email-intake-item" key={item.id}>
        <header><div><h3>{item.subject}</h3><p><strong>Da:</strong> {item.senderAddress} <span aria-hidden="true">·</span> <strong>Casella:</strong> {item.mailboxAddress}</p><small>Ricevuto {formatDate(item.messageReceivedAt)} · Acquisito {formatDate(item.receivedAt)}</small></div>
          <button className="secondary danger-action" type="button" disabled={busyId === item.id} onClick={() => void discard(item)}><Trash2 size={15}/>{busyId === item.id ? 'Scarto…' : 'Scarta'}</button></header>
        <details><summary>Mostra testo del messaggio</summary><pre>{item.bodyText}</pre></details>
        <small className="email-message-id">Message-ID: {item.messageId}</small>
      </article>)}</div>}
  </section>;
}
