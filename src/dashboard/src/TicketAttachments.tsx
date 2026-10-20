import { useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Attachment={id:string;fileName:string;mediaType:string;sizeBytes:number;visibility:'public'|'internal';scanState:'quarantined'|'scanning'|'clean'|'infected'|'scan_failed';createdAt:string;messageId:number|null};
export type AttachmentMessageChoice={id:number;label:string;visibility:'public'|'internal'};
const states:Record<Attachment['scanState'],string>={quarantined:'In quarantena',scanning:'Scansione in corso',clean:'Scansione completata',infected:'File bloccato',scan_failed:'Scansione non riuscita'};
const size=(bytes:number)=>bytes<1024?`${bytes} B`:bytes<1024*1024?`${(bytes/1024).toFixed(0)} KB`:`${(bytes/1024/1024).toFixed(1)} MB`;

export function TicketAttachments({basePath,version,canManage,canNote,canUpload=canManage,messages=[],onChanged}:{basePath:string;version:number;canManage:boolean;canNote:boolean;canUpload?:boolean;messages?:AttachmentMessageChoice[];onChanged:()=>Promise<void>}) {
  const [rows,setRows]=useState<Attachment[]>([]);const [file,setFile]=useState<File|null>(null);const [visibility,setVisibility]=useState<'public'|'internal'>('public');
  const [consent,setConsent]=useState(false);const [messageId,setMessageId]=useState('');const [busy,setBusy]=useState(false);const [error,setError]=useState('');
  async function load(){setRows(await api<Attachment[]>(`${basePath}/attachments`));}
  useEffect(()=>{void load().catch(e=>setError(e instanceof Error?e.message:'Elenco allegati non disponibile'));},[basePath,version]);
  useEffect(()=>{if(!rows.some(item=>item.scanState==='quarantined'||item.scanState==='scanning'))return;const timer=window.setInterval(()=>{void load().catch(()=>{});},3000);return()=>window.clearInterval(timer);},[rows,basePath]);
  async function upload(event:FormEvent){event.preventDefault();if(!file)return;setBusy(true);setError('');
    try{const form=new FormData();form.append('file',file,file.name);form.append('expectedVersion',String(version));form.append('visibility',visibility);form.append('consent',String(consent));if(messageId)form.append('messageId',messageId);
      await api(`${basePath}/attachments`,'POST',form);setFile(null);setConsent(false);setMessageId('');await onChanged();await load();
    }catch(e){setError(e instanceof Error?e.message:'Caricamento allegato non riuscito');}finally{setBusy(false);}}
  async function retryScan(id:string){setBusy(true);setError('');try{await api(`${basePath}/attachments/${id}/retry-scan`,'POST',{});await load();}catch(e){setError(e instanceof Error?e.message:'Nuovo tentativo di scansione non riuscito');}finally{setBusy(false);}}
  return <section className="ticket-attachments" aria-label="Allegati ticket">
    <div className="section-heading"><div><strong><Icon name="paperclip" size={16}/>Allegati</strong><small>Massimo 10 MiB ciascuno. Il download si abilita solo dopo scansione antivirus pulita.</small></div></div>
    {error&&<p role="alert">{error}</p>}
    {rows.length?<ul>{rows.map(item=><li key={item.id}><span><strong>{item.fileName}</strong><small>{size(item.sizeBytes)} · {item.visibility==='internal'?'Interno':'Visibile al richiedente'} · {new Date(item.createdAt).toLocaleString('it-IT')}</small></span><span className={`attachment-scan ${item.scanState}`} role="status">{item.scanState==='clean'?<a href={`/api${basePath}/attachments/${item.id}`}><Icon name="download" size={14}/>Scarica</a>:<><Icon name={item.scanState==='infected'?'security':'clock'} size={14}/>{states[item.scanState]}</>}</span>{item.scanState==='scan_failed'&&canManage&&<button className="secondary compact" disabled={busy} onClick={()=>void retryScan(item.id)}><Icon name="refresh" size={14}/>Riprova scansione</button>}</li>)}</ul>:<p>Nessun allegato.</p>}
    {canUpload&&<form className="ticket-attachment-upload" onSubmit={e=>void upload(e)}><label>Seleziona file (massimo 10 MiB)<input type="file" disabled={busy} onChange={e=>{setFile(e.target.files?.[0]??null);setConsent(false);}}/></label>
      {canNote&&<label>Visibilità<select value={visibility} disabled={busy} onChange={e=>{const next=e.target.value as 'public'|'internal';setVisibility(next);setConsent(false);if(messages.find(message=>String(message.id)===messageId)?.visibility!==next)setMessageId('');}}><option value="public">Condividi con il richiedente</option><option value="internal">Solo assistenza IT</option></select></label>}
      {messages.some(message=>message.visibility===visibility)&&<label>Collega a un messaggio (facoltativo)<select value={messageId} disabled={busy} onChange={e=>setMessageId(e.target.value)}><option value="">Allegato indipendente</option>{messages.filter(message=>message.visibility===visibility).map(message=><option key={message.id} value={message.id}>{message.label}</option>)}</select></label>}
      {visibility==='public'&&<label className="attachment-consent"><input type="checkbox" checked={consent} disabled={busy} onChange={e=>setConsent(e.target.checked)}/>Confermo la condivisione del file con il richiedente.</label>}
      <button className="secondary compact" disabled={busy||!file||(visibility==='public'&&!consent)}><Icon name="paperclip" size={15}/>{busy?'Caricamento…':'Carica in quarantena'}</button>
    </form>}
  </section>;
}
