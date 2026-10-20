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
type DeviceOption = { id: string; name: string; online: boolean; revoked: boolean };
type PromotionDraft = {
  deviceId: string;
  subject: string;
  description: string;
  category: string;
  priority: string;
  confirmUnverifiedSender: boolean;
};

const formatDate = (value: string) => new Date(value).toLocaleString('it-IT');

export function EmailIntakeInbox({ onMessage, onOpenTicket, devices }: {
  onMessage: (message: string) => void;
  onOpenTicket: (ticketId: string) => void;
  devices: DeviceOption[];
}) {
  const [items, setItems] = useState<Intake[]>([]);
  const [busy, setBusy] = useState(false);
  const [busyId, setBusyId] = useState('');
  const [error, setError] = useState('');
  const requestVersion = useRef(0);
  const [promotingId, setPromotingId] = useState('');
  const [draft, setDraft] = useState<PromotionDraft | null>(null);

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

  function beginPromotion(item: Intake) {
    const header = `Email ricevuta da ${item.senderAddress} (mittente non verificato).\n\n`;
    const completeDescription = header + item.bodyText;
    setPromotingId(item.id);
    setDraft({
      deviceId: devices.find(device => !device.revoked)?.id ?? '',
      subject: item.subject,
      description: completeDescription.length <= 8000 ? completeDescription : '',
      category: 'general',
      priority: 'p3',
      confirmUnverifiedSender: false,
    });
    setError('');
  }

  async function promote(item: Intake) {
    if (!draft) return;
    setBusyId(item.id);
    setError('');
    try {
      const result = await api<{ ticketId: string; number: number; duplicate: boolean }>(
        `/admin/email-intakes/${item.id}/promote`, 'POST', draft);
      setItems(current => current.filter(row => row.id !== item.id));
      setPromotingId('');
      setDraft(null);
      onMessage(`Ticket OD-${String(result.number).padStart(6, '0')} creato. Il mittente non è stato associato a un account.`);
      onOpenTicket(result.ticketId);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Impossibile creare il ticket');
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
          <div className="email-intake-actions"><button className="secondary" type="button" disabled={busyId === item.id || devices.every(device => device.revoked)} onClick={() => promotingId === item.id ? (setPromotingId(''), setDraft(null)) : beginPromotion(item)}>{promotingId === item.id ? 'Annulla' : 'Crea ticket'}</button><button className="secondary danger-action" type="button" disabled={busyId === item.id} onClick={() => void discard(item)}><Trash2 size={15}/>{busyId === item.id ? 'Attendi…' : 'Scarta'}</button></div></header>
        {promotingId === item.id && draft && <form className="email-promotion-form" onSubmit={event => { event.preventDefault(); void promote(item); }}>
          <p>La conversione richiede una scelta esplicita del dispositivo. L’indirizzo del mittente non viene usato per identificare o collegare un account.</p>
          <label>Dispositivo<select required value={draft.deviceId} onChange={event => setDraft(value => value ? { ...value, deviceId: event.target.value } : value)}><option value="">Seleziona un dispositivo</option>{devices.filter(device => !device.revoked).map(device => <option key={device.id} value={device.id}>{device.name}{device.online ? ' · Online' : ' · Offline'}</option>)}</select></label>
          <label>Oggetto ticket<input required maxLength={200} value={draft.subject} onChange={event => setDraft(value => value ? { ...value, subject: event.target.value } : value)}/></label>
          <label>Riepilogo per il ticket<textarea required maxLength={8000} rows={6} value={draft.description} onChange={event => setDraft(value => value ? { ...value, description: event.target.value } : value)}/></label>
          <div className="email-promotion-options"><label>Categoria<select value={draft.category} onChange={event => setDraft(value => value ? { ...value, category: event.target.value } : value)}><option value="general">Generale</option><option value="hardware">Hardware</option><option value="software">Software</option><option value="network">Rete</option><option value="account">Account</option><option value="other">Altro</option></select></label><label>Priorità<select value={draft.priority} onChange={event => setDraft(value => value ? { ...value, priority: event.target.value } : value)}><option value="p4">P4 · Bassa</option><option value="p3">P3 · Normale</option><option value="p2">P2 · Alta</option><option value="p1">P1 · Critica</option></select></label></div>
          <label className="email-identity-confirm"><input required type="checkbox" checked={draft.confirmUnverifiedSender} onChange={event => setDraft(value => value ? { ...value, confirmUnverifiedSender: event.target.checked } : value)}/>Ho esaminato il messaggio. Il mittente non è verificato e non verrà associato a un account.</label>
          <button disabled={busyId === item.id || !devices.some(device => device.id === draft.deviceId && !device.revoked)} type="submit">{busyId === item.id ? 'Creazione ticket…' : 'Conferma e crea ticket'}</button>
        </form>}
        <details><summary>Mostra testo del messaggio</summary><pre>{item.bodyText}</pre></details>
        <small className="email-message-id">Message-ID: {item.messageId}</small>
      </article>)}</div>}
  </section>;
}
