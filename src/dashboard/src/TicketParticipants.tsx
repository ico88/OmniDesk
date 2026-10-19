import { useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Participant = {id:string;userId:string;name:string|null;email:string|null;addedAt:string};
type Candidate = {id:string;name:string|null;email:string|null};
const date=(value:string)=>new Date(value).toLocaleString('it-IT');

export function TicketParticipants({ticketId,version,canManage,onChanged}:{ticketId:string;version:number;canManage:boolean;onChanged:()=>Promise<void>}) {
  const [participants,setParticipants]=useState<Participant[]>([]);
  const [candidates,setCandidates]=useState<Candidate[]>([]);
  const [selected,setSelected]=useState('');
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  async function load() {
    const [rows,available]=await Promise.all([
      api<Participant[]>(`/support/${ticketId}/participants`),
      canManage?api<Candidate[]>(`/support/${ticketId}/participant-candidates`):Promise.resolve([])
    ]);
    setParticipants(rows);setCandidates(available);
    setSelected(current=>available.some(x=>x.id===current)?current:available[0]?.id??'');
  }
  useEffect(()=>{void load().catch(e=>setError(e instanceof Error?e.message:'Caricamento partecipanti non riuscito'));},[ticketId,version,canManage]);
  async function add(event:FormEvent) {
    event.preventDefault();if(!selected)return;
    setBusy(true);setError('');
    try {await api(`/support/${ticketId}/participants`,'POST',{expectedVersion:version,userId:selected});await onChanged();await load();}
    catch(e) {setError(e instanceof Error?e.message:'Aggiunta partecipante non riuscita');}
    finally {setBusy(false);}
  }
  async function remove(participant:Participant) {
    setBusy(true);setError('');
    try {await api(`/support/${ticketId}/participants/${participant.id}?expectedVersion=${version}`,'DELETE');await onChanged();await load();}
    catch(e) {setError(e instanceof Error?e.message:'Rimozione partecipante non riuscita');}
    finally {setBusy(false);}
  }
  return <section className="ticket-participants" aria-label="Partecipanti al ticket">
    <div className="section-heading"><div><strong><Icon name="users" size={16}/>Partecipanti</strong><small>Ricevono notifiche sulle risposte e sugli aggiornamenti. L’accesso richiede già il permesso di lettura ticket.</small></div></div>
    {error&&<p role="alert">{error}</p>}
    {participants.length?<ul>{participants.map(item=><li key={item.id}><span><strong>{item.name||item.email||'Operatore'}</strong><small>{item.email} · Aggiunto il {date(item.addedAt)}</small></span>{canManage&&<button className="secondary compact" type="button" disabled={busy} aria-label={`Rimuovi ${item.name||item.email||'partecipante'}`} onClick={()=>void remove(item)}><Icon name="close" size={15}/>Rimuovi</button>}</li>)}</ul>:<p>Nessun partecipante aggiuntivo.</p>}
    {canManage&&<form className="ticket-participant-add" onSubmit={e=>void add(e)}><label htmlFor="ticket-participant-select">Aggiungi partecipante<select id="ticket-participant-select" aria-label="Partecipante da aggiungere" value={selected} disabled={busy||!candidates.length} onChange={e=>setSelected(e.target.value)}><option value="">{candidates.length?'Seleziona un operatore':'Nessun operatore con accesso di lettura disponibile'}</option>{candidates.map(item=><option key={item.id} value={item.id}>{item.name||item.email} · {item.email}</option>)}</select></label><button className="secondary compact" disabled={busy||!selected}><Icon name="users" size={15}/>Aggiungi</button></form>}
  </section>;
}
