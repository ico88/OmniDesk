import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Candidate = {id:string;deviceId:string;deviceName:string;artifactType:string;label:string;capability:string;state:string;occurredAt:string;completedAt:string|null};
type Intervention = {deviceId:string;deviceName:string;artifactType:string;artifactId:string;capability:string;label:string;state:string;currentState:string|null;occurredAt:string;completedAt:string|null};
type Summary = {id:string;deviceId:string;deviceName:string;artifactType:string;artifactId:string;summary:string;outcome:string;followUp:string|null;recordedAt:string;authorName:string};
const date=(value:string)=>new Date(value).toLocaleString('it-IT');
const states:Record<string,string>={queued:'In coda',claimed:'In esecuzione',running:'In esecuzione',pending:'In attesa',approved:'Approvata',awaiting_consent:'In attesa di consenso',succeeded:'Completata',failed:'Non riuscita',cancelled:'Annullata',closed:'Terminata',expired:'Scaduta'};
const outcomes:Record<string,string>={completed:'Completato',partial:'Parzialmente completato',not_completed:'Non completato',no_change:'Nessuna modifica',follow_up:'Richiede seguito'};
function duration(start:string,end:string|null) {
  if(!end)return null;
  const seconds=Math.floor((Date.parse(end)-Date.parse(start))/1000);
  if(!Number.isFinite(seconds)||seconds<0)return null;
  if(seconds<60)return `${seconds} secondi`;
  const minutes=Math.floor(seconds/60);
  if(minutes<60)return `${minutes} min`;
  const hours=Math.floor(minutes/60);
  const remainder=minutes%60;
  return remainder?`${hours} h ${remainder} min`:`${hours} h`;
}

export function TicketInterventions({ticketId,version,canManage,onChanged}:{ticketId:string;version:number;canManage:boolean;onChanged:()=>Promise<void>}) {
  const [candidates,setCandidates]=useState<Candidate[]>([]);
  const [items,setItems]=useState<Intervention[]>([]);
  const [summaries,setSummaries]=useState<Summary[]>([]);
  const [selected,setSelected]=useState('');
  const [summaryArtifact,setSummaryArtifact]=useState('');
  const [summaryText,setSummaryText]=useState('');
  const [outcome,setOutcome]=useState('completed');
  const [followUp,setFollowUp]=useState('');
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  async function load() {
    const [linked,available,history]=await Promise.all([
      api<Intervention[]>(`/support/${ticketId}/interventions`),
      canManage?api<Candidate[]>(`/support/${ticketId}/intervention-candidates`):Promise.resolve([]),
      api<Summary[]>(`/support/${ticketId}/intervention-summaries`)
    ]);
    setItems(linked);setCandidates(available);setSummaries(history);
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
  async function saveSummary(item:Intervention) {
    setBusy(true);setError('');
    try {
      await api(`/support/${ticketId}/intervention-summaries`,'POST',{expectedVersion:version,deviceId:item.deviceId,artifactType:item.artifactType,artifactId:item.artifactId,summary:summaryText,outcome,followUp:followUp.trim()||null});
      setSummaryArtifact('');setSummaryText('');setFollowUp('');setOutcome('completed');
      await onChanged();await load();
    } catch(e) {setError(e instanceof Error?e.message:'Salvataggio riepilogo non riuscito');}
    finally {setBusy(false);}
  }
  return <section className="ticket-interventions" aria-label="Interventi RMM associati">
    <div className="section-heading"><div><strong><Icon name="activity" size={16}/>Interventi RMM</strong><small>Sessioni remote e operazioni registrate sui dispositivi collegati al ticket.</small></div></div>
    {error&&<p role="alert">{error}</p>}
    {items.length?<ul>{items.map((item,i)=>{const elapsed=duration(item.occurredAt,item.completedAt);const currentState=item.currentState??item.state;const key=`${item.artifactType}:${item.deviceId}:${item.artifactId}`;return <li key={`${key}:${i}`}><span><strong>{item.label}</strong><small>{item.deviceName} · Stato: {states[currentState]??currentState}{currentState!==item.state?` · Al collegamento: ${states[item.state]??item.state}`:''}</small><small>Avvio: {date(item.occurredAt)}{item.completedAt?` · Completamento: ${date(item.completedAt)}`:''}{elapsed?` · Durata: ${elapsed}`:''}</small></span><div className="intervention-item-actions"><code>{item.artifactId.slice(0,8)}</code>{canManage&&<button className="secondary compact" disabled={busy} onClick={()=>setSummaryArtifact(current=>current===key?'':key)}>Aggiungi riepilogo</button>}</div>{summaryArtifact===key&&<form className="intervention-summary-form" onSubmit={e=>{e.preventDefault();void saveSummary(item);}}>
      <label>Riepilogo dell’intervento<textarea aria-label="Riepilogo dell’intervento" value={summaryText} onChange={e=>setSummaryText(e.target.value)} required maxLength={4000}/></label>
      <label>Esito<select aria-label="Esito intervento" value={outcome} onChange={e=>setOutcome(e.target.value)}>{Object.entries(outcomes).map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label>
      <label>Attività successive (facoltativo)<textarea aria-label="Attività successive" value={followUp} onChange={e=>setFollowUp(e.target.value)} maxLength={1000}/></label>
      <button disabled={busy||!summaryText.trim()||(outcome==='follow_up'&&!followUp.trim())}>{busy?'Salvataggio…':'Salva riepilogo'}</button>
    </form>}</li>;})}</ul>:<p>Nessun intervento RMM collegato.</p>}
    {summaries.length>0&&<section className="intervention-summary-history" aria-label="Riepiloghi degli interventi"><h3>Riepiloghi registrati</h3><ul>{summaries.map(x=><li key={x.id}><strong>{x.deviceName} · {outcomes[x.outcome]??x.outcome}</strong><p>{x.summary}</p>{x.followUp&&<small>Seguito: {x.followUp}</small>}<small>{x.authorName} · {date(x.recordedAt)} · attività {x.artifactId.slice(0,8)}</small></li>)}</ul></section>}
    {canManage&&<div className="ticket-intervention-link"><label htmlFor="ticket-rmm-item">Collega un’attività esistente<select id="ticket-rmm-item" value={selected} disabled={busy||!candidates.length} onChange={e=>setSelected(e.target.value)}><option value="">{candidates.length?'Seleziona attività':'Nessuna attività disponibile nei dispositivi collegati'}</option>{candidates.map(item=><option key={`${item.artifactType}:${item.id}`} value={`${item.artifactType}:${item.id}`}>{item.deviceName} · {item.label} · {item.state} · {date(item.occurredAt)}</option>)}</select></label><button className="secondary compact" disabled={busy||!selected} onClick={()=>void link()}><Icon name="link" size={15}/>Collega</button></div>}
  </section>;
}
