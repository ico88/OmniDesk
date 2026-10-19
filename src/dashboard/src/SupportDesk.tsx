import { useEffect, useState } from 'react';
import { api } from './api';

type Ticket = { id:string; deviceId:string; subject:string; description:string; state:string; resolution:string|null; version:number; createdAt:string; hasScreenshot:boolean };
const labels: Record<string,string> = {open:'Aperto',in_progress:'In lavorazione',resolved:'Risolto',closed:'Chiuso'};
export function SupportDesk() {
  const [tickets,setTickets] = useState<Ticket[]>([]);
  const [error,setError] = useState('');
  const [busy,setBusy] = useState(false);
  const [selected,setSelected] = useState<Ticket|null>(null);
  const [state,setState] = useState('open');
  const [resolution,setResolution] = useState('');
  async function load() { setTickets(await api<Ticket[]>('/support')); }
  async function work(action:()=>Promise<void>) {
    setBusy(true); setError('');
    try { await action(); } catch(e) { setError(e instanceof Error ? e.message : 'Operazione non riuscita'); }
    finally { setBusy(false); }
  }
  useEffect(()=>{void work(load);},[]);
  return <section className="panel"><h2>Richieste di assistenza</h2>
    <p>Ultime 100 richieste nei tuoi ambiti autorizzati. I permessi sui ticket non autorizzano le sessioni remote.</p>
    {error && <p role="alert">{error}</p>}
    <button disabled={busy} onClick={()=>void work(load)}>Aggiorna richieste</button>
    <table><thead><tr><th>Data</th><th>Oggetto</th><th>Stato</th><th>Dispositivo</th><th>Dettagli</th></tr></thead>
      <tbody>{tickets.map(t=><tr key={t.id}><td>{new Date(t.createdAt).toLocaleString('it-IT')}</td><td>{t.subject}</td><td>{labels[t.state]??t.state}</td><td>{t.deviceId}</td><td><button onClick={()=>{setSelected(t);setState(t.state);setResolution(t.resolution??'');}}>Apri ticket</button></td></tr>)}</tbody></table>
    {!tickets.length && <p>Nessuna richiesta visibile.</p>}
    {selected && <form onSubmit={e=>{e.preventDefault();void work(async()=>{
      await api(`/support/${selected.id}`,'PUT',{expectedVersion:selected.version,state,resolution}); setSelected(null); await load();
    });}}><h3>{selected.subject}</h3><p style={{whiteSpace:'pre-wrap'}}>{selected.description}</p>
      {selected.hasScreenshot && <p><a href={`/api/support/${selected.id}/screenshot`} download>Scarica screenshot autorizzato</a></p>}
      <label htmlFor="ticket-state">Stato</label><select id="ticket-state" value={state} onChange={e=>setState(e.target.value)}>{Object.entries(labels).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select>
      <label>Risposta visibile al richiedente<textarea maxLength={8000} value={resolution} onChange={e=>setResolution(e.target.value)}/></label>
      <p>Per risolvere o chiudere il ticket è richiesta una risposta. Il server verifica il permesso tickets.manage.</p>
      <button disabled={busy}>Salva ticket</button><button type="button" onClick={()=>setSelected(null)}>Chiudi dettaglio</button>
    </form>}
  </section>;
}
