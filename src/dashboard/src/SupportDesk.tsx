import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Ticket = { id:string; deviceId:string; deviceName:string|null; subject:string; description:string; state:string; resolution:string|null; version:number; createdAt:string; hasScreenshot:boolean };
const labels: Record<string,string> = {open:'Aperto',in_progress:'In lavorazione',resolved:'Risolto',closed:'Chiuso'};
export function SupportDesk({visibleDeviceIds,onOpenDevice}:{visibleDeviceIds:string[];onOpenDevice:(id:string)=>Promise<void>}) {
  const [tickets,setTickets] = useState<Ticket[]>([]);
  const [error,setError] = useState('');
  const [busy,setBusy] = useState(false);
  const [selected,setSelected] = useState<Ticket|null>(null);
  const [filterState,setFilterState]=useState('');
  const [search,setSearch]=useState('');
  const [state,setState] = useState('open');
  const [resolution,setResolution] = useState('');
  async function load() { const query=new URLSearchParams();if(filterState)query.set('state',filterState);if(search.trim())query.set('search',search.trim());setTickets(await api<Ticket[]>('/support?'+query.toString())); }
  async function work(action:()=>Promise<void>) {
    setBusy(true); setError('');
    try { await action(); } catch(e) { setError(e instanceof Error ? e.message : 'Operazione non riuscita'); }
    finally { setBusy(false); }
  }
  useEffect(()=>{void work(load);},[]);
  return <section className="panel"><h2><Icon name="support"/>Richieste di assistenza</h2>
    <p>Fino a 100 richieste nei tuoi ambiti autorizzati. Cerca per oggetto o nome del computer.</p>
    {error && <p role="alert">{error}</p>}
    <form className="support-toolbar" onSubmit={e=>{e.preventDefault();void work(load);}}>
      <label className="search-field"><Icon name="search"/><input aria-label="Cerca richieste" maxLength={200} placeholder="Oggetto o nome del computer…" value={search} onChange={e=>setSearch(e.target.value)}/></label>
      <label className="filter-field">Stato richieste<select aria-label="Filtra stato richieste" value={filterState} onChange={e=>setFilterState(e.target.value)}><option value="">Tutti gli stati</option>{Object.entries(labels).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label>
      <button disabled={busy}><Icon name="search"/>Filtra</button>
      <button className="secondary" type="button" disabled={busy} onClick={()=>void work(load)}><Icon name="refresh"/>Aggiorna richieste</button>
    </form>
    <div className="table-wrap"><table><thead><tr><th>Data</th><th>Oggetto</th><th>Stato</th><th>Dispositivo</th><th>Dettagli</th></tr></thead>
      <tbody>{tickets.map(t=><tr key={t.id}><td>{new Date(t.createdAt).toLocaleString('it-IT')}</td><td>{t.subject}</td><td><span className={`status-pill ${t.state==='resolved'||t.state==='closed'?'online':'offline'}`}>{labels[t.state]??t.state}</span></td><td>{visibleDeviceIds.includes(t.deviceId)&&onOpenDevice?<button className="device-link" title="Apri scheda dispositivo" disabled={busy} onClick={()=>void work(()=>onOpenDevice(t.deviceId))}><Icon name="devices" size={17}/>{t.deviceName||'Dispositivo non disponibile'}<Icon name="arrow" size={14}/></button>:<span className="device-name"><Icon name="devices" size={17}/>{t.deviceName||'Dispositivo non disponibile'}</span>}</td><td><button onClick={()=>{setSelected(t);setState(t.state);setResolution(t.resolution??'');}}>Apri ticket</button></td></tr>)}</tbody></table></div>
    {!tickets.length && <p>Nessuna richiesta corrisponde ai filtri o ai tuoi ambiti.</p>}
    {selected && <form onSubmit={e=>{e.preventDefault();void work(async()=>{
      await api(`/support/${selected.id}`,'PUT',{expectedVersion:selected.version,state,resolution}); setSelected(null); await load();
    });}}><h3>{selected.subject}</h3><p>Dispositivo: <strong>{selected.deviceName||'Dispositivo non disponibile'}</strong></p><p style={{whiteSpace:'pre-wrap'}}>{selected.description}</p>
      {selected.hasScreenshot && <p><a href={`/api/support/${selected.id}/screenshot`} download>Scarica screenshot autorizzato</a></p>}
      <label htmlFor="ticket-state">Stato</label><select id="ticket-state" value={state} onChange={e=>setState(e.target.value)}>{Object.entries(labels).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select>
      <label>Risposta visibile al richiedente<textarea maxLength={8000} value={resolution} onChange={e=>setResolution(e.target.value)}/></label>
      <p>Per risolvere o chiudere il ticket è richiesta una risposta. Il server verifica il permesso tickets.manage.</p>
      <button disabled={busy}>Salva ticket</button><button type="button" onClick={()=>setSelected(null)}>Chiudi dettaglio</button>
    </form>}
  </section>;
}
