import { useEffect, useRef, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Ticket = { id:string; deviceId:string; deviceName:string|null; subject:string; description:string; state:string; resolution:string|null; version:number; createdAt:string; hasScreenshot:boolean };
type Message = {id:number;kind:string;body:string;fromState:string|null;toState:string|null;createdAt:string;isImported:boolean};
type Thread = {version:number;state:string;resolution:string|null;canReply:boolean;hasMore:boolean;oldestId:number|null;messages:Message[]};
const labels: Record<string,string> = {open:'Aperto',in_progress:'In lavorazione',resolved:'Risolto',closed:'Chiuso'};
const date=(value:string)=>new Date(value).toLocaleString('it-IT');
export function SupportDesk({visibleDeviceIds,onOpenDevice}:{visibleDeviceIds:string[];onOpenDevice:(id:string)=>Promise<void>}) {
  const [tickets,setTickets] = useState<Ticket[]>([]);
  const [error,setError] = useState('');
  const [busy,setBusy] = useState(false);
  const [selected,setSelected] = useState<Ticket|null>(null);
  const [thread,setThread]=useState<Thread|null>(null);
  const [filterState,setFilterState]=useState('');
  const [search,setSearch]=useState('');
  const [state,setState] = useState('open');
  const [resolution,setResolution] = useState('');
  const panel=useRef<HTMLElement>(null);
  const reply=useRef<HTMLTextAreaElement>(null);
  const preview=useRef<HTMLDialogElement>(null);
  const needsReply=(state==='resolved'||state==='closed')&&!selected?.resolution;
  async function load() { const query=new URLSearchParams();if(filterState)query.set('state',filterState);if(search.trim())query.set('search',search.trim());setTickets(await api<Ticket[]>('/support?'+query.toString())); }
  async function loadThread(ticket:Ticket,before?:number) {
    const data=await api<Thread>(`/support/${ticket.id}/messages${before?'?before='+before:''}`);
    if(before){setThread(old=>old?{...old,hasMore:data.hasMore,oldestId:data.oldestId,messages:[...data.messages,...old.messages]}:data);return;}
    setThread(data);setSelected(old=>old?.id===ticket.id?{...old,version:data.version,state:data.state,resolution:data.resolution}:old);setState(data.state);
  }
  async function work(action:()=>Promise<void>) {
    setBusy(true); setError('');
    try { await action(); } catch(e) { setError(e instanceof Error ? e.message : 'Operazione non riuscita'); }
    finally { setBusy(false); }
  }
  useEffect(()=>{void work(load);},[]);
  useEffect(()=>{if(selected)panel.current?.scrollIntoView({behavior:'smooth',block:'start'});},[selected?.id]);
  return <section className="panel"><h2><Icon name="support"/>Richieste di assistenza</h2>
    <p>Fino a 100 richieste nei tuoi ambiti autorizzati. Cerca per oggetto o nome del computer.</p>
    {error&&!selected&&<p role="alert">{error}</p>}
    <form className="support-toolbar" onSubmit={e=>{e.preventDefault();void work(load);}}>
      <label className="search-field"><Icon name="search"/><input aria-label="Cerca richieste" maxLength={200} placeholder="Oggetto o nome del computer…" value={search} onChange={e=>setSearch(e.target.value)}/></label>
      <label className="filter-field">Stato richieste<select aria-label="Filtra stato richieste" value={filterState} onChange={e=>setFilterState(e.target.value)}><option value="">Tutti gli stati</option>{Object.entries(labels).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label>
      <button disabled={busy}><Icon name="search"/>Filtra</button>
      <button className="secondary" type="button" disabled={busy} onClick={()=>void work(load)}><Icon name="refresh"/>Aggiorna richieste</button>
    </form>
    <div className="table-wrap"><table><thead><tr><th>Data</th><th>Oggetto</th><th>Stato</th><th>Dispositivo</th><th>Dettagli</th></tr></thead>
      <tbody>{tickets.map(t=><tr key={t.id}><td>{date(t.createdAt)}</td><td>{t.subject}</td><td><span className={`status-pill ${t.state==='resolved'||t.state==='closed'?'online':'offline'}`}>{labels[t.state]??t.state}</span></td><td>{visibleDeviceIds.includes(t.deviceId)?<button className="device-link" title="Apri scheda dispositivo" disabled={busy} onClick={()=>void work(()=>onOpenDevice(t.deviceId))}><Icon name="devices" size={17}/>{t.deviceName||'Dispositivo non disponibile'}<Icon name="arrow" size={14}/></button>:<span className="device-name"><Icon name="devices" size={17}/>{t.deviceName||'Dispositivo non disponibile'}</span>}</td><td><button disabled={busy} onClick={()=>{setSelected(t);setThread(null);setState(t.state);setResolution('');void work(()=>loadThread(t));}}>Apri ticket</button></td></tr>)}</tbody></table></div>
    {!tickets.length&&<p>Nessuna richiesta corrisponde ai filtri o ai tuoi ambiti.</p>}
    {selected&&<section ref={panel} className="ticket-conversation" aria-label="Conversazione ticket">
      <div className="section-heading"><div><span className="eyebrow">CONVERSAZIONE</span><h3>{selected.subject}</h3><span className="device-name"><Icon name="devices" size={16}/>{selected.deviceName||'Dispositivo non disponibile'}</span></div><button className="secondary compact" type="button" disabled={busy} onClick={()=>{setSelected(null);setThread(null);setError('');}}><Icon name="close" size={15}/>Chiudi dettaglio</button></div>
      {error&&<p role="alert">{error}</p>}
      <div className="conversation-log" role="log" aria-label="Messaggi del ticket" aria-live="polite">
        <article className="chat-message requester"><div className="chat-meta"><strong>Richiesta dell’utente</strong><time>{date(selected.createdAt)}</time></div><p>{selected.description}</p>
          {selected.hasScreenshot&&<button className="chat-image-button" type="button" onClick={()=>preview.current?.showModal()} aria-label="Ingrandisci screenshot"><img src={`/api/support/${selected.id}/screenshot?inline=true`} alt="Screenshot allegato alla richiesta" loading="lazy"/><span><Icon name="search" size={14}/>Ingrandisci screenshot</span></button>}
        </article>
        {!thread&&<p>Caricamento conversazione…</p>}
        {thread?.hasMore&&<button className="secondary compact" disabled={busy} onClick={()=>void work(()=>loadThread(selected,thread.oldestId!))}>Carica messaggi precedenti</button>}
        {thread?.messages.map(m=>m.kind==='state_change'?<p className="chat-event" key={m.id}><Icon name="check" size={14}/>{labels[m.fromState??'']??m.fromState} → {labels[m.toState??'']??m.toState}<time>{date(m.createdAt)}</time></p>:<article className="chat-message staff" key={m.id}><div className="chat-meta"><strong>{m.isImported?'Risposta precedente':'Assistenza IT'}</strong><time>{m.isImported?'Ultima modifica: ':''}{date(m.createdAt)}</time></div><p>{m.body}</p></article>)}
      </div>
      {thread&&<div className="conversation-toolbar"><span className="status-pill offline">{labels[selected.state]??selected.state}</span><button className="secondary compact" disabled={busy} onClick={()=>void work(()=>loadThread(selected))}><Icon name="refresh" size={15}/>Aggiorna messaggi</button></div>}
      {thread?.canReply?<form className="chat-composer" onSubmit={e=>{
        e.preventDefault();const body=resolution.trim();
        if(needsReply&&!body){setError('Scrivi una risposta all’utente prima di risolvere o chiudere il ticket.');reply.current?.focus();return;}
        if(!body&&state===selected.state)return;
        void work(async()=>{await api(`/support/${selected.id}`,'PUT',{expectedVersion:selected.version,state,resolution:body||selected.resolution,addReply:!!body});setResolution('');await loadThread(selected);await load();});
      }}>
        <label htmlFor="ticket-state">Stato</label><select id="ticket-state" value={state} disabled={busy} onChange={e=>setState(e.target.value)}>{Object.entries(labels).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select>
        <label>Risposta visibile al richiedente<textarea ref={reply} maxLength={8000} required={needsReply} disabled={busy} value={resolution} placeholder="Scrivi una risposta all’utente…" aria-describedby="ticket-reply-help" onChange={e=>setResolution(e.target.value)}/></label>
        <div className="composer-footer"><small id="ticket-reply-help">{needsReply?'Per risolvere o chiudere serve una risposta.':'Stato e ultima risposta sono visibili all’utente nella Tray, con Aggiorna ticket.'}</small><button disabled={busy||(!resolution.trim()&&state===selected.state)}><Icon name="arrow" size={16}/>{resolution.trim()?'Invia risposta':'Aggiorna stato'}</button></div>
      </form>:thread&&<p>Puoi leggere la conversazione. Per rispondere serve il permesso di gestione ticket.</p>}
      {selected.hasScreenshot&&<dialog ref={preview} className="screenshot-dialog"><div className="section-heading"><strong>Screenshot della richiesta</strong><button className="secondary" onClick={()=>preview.current?.close()}><Icon name="close"/>Chiudi immagine</button></div><img src={`/api/support/${selected.id}/screenshot?inline=true`} alt="Screenshot della richiesta ingrandito"/></dialog>}
    </section>}
  </section>;
}
