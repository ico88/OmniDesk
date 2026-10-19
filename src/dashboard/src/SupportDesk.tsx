import { useEffect, useRef, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';
import { TicketInterventions } from './TicketInterventions';

type Ticket = { id:string; number:number; deviceId:string; siteId:string; deviceName:string|null; siteName:string|null; subject:string; description:string; state:string; priority:string; category:string; origin:string; queueId:string|null; queueName:string|null; assigneeId:string|null; assigneeEmail:string|null; canManage:boolean; resolution:string|null; version:number; createdAt:string; hasScreenshot:boolean };
type TicketSummary = { total:number; open:number; inProgress:number; urgent:number };
type Assignee = { id:string; email:string };
type Queue = { id:string; name:string; siteId?:string|null };
type TicketDevice = {deviceId:string;name:string;siteId:string;siteName:string;isPrimary:boolean};
type LinkableDevice = {deviceId:string;name:string;siteId:string;siteName:string|null};
type Message = {id:number;kind:string;visibility:'public'|'internal';body:string;fromState:string|null;toState:string|null;createdAt:string;isImported:boolean};
type Thread = {version:number;state:string;resolution:string|null;canReply:boolean;canNote:boolean;hasMore:boolean;oldestId:number|null;messages:Message[]};
const labels: Record<string,string> = {open:'Aperto',in_progress:'In lavorazione',resolved:'Risolto',closed:'Chiuso'};
const priorities: Record<string,string> = {low:'Bassa',normal:'Normale',high:'Alta',urgent:'Urgente'};
const categories: Record<string,string> = {general:'Generale',hardware:'Hardware',software:'Software',network:'Rete',account:'Account',other:'Altro'};
const date=(value:string)=>new Date(value).toLocaleString('it-IT');
export function SupportDesk({visibleDeviceIds,onOpenDevice,deviceId,sites=[],devices=[],focusTicketId,onTicketFocused}:{visibleDeviceIds:string[];onOpenDevice:(id:string)=>Promise<void>;deviceId?:string;sites?:{id:string;name:string;isArchived?:boolean}[];devices?:{id:string;name:string;siteId:string|null}[];focusTicketId?:string|null;onTicketFocused?:()=>void}) {
  const [tickets,setTickets] = useState<Ticket[]>([]);
  const [summary,setSummary]=useState<TicketSummary|null>(null);
  const [queues,setQueues]=useState<Queue[]>([]);
  const [selectedQueues,setSelectedQueues]=useState<Queue[]>([]);
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
  const [internalNote,setInternalNote] = useState('');
  const [priority,setPriority]=useState('normal');
  const [queueId,setQueueId]=useState('');
  const [assigneeId,setAssigneeId]=useState('');
  const [assignees,setAssignees]=useState<Assignee[]>([]);
  const [ticketDevices,setTicketDevices]=useState<TicketDevice[]>([]);
  const [linkableDevices,setLinkableDevices]=useState<LinkableDevice[]>([]);
  const [linkDeviceId,setLinkDeviceId]=useState('');
  const [manualOpen,setManualOpen]=useState(false);
  const [manualRequestId,setManualRequestId]=useState('');
  const [manualDevice,setManualDevice]=useState(deviceId??'');
  const [manualSubject,setManualSubject]=useState('');
  const [manualDescription,setManualDescription]=useState('');
  const [manualCategory,setManualCategory]=useState('general');
  const [manualPriority,setManualPriority]=useState('normal');
  const panel=useRef<HTMLElement>(null);
  const reply=useRef<HTMLTextAreaElement>(null);
  const preview=useRef<HTMLDialogElement>(null);
  const needsReply=(state==='resolved'||state==='closed')&&!selected?.resolution;
  async function load() {
    const query=new URLSearchParams();
    if(focusTicketId)query.set('ticketId',focusTicketId);else{if(filterState)query.set('state',filterState);if(search.trim())query.set('search',search.trim());if(deviceId)query.set('deviceId',deviceId);if(siteFilter)query.set('siteId',siteFilter);if(filterPriority)query.set('priority',filterPriority);if(filterCategory)query.set('category',filterCategory);if(filterQueue)query.set('queue',filterQueue);if(assigneeFilter.trim())query.set('assignee',assigneeFilter.trim());}
    const scopeQuery=new URLSearchParams();if(deviceId)scopeQuery.set('deviceId',deviceId);if(siteFilter)scopeQuery.set('siteId',siteFilter);
    const [rows,counts]=await Promise.all([api<Ticket[]>('/support?'+query.toString()),api<TicketSummary>('/support/summary?'+scopeQuery.toString()).catch(()=>null)]);
    const siteIds=[...new Set(rows.map(ticket=>ticket.siteId).filter(Boolean))];
    const queueRows=await Promise.all([api<Queue[]>('/support/queues'),...siteIds.map(id=>api<Queue[]>(`/support/queues?siteId=${encodeURIComponent(id)}`))]);
    const byId=new Map(queueRows.flat().map(queue=>[queue.id,queue]));
    setQueues([...byId.values()].sort((a,b)=>a.name.localeCompare(b.name,'it')));
    setTickets(rows);setSummary(counts);setSelectedTicketIds([]);setSelected(old=>old?rows.find(t=>t.id===old.id)??old:old);
  }
  async function loadThread(ticket:Ticket,before?:number) {
    const data=await api<Thread>(`/support/${ticket.id}/messages${before?'?before='+before:''}`);
    if(before){setThread(old=>old?{...old,hasMore:data.hasMore,oldestId:data.oldestId,messages:[...data.messages,...old.messages]}:data);return;}
    setThread(data);setSelected(old=>old?.id===ticket.id?{...old,version:data.version,state:data.state,resolution:data.resolution}:old);setState(data.state);
  }
  async function loadAssignees(ticket:Ticket,queue?:string) { const query=queue===undefined?'':`?queue=${queue||'none'}`;setAssignees(await api<Assignee[]>(`/support/${ticket.id}/assignees${query}`)); }
  async function loadTicketDevices(ticket:Ticket) {
    const links=await api<TicketDevice[]>(`/support/${ticket.id}/devices`);
    const candidates=ticket.canManage?await api<LinkableDevice[]>(`/support/${ticket.id}/linkable-devices`):[];
    setTicketDevices(links);setLinkableDevices(candidates);setLinkDeviceId(candidates[0]?.deviceId??'');
  }
  async function linkDevice() {
    if(!selected||!linkDeviceId)return;
    await api(`/support/${selected.id}/devices`,'POST',{expectedVersion:selected.version,deviceId:linkDeviceId});
    await load();await loadTicketDevices({...selected,version:selected.version+1});
  }
  async function unlinkDevice(device:TicketDevice) {
    if(!selected||device.isPrimary)return;
    await api(`/support/${selected.id}/devices/${device.deviceId}?expectedVersion=${selected.version}`,'DELETE');
    await load();await loadTicketDevices({...selected,version:selected.version+1});
  }
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
  useEffect(()=>{void work(load);},[deviceId,focusTicketId]);
  useEffect(()=>{
    const target=focusTicketId? tickets.find(ticket=>ticket.id===focusTicketId):undefined;
    if(!target)return;
    setSelected(target);setThread(null);
    void work(async()=>{const [ticketQueues]=await Promise.all([api<Queue[]>(`/support/${target.id}/queues`),loadAssignees(target),loadTicketDevices(target),loadThread(target)]);setSelectedQueues(ticketQueues);onTicketFocused?.();});
  },[focusTicketId,tickets]);
  useEffect(()=>{if(selected)panel.current?.scrollIntoView({behavior:'smooth',block:'start'});},[selected?.id]);
  const createableDevices=deviceId?devices.filter(x=>x.id===deviceId):devices;
  return <section className="panel"><div className="section-heading"><h2><Icon name="support"/>Richieste di assistenza</h2>{createableDevices.length>0&&<button className="secondary compact" type="button" onClick={()=>{setManualDevice(deviceId??createableDevices[0]!.id);setManualRequestId(crypto.randomUUID());setManualOpen(value=>!value);setError('');}}><Icon name="support" size={15}/>{manualOpen?'Chiudi':'Nuovo ticket'}</button>}</div>
    <p>Fino a 100 richieste nei tuoi ambiti autorizzati. Cerca per oggetto o nome del computer.</p>
    {manualOpen&&<form className="chat-composer manual-ticket-form" onSubmit={e=>{e.preventDefault();void work(async()=>{await api('/support/manual','POST',{requestId:manualRequestId,deviceId:manualDevice,subject:manualSubject,description:manualDescription,category:manualCategory,priority:manualPriority});setManualOpen(false);setManualRequestId('');setManualSubject('');setManualDescription('');await load();});}}>
      <label>Dispositivo<select required value={manualDevice} disabled={!!deviceId||busy} onChange={e=>{setManualDevice(e.target.value);setManualRequestId(crypto.randomUUID());}}>{createableDevices.map(item=><option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>Oggetto<input required maxLength={200} value={manualSubject} disabled={busy} onChange={e=>{setManualSubject(e.target.value);setManualRequestId(crypto.randomUUID());}}/></label>
      <label>Descrizione<textarea required maxLength={8000} value={manualDescription} disabled={busy} onChange={e=>{setManualDescription(e.target.value);setManualRequestId(crypto.randomUUID());}}/></label>
      <div className="action-row"><label>Categoria<select value={manualCategory} disabled={busy} onChange={e=>{setManualCategory(e.target.value);setManualRequestId(crypto.randomUUID());}}>{Object.entries(categories).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label><label>Priorità<select value={manualPriority} disabled={busy} onChange={e=>{setManualPriority(e.target.value);setManualRequestId(crypto.randomUUID());}}>{Object.entries(priorities).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label></div>
      <small>Il ticket sarà associato al dispositivo e alla sua sede corrente. La creazione richiede tickets.manage sul dispositivo.</small><button disabled={busy||!manualDevice||!manualSubject.trim()||!manualDescription.trim()}><Icon name="arrow" size={15}/>Crea ticket</button>{error&&<p role="alert">{error}</p>}
    </form>}
    <div className="ticket-summary" aria-label="Riepilogo ticket nei tuoi ambiti"><article><small>Ticket totali</small><strong>{summary?.total??'—'}</strong></article><article><small>Aperti</small><strong>{summary?.open??'—'}</strong></article><article><small>In lavorazione</small><strong>{summary?.inProgress??'—'}</strong></article><article className="urgent"><small>Urgenti non chiusi</small><strong>{summary?.urgent??'—'}</strong></article></div>
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
    <tbody>{tickets.map(t=><tr key={t.id}><td><input aria-label={`Seleziona OD-${String(t.number).padStart(6,'0')}`} type="checkbox" checked={selectedTicketIds.includes(t.id)} disabled={!t.canManage||busy} onChange={e=>setSelectedTicketIds(ids=>e.target.checked?[...ids,t.id]:ids.filter(id=>id!==t.id))}/></td><td>OD-{String(t.number).padStart(6,'0')}</td><td>{date(t.createdAt)}</td><td>{t.subject}</td><td>{categories[t.category]??t.category}</td><td>{t.queueName||'Senza coda'}</td><td><span className={`status-pill ${t.state==='resolved'||t.state==='closed'?'online':'offline'}`}>{labels[t.state]??t.state}</span></td><td><span className={`status-pill ${t.priority==='urgent'?'danger':t.priority==='high'?'offline':'online'}`}>{priorities[t.priority]??t.priority}</span></td><td>{t.assigneeEmail||'Non assegnato'}</td><td>{visibleDeviceIds.includes(t.deviceId)?<button className="device-link" title="Apri scheda dispositivo" disabled={busy} onClick={()=>void work(()=>onOpenDevice(t.deviceId))}><Icon name="devices" size={17}/>{t.deviceName||'Dispositivo non disponibile'}<Icon name="arrow" size={14}/></button>:<span className="device-name"><Icon name="devices" size={17}/>{t.deviceName||'Dispositivo non disponibile'}</span>}</td><td>{t.siteName||'Sede non disponibile'}</td><td><button disabled={busy} onClick={()=>{setSelected(t);setThread(null);setState(t.state);setResolution('');setInternalNote('');setPriority(t.priority);setQueueId(t.queueId??'');setAssigneeId(t.assigneeId??'');setAssignees([]);void work(async()=>{const [ticketQueues]=await Promise.all([api<Queue[]>(`/support/${t.id}/queues`),loadThread(t),loadAssignees(t),loadTicketDevices(t)]);setSelectedQueues(ticketQueues);});}}>Apri ticket</button></td></tr>)}</tbody></table></div>
    {!tickets.length&&<p>Nessuna richiesta corrisponde ai filtri o ai tuoi ambiti.</p>}
    {selected&&<section ref={panel} className="ticket-conversation" aria-label="Conversazione ticket">
      <div className="section-heading"><div><span className="eyebrow">CONVERSAZIONE · OD-{String(selected.number).padStart(6,'0')}</span><h3>{selected.subject}</h3><span className="device-name"><Icon name="devices" size={16}/>{selected.deviceName||'Dispositivo non disponibile'}</span></div><button className="secondary compact" type="button" disabled={busy} onClick={()=>{setSelected(null);setThread(null);setError('');}}><Icon name="close" size={15}/>Chiudi dettaglio</button></div>
      <section className="ticket-linked-devices" aria-label="Dispositivi associati al ticket">
        <div className="section-heading"><div><strong>Dispositivi coinvolti</strong><small>Collega altri dispositivi per raccogliere nello stesso ticket le attività di assistenza.</small></div></div>
        <ul>{ticketDevices.map(item=><li key={item.deviceId}>
          <span className="device-name"><Icon name="devices" size={16}/>{visibleDeviceIds.includes(item.deviceId)?<button className="device-link" disabled={busy} onClick={()=>void work(()=>onOpenDevice(item.deviceId))}>{item.name}</button>:item.name}<small>{item.siteName}{item.isPrimary?' · principale':''}</small></span>
          {!item.isPrimary&&selected.canManage&&<button type="button" className="secondary compact" disabled={busy} aria-label={'Scollega '+item.name} onClick={()=>void work(()=>unlinkDevice(item))}>Scollega</button>}
        </li>)}</ul>
        {selected.canManage&&<form className="chat-composer ticket-device-link-form" onSubmit={e=>{e.preventDefault();void work(linkDevice);}}>
          <label>Associa un dispositivo<select value={linkDeviceId} disabled={busy||!linkableDevices.length} onChange={e=>setLinkDeviceId(e.target.value)}><option value="">{linkableDevices.length?'Seleziona un dispositivo':'Nessun altro dispositivo autorizzato'}</option>{linkableDevices.map(item=><option key={item.deviceId} value={item.deviceId}>{item.name}{item.siteName?' · '+item.siteName:''}</option>)}</select></label>
          <button type="submit" className="secondary compact" disabled={busy||!linkDeviceId}>Collega</button>
        </form>}
      </section>
      <TicketInterventions ticketId={selected.id} version={selected.version} canManage={selected.canManage} onChanged={async()=>{await loadThread(selected);await load();}}/>
      {error&&<p role="alert">{error}</p>}
      <div className="conversation-log" role="log" aria-label="Messaggi del ticket" aria-live="polite">
        <article className="chat-message requester"><div className="chat-meta"><strong>{selected.origin==='operator'?'Ticket inserito da assistenza':'Richiesta dell’utente'}</strong><time>{date(selected.createdAt)}</time></div><p>{selected.description}</p>
          {selected.hasScreenshot&&<button className="chat-image-button" type="button" onClick={()=>preview.current?.showModal()} aria-label="Ingrandisci screenshot"><img src={`/api/support/${selected.id}/screenshot?inline=true`} alt="Screenshot allegato alla richiesta" loading="lazy"/><span><Icon name="search" size={14}/>Ingrandisci screenshot</span></button>}
        </article>
        {!thread&&<p>Caricamento conversazione…</p>}
        {thread?.hasMore&&<button className="secondary compact" disabled={busy} onClick={()=>void work(()=>loadThread(selected,thread.oldestId!))}>Carica messaggi precedenti</button>}
        {thread?.messages.map(m=>m.kind==='state_change'?<p className="chat-event" key={m.id}><Icon name="check" size={14}/>{labels[m.fromState??'']??m.fromState} → {labels[m.toState??'']??m.toState}<time>{date(m.createdAt)}</time></p>:<article className={`chat-message ${m.visibility==='internal'?'internal-note':'staff'}`} key={m.id}><div className="chat-meta"><strong>{m.visibility==='internal'?'Nota interna':m.isImported?'Risposta precedente':'Assistenza IT'}</strong><time>{m.isImported?'Ultima modifica: ':''}{date(m.createdAt)}</time></div><p>{m.body}</p></article>)}
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
        <label htmlFor="ticket-queue">Coda</label><select id="ticket-queue" value={queueId} disabled={busy} onChange={e=>{const next=e.target.value;setQueueId(next);void work(()=>loadAssignees(selected,next));}}><option value="">Senza coda</option>{selected.queueId&&!selectedQueues.some(q=>q.id===selected.queueId)&&<option value={selected.queueId}>{selected.queueName||'Coda non attiva'}</option>}{selectedQueues.map(q=><option key={q.id} value={q.id}>{q.name}</option>)}</select>
        <label htmlFor="ticket-assignee">Assegnatario</label><select id="ticket-assignee" value={assigneeId} disabled={busy} onChange={e=>setAssigneeId(e.target.value)}><option value="">Non assegnato</option>{selected.assigneeId&&!assignees.some(a=>a.id===selected.assigneeId)&&<option value={selected.assigneeId}>{selected.assigneeEmail||'Utente non disponibile'}</option>}{assignees.map(a=><option key={a.id} value={a.id}>{a.email}</option>)}</select>
        <label>Risposta visibile al richiedente<textarea ref={reply} maxLength={8000} required={needsReply} disabled={busy} value={resolution} placeholder="Scrivi una risposta all’utente…" aria-describedby="ticket-reply-help" onChange={e=>setResolution(e.target.value)}/></label>
        <div className="composer-footer"><small id="ticket-reply-help">{needsReply?'Per risolvere o chiudere serve una risposta.':'Coda, assegnatario e priorità sono riservati alla console IT; stato e risposta sono visibili all’utente.'}</small><button disabled={busy||(!resolution.trim()&&state===selected.state&&priority===selected.priority&&queueId===(selected.queueId??'')&&assigneeId===(selected.assigneeId??''))}><Icon name="arrow" size={16}/>{resolution.trim()?'Invia risposta':'Salva ticket'}</button></div>
      </form>:thread&&<p>Puoi leggere la conversazione. Per rispondere serve il permesso di gestione ticket.</p>}
      {thread?.canNote&&<form className="internal-note-composer" onSubmit={e=>{
        e.preventDefault();const body=internalNote.trim();if(!body)return;
        void work(async()=>{await api(`/support/${selected.id}/internal-notes`,'POST',{expectedVersion:thread.version,body});setInternalNote('');await loadThread(selected);await load();});
      }}><label htmlFor="ticket-internal-note"><Icon name="security" size={15}/>Nota interna, visibile solo agli operatori autorizzati<textarea id="ticket-internal-note" maxLength={8000} disabled={busy} value={internalNote} onChange={e=>setInternalNote(e.target.value)} placeholder="Aggiungi un aggiornamento interno…"/></label><button disabled={busy||!internalNote.trim()}><Icon name="arrow" size={15}/>Aggiungi nota</button></form>}
      {selected.hasScreenshot&&<dialog ref={preview} className="screenshot-dialog"><div className="section-heading"><strong>Screenshot della richiesta</strong><button className="secondary" onClick={()=>preview.current?.close()}><Icon name="close"/>Chiudi immagine</button></div><img src={`/api/support/${selected.id}/screenshot?inline=true`} alt="Screenshot della richiesta ingrandito"/></dialog>}
    </section>}
  </section>;
}
