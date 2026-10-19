import { useEffect, useMemo, useState } from 'react';
import { CalendarClock, Clock3, Plus, Save } from 'lucide-react';
import { api } from './api';

const days: {key:string;label:string}[] = [
  {key:'monday',label:'Lunedì'},{key:'tuesday',label:'Martedì'},{key:'wednesday',label:'Mercoledì'},
  {key:'thursday',label:'Giovedì'},{key:'friday',label:'Venerdì'},{key:'saturday',label:'Sabato'},{key:'sunday',label:'Domenica'},
];
type Range={start:string;end:string};
type Definition={firstResponseMinutes:number;resolutionMinutes:number;calendar:{timeZoneId:string;weeklyHours:Record<string,Range[]>;holidays:string[]}};
type Version={version:number;name:string;siteId:string|null;definition:Definition;publishedAt:string};
type Policy={id:string;name:string;siteId:string|null;scopeKey:string;currentVersion:number;version:number;versions:Version[]};
type Scopes={canManageGlobal:boolean;sites:{id:string;name:string}[]};
const freshDefinition=():Definition=>({firstResponseMinutes:60,resolutionMinutes:480,calendar:{timeZoneId:Intl.DateTimeFormat().resolvedOptions().timeZone||'UTC',weeklyHours:{monday:[{start:'09:00',end:'17:00'}],tuesday:[{start:'09:00',end:'17:00'}],wednesday:[{start:'09:00',end:'17:00'}],thursday:[{start:'09:00',end:'17:00'}],friday:[{start:'09:00',end:'17:00'}]},holidays:[]}});
const clone=<T,>(v:T):T=>structuredClone(v);
const minutes=(value:string)=>{const [h,m]=value.split(':').map(Number);return h*60+m;};
const time=(value:number)=>`${String(Math.floor(value/60)).padStart(2,'0')}:${String(value%60).padStart(2,'0')}`;
const normalizeDefinition=(source:Definition):Definition=>({...source,calendar:{...source.calendar,weeklyHours:Object.fromEntries(days.flatMap(day=>{const match=Object.entries(source.calendar.weeklyHours).find(([key])=>key.toLowerCase()===day.key);return match?[[day.key,match[1]]]:[]}))}});

export function SlaPolicyAdmin({onMessage}:{onMessage:(message:string)=>void}) {
  const [scopes,setScopes]=useState<Scopes>({canManageGlobal:false,sites:[]});
  const [policies,setPolicies]=useState<Policy[]>([]);
  const [selectedId,setSelectedId]=useState('');
  const [policyName,setPolicyName]=useState('');
  const [scope,setScope]=useState('global');
  const [name,setName]=useState('');
  const [definition,setDefinition]=useState<Definition>(freshDefinition);
  const [holidayText,setHolidayText]=useState('');
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const selected=useMemo(()=>policies.find(p=>p.id===selectedId)??null,[policies,selectedId]);

  async function load(){
    const [available,rows]=await Promise.all([api<Scopes>('/admin/support-sla/scopes'),api<Policy[]>('/admin/support-sla/policies')]);
    setScopes(available);setPolicies(rows);
    setScope(current=>available.canManageGlobal||available.sites.some(site=>site.id===current)?current:available.sites[0]?.id??'');
    if(!rows.some(p=>p.id===selectedId))setSelectedId(rows[0]?.id??'');
  }
  async function run(action:()=>Promise<void>){setBusy(true);setError('');try{await action();}catch(e){setError(e instanceof Error?e.message:'Operazione SLA non riuscita');}finally{setBusy(false);}}
  useEffect(()=>{void run(load);},[]);
  useEffect(()=>{
    if(!selected)return;
    setPolicyName(selected.name);
    const latest=selected.versions.find(v=>v.version===selected.currentVersion);
    if(latest){setName(latest.name);setDefinition(normalizeDefinition(latest.definition));setHolidayText(latest.definition.calendar.holidays.join('\n'));}
  },[selectedId,policies]);

  function setDay(key:string,enabled:boolean){setDefinition(d=>{const weeklyHours={...d.calendar.weeklyHours};if(enabled)weeklyHours[key]=[{start:'09:00',end:'17:00'}];else delete weeklyHours[key];return {...d,calendar:{...d.calendar,weeklyHours}};});}
  function setRange(key:string,index:number,field:keyof Range,value:string){setDefinition(d=>({...d,calendar:{...d.calendar,weeklyHours:{...d.calendar.weeklyHours,[key]:d.calendar.weeklyHours[key].map((range,i)=>i===index?{...range,[field]:value}:range)}}}));}
  function removeRange(key:string,index:number){setDefinition(d=>{const ranges=d.calendar.weeklyHours[key].filter((_,i)=>i!==index);const weeklyHours={...d.calendar.weeklyHours};if(ranges.length)weeklyHours[key]=ranges;else delete weeklyHours[key];return {...d,calendar:{...d.calendar,weeklyHours}};});}
  function splitRange(key:string){setDefinition(d=>{const ranges=[...(d.calendar.weeklyHours[key]??[])];const index=ranges.reduce((best,range,i)=>minutes(range.end)-minutes(range.start)>minutes(ranges[best].end)-minutes(ranges[best].start)?i:best,0);const current=ranges[index];const length=minutes(current.end)-minutes(current.start);if(length<120)return d;const middle=Math.floor((minutes(current.start)+minutes(current.end))/2/15)*15;const breakStart=middle-30,breakEnd=middle+30;if(breakStart-minutes(current.start)<30||minutes(current.end)-breakEnd<30)return d;ranges.splice(index,1,{start:current.start,end:time(breakStart)},{start:time(breakEnd),end:current.end});return {...d,calendar:{...d.calendar,weeklyHours:{...d.calendar.weeklyHours,[key]:ranges}}};});}
  function makeDefinition(){
    const holidays=holidayText.split(/\r?\n/).map(x=>x.trim()).filter(Boolean);
    if(holidays.some(x=>!/^\d{4}-\d{2}-\d{2}$/.test(x)||Number.isNaN(Date.parse(`${x}T00:00:00Z`))||new Date(`${x}T00:00:00Z`).toISOString().slice(0,10)!==x))throw new Error('Inserisci festività reali nel formato YYYY-MM-DD.');
    if(!Object.keys(definition.calendar.weeklyHours).length)throw new Error('Configura almeno un giorno lavorativo.');
    return {...definition,calendar:{...definition.calendar,weeklyHours:Object.fromEntries(Object.entries(definition.calendar.weeklyHours).map(([day,ranges])=>[day,ranges.slice().sort((a,b)=>a.start.localeCompare(b.start))])),holidays:[...new Set(holidays)].sort()}};
  }
  async function create(){
    const siteId=scope==='global'?null:scope;
    const created=await api<{id:string}>('/admin/support-sla/policies','POST',{name:policyName.trim(),siteId,definition:makeDefinition()});
    setPolicyName('');await load();setSelectedId(created.id);onMessage('Policy SLA pubblicata');
  }
  async function publish(){
    if(!selected)return;
    await api(`/admin/support-sla/policies/${selected.id}/versions`,'POST',{expectedVersion:selected.version,name:name.trim(),definition:makeDefinition()});
    await load();onMessage('Nuova versione SLA pubblicata');
  }

  return <div className="sla-policy-admin">
    {error&&<p role="alert" className="error">{error}</p>}
    <section className="panel">
      <div className="section-heading"><div><span className="eyebrow">SERVICE DESK</span><h2><CalendarClock size={19}/>Policy SLA</h2></div><span className="badge">Permesso `tickets.sla.manage`</span></div>
      <p>Le policy pubblicate sono versionate. Ogni nuovo ticket acquisisce la versione applicabile alla sua sede; i ticket esistenti mantengono le scadenze già assegnate.</p>
      <form className="workflow-create" onSubmit={e=>{e.preventDefault();void run(create);}}>
        <label>Nome policy<input aria-label="Nome nuova policy SLA" value={policyName} onChange={e=>setPolicyName(e.target.value)} required maxLength={100}/></label>
        <label>Ambito<select aria-label="Ambito nuova policy SLA" value={scope} onChange={e=>setScope(e.target.value)}>{scopes.canManageGlobal&&<option value="global">Globale</option>}{scopes.sites.map(s=><option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <button disabled={busy||!policyName.trim()||(!scopes.canManageGlobal&&scopes.sites.length===0)}><Plus size={16}/>Crea e pubblica</button>
      </form>
      <p className="muted-hint">Per modificare una policy esistente pubblica una nuova versione qui sotto; la cronologia resta consultabile.</p>
    </section>

    <section className="panel">
      <label>Policy da modificare<select aria-label="Policy SLA da modificare" value={selectedId} onChange={e=>setSelectedId(e.target.value)}><option value="">Seleziona una policy</option>{policies.map(p=><option key={p.id} value={p.id}>{p.name} · {p.siteId?scopes.sites.find(s=>s.id===p.siteId)?.name??'Sede': 'Globale'} · v{p.currentVersion}</option>)}</select></label>
      {!selected?<p>{policies.length?'Seleziona una policy per consultare la cronologia o pubblicare una revisione.':'Nessuna policy accessibile. Crea una policy nel tuo ambito autorizzato.'}</p>:<>
        <div className="section-heading"><div><h3>{selected.name}</h3><small>{selected.siteId?scopes.sites.find(s=>s.id===selected.siteId)?.name??'Sede':'Globale'} · versione corrente {selected.currentVersion}</small></div></div>
        <form className="sla-editor" onSubmit={e=>{e.preventDefault();void run(publish);}}>
          <div className="sla-target-fields">
            <label>Nome versione<input aria-label="Nome versione SLA" value={name} onChange={e=>setName(e.target.value)} required maxLength={100}/></label>
            <label>Primo riscontro · minuti<input aria-label="Minuti primo riscontro" type="number" min={1} max={525600} value={definition.firstResponseMinutes} onChange={e=>setDefinition(d=>({...d,firstResponseMinutes:Number(e.target.value)}))} required/></label>
            <label>Risoluzione · minuti<input aria-label="Minuti risoluzione" type="number" min={1} max={525600} value={definition.resolutionMinutes} onChange={e=>setDefinition(d=>({...d,resolutionMinutes:Number(e.target.value)}))} required/></label>
            <label>Fuso orario IANA<input aria-label="Fuso orario SLA" value={definition.calendar.timeZoneId} onChange={e=>setDefinition(d=>({...d,calendar:{...d.calendar,timeZoneId:e.target.value}}))} required maxLength={100} placeholder="Europe/Rome"/></label>
          </div>
        <div className="sla-calendar"><h3><Clock3 size={17}/>Orario lavorativo settimanale</h3><div className="sla-days">{days.map(day=>{const ranges=definition.calendar.weeklyHours[day.key]??[];const longest=ranges.length?Math.max(...ranges.map(r=>minutes(r.end)-minutes(r.start))):0;return <div className="sla-day" key={day.key}><label className="sla-day-name"><input type="checkbox" aria-label={`${day.label} lavorativo`} checked={ranges.length>0} onChange={e=>setDay(day.key,e.target.checked)}/>{day.label}</label>{ranges.map((range,index)=><div className="sla-range" key={`${day.key}-${index}`}><small>Intervallo {index+1}</small><label>Da<input aria-label={`${day.label} intervallo ${index+1} apertura`} type="time" value={range.start} onChange={e=>setRange(day.key,index,'start',e.target.value)}/></label><label>A<input aria-label={`${day.label} intervallo ${index+1} chiusura`} type="time" value={range.end} onChange={e=>setRange(day.key,index,'end',e.target.value)}/></label><button type="button" className="secondary compact" aria-label={`Rimuovi ${day.label} intervallo ${index+1}`} onClick={()=>removeRange(day.key,index)}>Rimuovi</button></div>)}{ranges.length>0&&<button type="button" className="secondary compact sla-add-range" aria-label={`Aggiungi intervallo ${day.label}`} disabled={longest<120} onClick={()=>splitRange(day.key)}>Aggiungi intervallo</button>}</div>;})}</div><small>Aggiungi intervalli per pause e orari spezzati; ciascun intervallo deve avere inizio precedente alla fine.</small></div>
          <label>Festività · una data YYYY-MM-DD per riga<textarea aria-label="Festività SLA" value={holidayText} onChange={e=>setHolidayText(e.target.value)} rows={4} placeholder="2026-12-25\n2026-12-26"/></label>
          <button disabled={busy||!name.trim()}><Save size={16}/>Pubblica versione {selected.currentVersion+1}</button>
        </form>
        <details className="sla-history"><summary>Cronologia versioni ({selected.versions.length})</summary><ol>{selected.versions.slice().reverse().map(v=><li key={v.version}><strong>v{v.version} · {v.name}</strong><small>{new Date(v.publishedAt).toLocaleString('it-IT')} · {v.definition.firstResponseMinutes} min riscontro / {v.definition.resolutionMinutes} min risoluzione · {v.definition.calendar.timeZoneId}</small></li>)}</ol></details>
      </>}
    </section>
  </div>;
}
