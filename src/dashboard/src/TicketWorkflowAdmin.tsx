import { useEffect, useMemo, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type WorkflowState={code:string;label:string;requiresResolution:boolean};
type Transition={from:string;to:string};
type Definition={states:WorkflowState[];transitions:Transition[]};
type Workflow={id:string;name:string;siteId:string|null;siteName:string|null;scopeKey:string;version:number;activeVersion:number;draftDefinition:Definition;activeDefinition:Definition|null};
const baseline:Definition={states:[{code:'open',label:'Aperto',requiresResolution:false},{code:'in_progress',label:'In lavorazione',requiresResolution:false},{code:'resolved',label:'Risolto',requiresResolution:true},{code:'closed',label:'Chiuso',requiresResolution:true}],transitions:[['open','open'],['open','in_progress'],['open','resolved'],['in_progress','in_progress'],['in_progress','open'],['in_progress','resolved'],['resolved','resolved'],['resolved','open'],['resolved','in_progress'],['resolved','closed'],['closed','closed']].map(([from,to])=>({from,to}))};
const clone=(value:Definition):Definition=>structuredClone(value);

export function TicketWorkflowAdmin({sites,onMessage}:{sites:{id:string;name:string;isArchived:boolean}[];onMessage:(message:string)=>void}) {
  const [workflows,setWorkflows]=useState<Workflow[]>([]);
  const [selectedId,setSelectedId]=useState('');
  const [name,setName]=useState('');
  const [definition,setDefinition]=useState<Definition>(clone(baseline));
  const [newScope,setNewScope]=useState('global');
  const [newName,setNewName]=useState('');
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const selected=useMemo(()=>workflows.find(x=>x.id===selectedId)??null,[workflows,selectedId]);
  async function load() {
    const rows=await api<Workflow[]>('/admin/support-workflows');
    setWorkflows(rows);
    if(!rows.some(x=>x.id===selectedId))setSelectedId(rows[0]?.id??'');
  }
  async function run(action:()=>Promise<void>) {
    setBusy(true);setError('');
    try{await action();}catch(e){setError(e instanceof Error?e.message:'Operazione workflow non riuscita');}
    finally{setBusy(false);}
  }
  useEffect(()=>{void run(load);},[]);
  useEffect(()=>{
    if(!selected)return;
    setName(selected.name);setDefinition(clone(selected.draftDefinition));
  },[selectedId,workflows]);
  const transitionSet=new Set(definition.transitions.map(x=>`${x.from}:${x.to}`));
  function toggle(from:string,to:string,enabled:boolean){
    setDefinition(d=>({...d,transitions:enabled?[...d.transitions,{from,to}]:d.transitions.filter(x=>x.from!==from||x.to!==to)}));
  }
  function addState(){
    const used=new Set(definition.states.map(x=>x.code));let suffix=1;while(used.has(`custom_${suffix}`))suffix++;
    const code=`custom_${suffix}`;
    setDefinition(d=>({states:[...d.states,{code,label:'Nuovo stato',requiresResolution:false}],transitions:[...d.transitions,{from:code,to:code}]}));
  }
  async function create(){
    const siteId=newScope==='global'?null:newScope;
    const created=await api<{id:string}>('/admin/support-workflows','POST',{name:newName,siteId,definition:clone(baseline)});
    setNewName('');await load();setSelectedId(created.id);onMessage('Bozza workflow creata');
  }
  async function save(){
    if(!selected)return;
    await api(`/admin/support-workflows/${selected.id}`,'PUT',{expectedVersion:selected.version,name,definition});
    await load();onMessage('Bozza salvata');
  }
  async function publish(){
    if(!selected)return;
    await api(`/admin/support-workflows/${selected.id}/publish?expectedVersion=${selected.version}`,'POST');
    await load();onMessage('Nuova versione workflow pubblicata');
  }
  async function deactivate(){
    if(!selected)return;
    if(!window.confirm('Disattivare il workflow per i nuovi ticket? I ticket esistenti manterranno la versione associata.'))return;
    await api(`/admin/support-workflows/${selected.id}/deactivate?expectedVersion=${selected.version}`,'POST');
    await load();onMessage('Workflow disattivato per i nuovi ticket');
  }
  return <div className="ticket-workflow-admin">
    {error&&<p role="alert" className="error">{error}</p>}
    <section className="panel settings">
      <div className="section-heading"><div><span className="eyebrow">SERVICE DESK</span><h2><Icon name="settings"/> Workflow ticket</h2></div><span className="badge">Solo amministrazione globale</span></div>
      <p>Le versioni pubblicate non si modificano. Ogni ticket conserva la versione assegnata alla creazione; la sede ha precedenza sul workflow globale.</p>
      <form className="workflow-create" onSubmit={e=>{e.preventDefault();void run(create);}}>
        <label>Nuovo workflow<input value={newName} onChange={e=>setNewName(e.target.value)} required maxLength={100} placeholder="es. Assistenza clienti"/></label>
        <label>Ambito<select value={newScope} onChange={e=>setNewScope(e.target.value)}><option value="global">Globale</option>{sites.filter(s=>!s.isArchived).map(s=><option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <button disabled={busy||!newName.trim()}>Crea bozza</button>
      </form>
    </section>
    <section className="panel settings workflow-editor">
      <label>Workflow<select aria-label="Workflow da modificare" value={selectedId} onChange={e=>setSelectedId(e.target.value)}><option value="">Seleziona un workflow</option>{workflows.map(w=><option key={w.id} value={w.id}>{w.name} · {w.siteName??'Globale'} · {w.activeVersion?`v${w.activeVersion} attiva`:'disattivato'}</option>)}</select></label>
      {!selected?<p>Nessun workflow personalizzato: i ticket usano la policy predefinita.</p>:<>
        <div className="section-heading"><div><h3>{selected.name}</h3><small>{selected.siteName??'Ambito globale'} · Bozza #{selected.version} · {selected.activeVersion?`Versione attiva ${selected.activeVersion}`:'Non attivo'}</small></div></div>
        <label>Nome<input value={name} onChange={e=>setName(e.target.value)} required maxLength={100}/></label>
        <div className="table-wrap"><table><thead><tr><th>Codice</th><th>Etichetta</th><th>Richiede risposta risolutiva</th><th></th></tr></thead><tbody>{definition.states.map((state,index)=><tr key={state.code}>
          <td><code>{state.code}</code>{['open','resolved','closed'].includes(state.code)&&<small> · richiesto dal sistema</small>}</td>
          <td><input aria-label={`Etichetta ${state.code}`} value={state.label} maxLength={80} onChange={e=>setDefinition(d=>({...d,states:d.states.map((s,i)=>i===index?{...s,label:e.target.value}:s)}))}/></td>
          <td><input aria-label={`Richiede risoluzione ${state.code}`} type="checkbox" checked={state.requiresResolution} disabled={state.code==='closed'||state.code==='resolved'} onChange={e=>setDefinition(d=>({...d,states:d.states.map((s,i)=>i===index?{...s,requiresResolution:e.target.checked}:s)}))}/></td>
          <td>{['open','resolved','closed'].includes(state.code)?null:<button className="secondary compact" type="button" disabled={busy} onClick={()=>setDefinition(d=>({states:d.states.filter(s=>s.code!==state.code),transitions:d.transitions.filter(t=>t.from!==state.code&&t.to!==state.code)}))}>Rimuovi</button>}</td>
        </tr>)}</tbody></table></div>
        <button type="button" className="secondary compact" disabled={busy||definition.states.length>=16} onClick={addState}><Icon name="plus"/>Aggiungi stato</button>
        <h3>Transizioni consentite</h3><p>Ogni stato resta sullo stesso stato finché non viene scelta una transizione. Chiuso è sempre terminale.</p>
        <div className="table-wrap workflow-transition-wrap"><table className="workflow-transition-matrix"><thead><tr><th>Da / a</th>{definition.states.map(s=><th key={s.code}>{s.label}</th>)}</tr></thead><tbody>{definition.states.map(from=><tr key={from.code}><th>{from.label}</th>{definition.states.map(to=>{const checked=transitionSet.has(`${from.code}:${to.code}`);const forced=from.code===to.code||from.code==='closed'&&to.code!=='closed';return <td key={to.code}><input aria-label={`${from.label} a ${to.label}`} type="checkbox" checked={checked} disabled={busy||forced} onChange={e=>toggle(from.code,to.code,e.target.checked)}/></td>;})}</tr>)}</tbody></table></div>
        <div className="workflow-actions"><button disabled={busy||!name.trim()} onClick={()=>void run(save)}><Icon name="check"/>Salva bozza</button><button className="secondary" disabled={busy||!name.trim()} onClick={()=>void run(publish)}><Icon name="shield"/>Pubblica nuova versione</button>{selected.activeVersion>0&&<button className="secondary" disabled={busy} onClick={()=>void run(deactivate)}>Disattiva per nuovi ticket</button>}</div>
      </>}
    </section>
  </div>;
}
