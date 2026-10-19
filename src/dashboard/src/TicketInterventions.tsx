import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Candidate = {id:string;deviceId:string;deviceName:string;artifactType:string;label:string;capability:string;state:string;occurredAt:string};
type Intervention = {deviceId:string;deviceName:string;artifactType:string;artifactId:string;capability:string;label:string;state:string;occurredAt:string};
const date=(value:string)=>new Date(value).toLocaleString('it-IT');

export function TicketInterventions({ticketId,version,canManage,onChanged}:{ticketId:string;version:number;canManage:boolean;onChanged:()=>Promise<void>}) {
  const [candidates,setCandidates]=useState<Candidate[]>([]);
  const [items,setItems]=useState<Intervention[]>([]);
  const [selected,setSelected]=useState('');
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  async function load() {
    const [linked,available]=await Promise.all([
      api<Intervention[]>(`/support/${ticketId}/interventions`),
      canManage?api<Candidate[]>(`/support/${ticketId}/intervention-candidates`):Promise.resolve([])
    ]);
    setItems(linked);setCandidates(available);
    setSelected(current=>available.some(x=>`${x.artifactType}:${x.id}`===current)?current:available[0]?`${available[0].artifactType}:${available[0].id}`:'');
  }
  useEffect(()=>{void load().catch(e=>setError(e instanceof Error?e.message:'Caricamento interventi non riuscito'));},[ticketId,version,canManage]);
  async function link() {
    const item=candidates.find(x=>`${x.artifactType}:${x.id}`===selected);
    if(!item)return;
    setBusy(true);setError('');
    try {
      await api(`/support/${ticketId}/interventions`,'POST',{expectedVersion:version,deviceId:item.deviceId,artifactType:item.artifactType,artifactId:item.id});
      await onChanged();await load();
    } catch(e) {setError(e instanceof Error?e.message:'Collegamento intervento non riuscito');}
    finally {setBusy(false);}
  }
  return <section className="ticket-interventions" aria-label="Interventi RMM associati">
    <div className="section-heading"><div><strong><Icon name="activity" size={16}/>Interventi RMM</strong><small>Sessioni remote e operazioni registrate sui dispositivi collegati al ticket.</small></div></div>
    {error&&<p role="alert">{error}</p>}
    {items.length?<ul>{items.map((item,i)=><li key={`${item.artifactType}:${item.artifactId}:${i}`}><span><strong>{item.label}</strong><small>{item.deviceName} · {item.state} · {date(item.occurredAt)}</small></span><code>{item.artifactId.slice(0,8)}</code></li>)}</ul>:<p>Nessun intervento RMM collegato.</p>}
    {canManage&&<div className="ticket-intervention-link"><label htmlFor="ticket-rmm-item">Collega un’attività esistente<select id="ticket-rmm-item" value={selected} disabled={busy||!candidates.length} onChange={e=>setSelected(e.target.value)}><option value="">{candidates.length?'Seleziona attività':'Nessuna attività disponibile nei dispositivi collegati'}</option>{candidates.map(item=><option key={`${item.artifactType}:${item.id}`} value={`${item.artifactType}:${item.id}`}>{item.deviceName} · {item.label} · {item.state} · {date(item.occurredAt)}</option>)}</select></label><button className="secondary compact" disabled={busy||!selected} onClick={()=>void link()}><Icon name="link" size={15}/>Collega</button></div>}
  </section>;
}
