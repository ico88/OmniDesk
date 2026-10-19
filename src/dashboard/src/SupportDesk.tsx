import { useEffect, useRef, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Ticket = { id:string; number:number; deviceId:string; siteId:string; deviceName:string|null; siteName:string|null; subject:string; description:string; state:string; priority:string; category:string; queueId:string|null; queueName:string|null; assigneeId:string|null; assigneeEmail:string|null; canManage:boolean; resolution:string|null; version:number; createdAt:string; hasScreenshot:boolean };
type Assignee = { id:string; email:string };
type Queue = { id:string; name:string };
type Message = {id:number;kind:string;body:string;fromState:string|null;toState:string|null;createdAt:string;isImported:boolean};
type Thread = {version:number;state:string;resolution:string|null;canReply:boolean;hasMore:boolean;oldestId:number|null;messages:Message[]};
const labels: Record<string,string> = {open:'Aperto',in_progress:'In lavorazione',resolved:'Risolto',closed:'Chiuso'};
const priorities: Record<string,string> = {low:'Bassa',normal:'Normale',high:'Alta',urgent:'Urgente'};
const categories: Record<string,string> = {general:'Generale',hardware:'Hardware',software:'Software',network:'Rete',account:'Account',other:'Altro'};
const date=(value:string)=>new Date(value).toLocaleString('it-IT');
export function SupportDesk({visibleDeviceIds,onOpenDevice,deviceId,sites=[]}:{visibleDeviceIds:string[];onOpenDevice:(id:string)=>Promise<void>;deviceId?:string;sites?:{id:string;name:string;isArchived?:boolean}[]}) {
  const [tickets,setTickets] = useState<Ticket[]>([]);
  const [queues,setQueues]=useState<Queue[]>([]);
  const [selectedTicketIds,setSelectedTicketIds]=useState<string[]>([]);
  const [bulkQueueId,setBulkQueueId]=useState('');
  const [error,setError] = useState('');
  const [busy,setBusy] = useState(false);
  const [selected,setSelected] = useState<Ticket|null>(null);
  const [thread,setThread]=useState<Thread|null>(null);
  const [filterState,setFilterState]=useState('');
  const [search,setSearch]=useState('');
  const [siteFilter,setSiteFilter]=useState('');
  const [filterPriority,setFilterPriority]=useState('');
  const [filterCategory,setFilterCategory]=useState('');
  const [filterQueue,setFilterQueue]=useState('');
  const [assigneeFilter,setAssigneeFilter]=useState('');
  const [state,setState] = useState('open');
  const [resolution,setResolution] = useState('');
  const [priority,setPriority]=useState('normal');
  const [queueId,setQueueId]=useState('');
  const [assigneeId,setAssigneeId]=useState('');
  const [assignees,setAssignees]=useState<Assignee[]>([]);
  const panel=useRef<HTMLElement>(null);
  const reply=useRef<HTMLTextAreaElement>(null);
  const preview=useRef<HTMLDialogElement>(null);
  const needsReply=(state==='resolved'||state==='closed')&&!selected?.resolution;
  async function load() { const query=new URLSearchParams();if(filterState)query.set('state',filterState);if(search.trim())query.set('search',search.trim());if(deviceId)query.set('deviceId',deviceId);if(siteFilter)query.set('siteId',siteFilter);if(filterPriority)query.set('priority',filterPriority);if(filterCategory)query.set('category',filterCategory);if(filterQueue)query.set('queue',filterQueue);if(assigneeFilter.trim())query.set('assignee',assigneeFilter.trim());const rows=await api<Ticket[]>('/support?'+query.toString());setTickets(rows);setSelectedTicketIds([]);setSelected(old=>old?rows.find(t=>t.id===old.id)??old:old); }
  async function loadThread(ticket:Ticket,before?:number) {
    const data=await api<Thread>(`/support/${ticket.id}/messages${before?'?before='+before:''}`);
    if(before){setThread(old=>old?{...old,hasMore:data.hasMore,oldestId:data.oldestId,messages:[...data.messages,...old.messages]}:data);return;}
    setThread(data);setSelected(old=>old?.id===ticket.id?{...old,version:data.version,state:data.state,resolution:data.resolution}:old);setState(data.state);
  }
  async function loadAssignees(ticket:Ticket,queue?:string) { const query=queue===undefined?'':`?queue=${queue||'none'}`;setAssignees(await api<Assignee[]>(`/support/${ticket.id}/assignees${query}`)); }
  async function assignQueueToSelected(queue:string) {
    const selectedTickets=tickets.filter(t=>selectedTicketIds.includes(t.id)&&t.canManage);
    if(!selectedTickets.length)return;
    await api('/support/bulk-queue','PUT',{queueId:queue||null,tickets:selectedTickets.map(t=>({ticketId:t.id,expectedVersion:t.version}))});
    setSelectedTicketIds([]);await load();
  }
  async function work(action:()=>Promise<void>) {
    setBusy(true); setError('');
    try { await action(); } catch(e) { setError(e instanceof Error ? e.message : 'Operazione non riuscita'); }
    finally { setBusy(false); }
  }
  useEffect(()=>{void work(async()=>{const [rows,queueRows]=await Promise.all([api<Ticket[]>('/support'),api<Queue[]>('/support/queues')]);setTickets(rows);setQueues(queueRows);});},[deviceId]);
  useEffect(()=>{if(selected)panel.current?.scrollIntoView({behavior:'smooth',block:'start'});},[selected?.id]);
  return <section className="panel"><h2><Icon name="support"/>Richieste di assistenza</h2>
    <p>Fino a 100 richieste nei tuoi ambiti autorizzati. Cerca per oggetto o nome del computer.</p>
    {error&&!selected&&<p role="alert">{error}</p>}
    <form className="support-toolbar" onSubmit={e=>{e.preventDefault();void work(load);}}>
      <label className="search-field"><Icon name="search"/><input aria-label="Cerca richieste" maxLength={200} placeholder="Oggetto o nome del computer…" value={search} onChange={e=>setSearch(e.target.value)}/></label>
      <label className="filter-field">Stato richieste<select aria-label="Filtra stato richieste" value={filterState} onChange={e=>setFilterState(e.target.value)}><option value="">Tutti gli stati</option>{Object.entries(labels).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label>
      <label className="filter-field">Priorità<select aria-label="Filtra priorità ticket" value={filterPriority} onChange={e=>setFilterPriority(e.target.value)}><option value="">Tutte le priorità</option>{Object.entries(priorities).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label>
      <label className="filter-field">Categoria<select aria-label="Filtra categoria ticket" value={filterCategory} onChange={e=>setFilterCategory(e.target.value)}><option value="">Tutte le categorie</option>{Object.entries(categories).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label>
      <label className="filter-field">Coda<select aria-label="Filtra coda ticket" value={filterQueue} onChange={e=>setFilterQueue(e.target.value)}><option value="">Tutte le code</option><option value="unassigned">Senza coda</option>{queues.map(q=><option key={q.id} value={q.id}>{q.name}</option>)}</select></label>
      <label className="filter-field">Assegnatario<input aria-label="Filtra assegnatario ticket" maxLength={320} placeholder="email operatore" value={assigneeFilter} onChange={e=>setAssigneeFilter(e.target.value)}/></label>
      {!deviceId&&sites.length>0&&<label className="filter-field">Sede<select aria-label="Filtra sede ticket" value={siteFilter} onChange={e=>setSiteFilter(e.target.value)}><option value="">Tutte le sedi autorizzate</option>{sites.map(s=><option key={s.id} value={s.id}>{s.name}</option>)}</select></label>}
      <button disabled={busy}><Icon name="search"/>Filtra</button>
      <button className="secondary" type="button" disabled={busy} onClick={()=>void work(load)}><Icon name="refresh"/>Aggiorna richieste</button>
    </form>
    {tickets.some(t=>t.canManage)&&<div className="conversation-toolbar"><span>{selectedTicketIds.length} ticket selezionati</span><label className="filter-field">Assegna coda<select aria-label="Coda assegnazione massiva" value={bulkQueueId} disabled={busy} onChange={e=>setBulkQueueId(e.target.value)}><option value="">Senza coda</option>{queues.map(q=><option key={q.id} value={q.id}>{q.name}</option>)}</select></label><button disabled={busy||!selectedTicketIds.length} onClick={()=>void work(()=>assignQueueToSelected(bulkQueueId))}>Applica coda ai selezionati</button></div>}
    <div className="table-wrap"><table><thead><tr><th><input aria-label="Seleziona tutti i ticket gestibili" type="checkbox" checked={tickets.some(t=>t.canManage)&&tickets.filter(t=>t.canManage).every(t=>selectedTicketIds.includes(t.id))} onChange={e=>setSelectedTicketIds(e.target.checked?tickets.filter(t=>t.canManage).map(t=>t.id):[])} disabled={!tickets.some(t=>t.canManage)||busy}/></th><th>Ticket</th><th>Data</th><th>Oggetto</th><th>Categoria</th><th>Coda</th><th>Stato</th><th>Priorità</th><th>Assegnatario</th><th>Dispositivo</th><th>Sede</th><th>Dettagli</th></tr></thead>
      <tbody>{tickets.map(t=><tr key={t.id}><td><input aria-label={`Seleziona OD-${String(t.number).padStart(6,'0')}`} type="checkbox" checked={selectedTicketIds.includes(t.id)} disabled={!t.canManage||busy} onChange={e=>setSelectedTicketIds(ids=>e.target.checked?[...ids,t.id]:ids.filter(id=>id!==t.id))}/></td><td>OD-{String(t.number).padStart(6,'0')}</td><td>{date(t.createdAt)}</td><td>{t.subject}</td><td>{categories[t.category]??t.category}</td><td>{t.queueName||'Senza coda'}</td><td><span className={`status-pill ${t.state==='resolved'||t.state==='closed'?'online':'offline'}`}>{labels[t.state]??t.state}</span></td><td><span className={`status-pill ${t.priority==='urgent'?'danger':t.priority==='high'?'offline':'online'}`}>{priorities[t.priority]??t.priority}</span></td><td>{t.assigneeEmail||'Non assegnato'}</td><td>{visibleDeviceIds.includes(t.deviceId)?<button className="device-link" title="Apri scheda dispositivo" disabled={busy} onClick={()=>void work(()=>onOpenDevice(t.deviceId))}><Icon name="devices" size={17}/>{t.deviceName||'Dispositivo non disponibile'}<Icon name="arrow" size={14}/></button>:<span className="device-name"><Icon name="devices" size={17}/>{t.deviceName||'Dispositivo non disponibile'}</span>}</td><td>{t.siteName||'Sede non disponibile'}</td><td><button disabled={busy} onClick={()=>{setSelected(t);setThread(null);setState(t.state);setResolution('');setPriority(t.priority);setQueueId(t.queueId??'');setAssigneeId(t.assigneeId??'');setAssignees([]);void work(async()=>{await Promise.all([loadThread(t),loadAssignees(t)]);});}}>Apri ticket</button></td></tr>)}</tbody></table></div>
    {!tickets.length&&<p>Nessuna richiesta corrisponde ai filtri o ai tuoi ambiti.</p>}
    {selected&&<section ref={panel} className="ticket-conversation" aria-label="Conversazione ticket">
      <div className="section-heading"><div><span className="eyebrow">CONVERSAZIONE · OD-{String(selected.number).padStart(6,'0')}</span><h3>{selected.subject}</h3><span className="device-name"><Icon name="devices" size={16}/>{selected.deviceName||'Dispositivo non disponibile'}</span></div><button className="secondary compact" type="button" disabled={busy} onClick={()=>{setSelected(null);setThread(null);setError('');}}><Icon name="close" size={15}/>Chiudi dettaglio</button></div>
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
        if(!body&&state===selected.state&&priority===selected.priority&&queueId===(selected.queueId??'')&&assigneeId===(selected.assigneeId??''))return;
        void work(async()=>{await api(`/support/${selected.id}`,'PUT',{expectedVersion:selected.version,state,resolution:body||selected.resolution,addReply:!!body,priority,updatePriority:priority!==selected.priority,queueId:queueId||null,updateQueue:queueId!==(selected.queueId??''),assigneeId:assigneeId||null,updateAssignment:assigneeId!==(selected.assigneeId??'')});setResolution('');await loadThread(selected);await load();});
      }}>
        <label htmlFor="ticket-state">Stato</label><select id="ticket-state" value={state} disabled={busy} onChange={e=>setState(e.target.value)}>{Object.entries(labels).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select>
        <label htmlFor="ticket-priority">Priorità</label><select id="ticket-priority" value={priority} disabled={busy} onChange={e=>setPriority(e.target.value)}>{Object.entries(priorities).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select>
        <label htmlFor="ticket-queue">Coda</label><select id="ticket-queue" value={queueId} disabled={busy} onChange={e=>{const next=e.target.value;setQueueId(next);void work(()=>loadAssignees(selected,next));}}><option value="">Senza coda</option>{selected.queueId&&!queues.some(q=>q.id===selected.queueId)&&<option value={selected.queueId}>{selected.queueName||'Coda non attiva'}</option>}{queues.map(q=><option key={q.id} value={q.id}>{q.name}</option>)}</select>
        <label htmlFor="ticket-assignee">Assegnatario</label><select id="ticket-assignee" value={assigneeId} disabled={busy} onChange={e=>setAssigneeId(e.target.value)}><option value="">Non assegnato</option>{selected.assigneeId&&!assignees.some(a=>a.id===selected.assigneeId)&&<option value={selected.assigneeId}>{selected.assigneeEmail||'Utente non disponibile'}</option>}{assignees.map(a=><option key={a.id} value={a.id}>{a.email}</option>)}</select>
        <label>Risposta visibile al richiedente<textarea ref={reply} maxLength={8000} required={needsReply} disabled={busy} value={resolution} placeholder="Scrivi una risposta all’utente…" aria-describedby="ticket-reply-help" onChange={e=>setResolution(e.target.value)}/></label>
        <div className="composer-footer"><small id="ticket-reply-help">{needsReply?'Per risolvere o chiudere serve una risposta.':'Coda, assegnatario e priorità sono riservati alla console IT; stato e risposta sono visibili all’utente.'}</small><button disabled={busy||(!resolution.trim()&&state===selected.state&&priority===selected.priority&&queueId===(selected.queueId??'')&&assigneeId===(selected.assigneeId??''))}><Icon name="arrow" size={16}/>{resolution.trim()?'Invia risposta':'Salva ticket'}</button></div>
      </form>:thread&&<p>Puoi leggere la conversazione. Per rispondere serve il permesso di gestione ticket.</p>}
      {selected.hasScreenshot&&<dialog ref={preview} className="screenshot-dialog"><div className="section-heading"><strong>Screenshot della richiesta</strong><button className="secondary" onClick={()=>preview.current?.close()}><Icon name="close"/>Chiudi immagine</button></div><img src={`/api/support/${selected.id}/screenshot?inline=true`} alt="Screenshot della richiesta ingrandito"/></dialog>}
    </section>}
  </section>;
}
