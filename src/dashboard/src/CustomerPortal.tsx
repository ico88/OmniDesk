import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';
import { TicketAttachments } from './TicketAttachments';
import './portal.css';

type PortalDevice = { deviceId:string; deviceName:string; siteId:string; siteName:string };
type Ticket = { id:string; number:number; deviceId:string; deviceName:string; siteName:string; subject:string; state:string; category:string; version:number; createdAt:string; updatedAt:string; hasScreenshot:boolean };
type Message = { id:number; kind:string; body:string; fromState:string|null; toState:string|null; createdAt:string };
type Detail = { ticket:Ticket; messages:Message[] };
const states:Record<string,string>={open:'Aperto',in_progress:'In lavorazione',resolved:'Risolto',closed:'Chiuso'};
const categories=[['general','Generale'],['hardware','Hardware'],['software','Software'],['network','Rete'],['account','Account'],['other','Altro']];
const date=(value:string)=>new Date(value).toLocaleString('it-IT');

export function CustomerPortal({onMessage}:{onMessage:(message:string)=>void}) {
  const [devices,setDevices]=useState<PortalDevice[]>([]);
  const [tickets,setTickets]=useState<Ticket[]>([]);
  const [selectedId,setSelectedId]=useState('');
  const [detail,setDetail]=useState<Detail|null>(null);
  const [deviceId,setDeviceId]=useState('');
  const [subject,setSubject]=useState('');
  const [description,setDescription]=useState('');
  const [screenshot,setScreenshot]=useState<File|null>(null);
  const [screenshotConsent,setScreenshotConsent]=useState(false);
  const [category,setCategory]=useState('general');
  const [reply,setReply]=useState('');
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');

  async function load() {
    const [available,owned]=await Promise.all([api<PortalDevice[]>('/portal/devices'),api<Ticket[]>('/portal/tickets')]);
    setDevices(available);setTickets(owned);
    if(selectedId&&!owned.some(ticket=>ticket.id===selectedId)){setSelectedId('');setDetail(null);}
  }
  useEffect(()=>{let active=true;void Promise.all([api<PortalDevice[]>('/portal/devices'),api<Ticket[]>('/portal/tickets')]).then(([available,owned])=>{
    if(active){setDevices(available);setTickets(owned);}
  }).catch(e=>{if(active)setError(e instanceof Error?e.message:'I ticket personali non sono disponibili');});return()=>{active=false;};},[]);
  useEffect(()=>{if(!selectedId){setDetail(null);return;}let active=true;setDetail(null);
    void api<Detail>(`/portal/tickets/${selectedId}`).then(value=>{if(active)setDetail(value);}).catch(e=>{if(active){setError(e instanceof Error?e.message:'Conversazione non disponibile');setSelectedId('');}});
    return()=>{active=false;};},[selectedId]);

  async function run(action:()=>Promise<void>){setBusy(true);setError('');try{await action();}catch(e){setError(e instanceof Error?e.message:'Operazione non riuscita');}finally{setBusy(false);}}
  async function createTicket(){
    let screenshotBase64:string|undefined;
    if(screenshot){
      if(!screenshotConsent)throw new Error('Conferma il consenso prima di allegare lo screenshot.');
      if(screenshot.type!=='image/png'||screenshot.size>524288)throw new Error('Scegli un PNG fino a 512 KB.');
      screenshotBase64=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onerror=()=>reject(new Error('Impossibile leggere il file selezionato.'));reader.onload=()=>resolve(String(reader.result).split(',')[1]??'');reader.readAsDataURL(screenshot);});
    }
    const created=await api<Ticket>('/portal/tickets','POST',{requestId:crypto.randomUUID(),deviceId,subject,description,category,screenshotBase64,screenshotConsent});
    setSubject('');setDescription('');setCategory('general');setScreenshot(null);setScreenshotConsent(false);await load();setSelectedId(created.id);onMessage(`Ticket #${created.number} creato`);
  }
  async function sendReply(){
    if(!detail||!reply.trim())return;
    await api(`/portal/tickets/${detail.ticket.id}/messages`,'POST',{expectedVersion:detail.ticket.version,body:reply});
    setReply('');await load();setDetail(await api<Detail>(`/portal/tickets/${detail.ticket.id}`));onMessage('Risposta inviata');
  }

  return <div className="portal-layout">
    <section className="panel portal-tickets">
      <div className="section-heading"><div><span className="eyebrow">AREA PERSONALE</span><h2>I miei ticket <span className="count">{tickets.length}</span></h2></div><button className="secondary compact" disabled={busy} onClick={()=>void run(load)}><Icon name="refresh"/>Aggiorna</button></div>
      {tickets.length===0?<p className="empty-state">Non hai ancora richieste di assistenza.</p>:<div className="portal-ticket-list">{tickets.map(ticket=><button type="button" key={ticket.id} className={`portal-ticket ${selectedId===ticket.id?'selected':''}`} onClick={()=>{setError('');setSelectedId(ticket.id);}}><span><strong>#{ticket.number} · {ticket.subject}</strong><small>{ticket.deviceName} · {ticket.siteName}</small></span><span className="status-pill">{states[ticket.state]??ticket.state}</span></button>)}</div>}
    </section>
    <section className="panel portal-content">
      {error&&<p role="alert" className="error">{error}</p>}
      {selectedId&&<>
        {!detail?<p>Caricamento conversazione…</p>:<><div className="section-heading"><div><span className="eyebrow">TICKET #{detail.ticket.number} · {states[detail.ticket.state]??detail.ticket.state}</span><h2>{detail.ticket.subject}</h2><small>{detail.ticket.deviceName} · {detail.ticket.siteName} · {date(detail.ticket.createdAt)}</small></div></div>
          <div className="conversation-log" role="log" aria-label="Conversazione del ticket" aria-live="polite">{detail.messages.map(message=>message.kind==='state_change'?<p className="chat-event" key={message.id}><Icon name="check" size={14}/>{states[message.fromState??'']??message.fromState} → {states[message.toState??'']??message.toState}<time>{date(message.createdAt)}</time></p>:<article className={`chat-message ${message.kind==='request'?'requester':'staff'}`} key={message.id}><div className="chat-meta"><strong>{message.kind==='request'?'La tua richiesta':'Assistenza OmniDesk'}</strong><time>{date(message.createdAt)}</time></div><p>{message.body}</p>{message.kind==='request'&&detail.ticket.hasScreenshot&&<a className="chat-image-button" href={`/api/portal/tickets/${detail.ticket.id}/screenshot`} target="_blank" rel="noreferrer"><img src={`/api/portal/tickets/${detail.ticket.id}/screenshot`} alt="Screenshot allegato alla richiesta" loading="lazy"/><span><Icon name="search" size={14}/>Apri screenshot</span></a>}</article>)}</div>
          <TicketAttachments basePath={`/portal/tickets/${detail.ticket.id}`} version={detail.ticket.version} canManage={false} canNote={false} canUpload={detail.ticket.state==='open'||detail.ticket.state==='in_progress'} onChanged={async()=>{await load();setDetail(await api<Detail>(`/portal/tickets/${detail.ticket.id}`));}}/>
          {detail.ticket.state==='open'||detail.ticket.state==='in_progress'?<form className="chat-composer" onSubmit={e=>{e.preventDefault();void run(sendReply);}}><label>Rispondi all’assistenza<textarea required maxLength={8000} value={reply} disabled={busy} onChange={e=>setReply(e.target.value)} placeholder="Scrivi un messaggio…"/></label><div className="composer-footer"><small>La risposta sarà visibile al team di assistenza.</small><button disabled={busy||!reply.trim()}><Icon name="arrow" size={16}/>Invia risposta</button></div></form>:<p className="portal-closed">Questo ticket è chiuso. Se serve altro, apri una nuova richiesta.</p>}
        </>}
      </>}
      {!selectedId&&<form className="portal-create settings" onSubmit={e=>{e.preventDefault();void run(createTicket);}}><span className="eyebrow">NUOVA RICHIESTA</span><h2>Richiedi assistenza</h2>
        {devices.length===0?<p className="empty-state">Il tuo account non è ancora associato a dispositivi. Contatta l’assistenza IT per configurare l’accesso personale.</p>:<>
          <label>Dispositivo<select required value={deviceId} onChange={e=>setDeviceId(e.target.value)}><option value="">Seleziona un dispositivo</option>{devices.map(device=><option key={device.deviceId} value={device.deviceId}>{device.deviceName} · {device.siteName}</option>)}</select></label>
          <label>Oggetto<input required maxLength={200} value={subject} onChange={e=>setSubject(e.target.value)} placeholder="Descrivi brevemente il problema"/></label>
          <label>Categoria<select value={category} onChange={e=>setCategory(e.target.value)}>{categories.map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label>
          <label>Descrizione<textarea required maxLength={8000} rows={6} value={description} onChange={e=>setDescription(e.target.value)} placeholder="Aggiungi i dettagli utili per l’assistenza…"/></label>
          <label>Screenshot facoltativo (PNG, massimo 512 KB)<input type="file" accept="image/png,.png" onChange={e=>{setScreenshot(e.target.files?.[0]??null);setScreenshotConsent(false);}}/></label>
          {screenshot&&<label className="portal-consent"><input type="checkbox" checked={screenshotConsent} onChange={e=>setScreenshotConsent(e.target.checked)}/>Acconsento ad allegare e condividere questo screenshot con il team di assistenza per la gestione del ticket.</label>}
          <button disabled={busy||!deviceId||(screenshot!==null&&!screenshotConsent)}>Apri ticket</button>
          <small>Il dispositivo è associato al tuo account dall’amministratore. Il portale non concede accesso remoto o altri permessi RMM.</small>
        </>}
      </form>}
    </section>
  </div>;
}
