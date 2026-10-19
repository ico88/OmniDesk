import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Session = {id:string;capability:string;state:string;createdAt:string;expiresAt:string|null;consentRequired:boolean};
type Availability = {configured:boolean;mapped:boolean;revoked:boolean;nodeId:string|null;unattendedAllowed:boolean};
export function RemoteSessions({deviceId,admin,onAvailability}:{deviceId:string;admin:boolean;onAvailability:(ready:boolean)=>void}) {
  const [sessions,setSessions]=useState<Session[]>([]);
  const [availability,setAvailability]=useState<Availability|null>(null);
  const [enabled,setEnabled]=useState(false);
  const [policy,setPolicy]=useState({unattendedAllowed:false,accessPolicyVersion:0});
  const [node,setNode]=useState('');
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('');
  async function load() {
    const [rows,config]=await Promise.all([api<Session[]>(`/devices/${deviceId}/remote-sessions`),api<Availability>(`/devices/${deviceId}/remote-sessions/availability`)]);
    setSessions(rows);setAvailability(config);setNode(config.nodeId??'');onAvailability(config.configured&&config.mapped&&!config.revoked);
    if(admin) {const config=await api<{enabled:boolean;unattendedAllowed:boolean;accessPolicyVersion:number}>(`/admin/devices/${deviceId}/remote-provisioning`);setEnabled(config.enabled);setPolicy(config);}
  }
  async function work(action:()=>Promise<void>) {
    setBusy(true);setMessage('');
    try {await action();}catch(e){setMessage(e instanceof Error?e.message:'Operazione non riuscita');}finally{setBusy(false);}
  }
  useEffect(()=>{void work(load);const timer=window.setInterval(()=>{void load().catch(()=>{onAvailability(false);setAvailability(null);setMessage('Stato remoto non verificabile. Aggiorna per riprovare.');});},15000);return()=>window.clearInterval(timer);},[deviceId]);
  return <section className="remote-status"><div className="remote-heading"><Icon name="security"/><strong>Accesso remoto</strong></div>
    <p>{!availability?'Configurazione in verifica':availability.revoked?'Accesso disattivato: dispositivo revocato.':!availability.configured?'Motore remoto non configurato sul server.':!availability.mapped?(enabled?'In attesa del motore Windows: approvazione salvata, associazione non ancora ricevuta.':'Abilita il motore remoto per completare l’associazione automatica.'):`Motore associato · ${availability.unattendedAllowed?'Accesso non presidiato':'Conferma locale richiesta'}`}</p>
    {message&&<p role="status">{message}</p>}
    <button className="secondary compact" disabled={busy} onClick={()=>void work(load)}><Icon name="refresh" size={15}/>Aggiorna sessioni</button>
    {admin&&<p>Provisioning remoto: {enabled?'approvato':'non approvato'} <button disabled={busy||!availability?.configured||availability.revoked} onClick={()=>void work(async()=>{
      await api(`/admin/devices/${deviceId}/remote-provisioning`,enabled?'DELETE':'POST');await load();
      setMessage(enabled?'Disattivazione richiesta: il servizio remoto perde il rinnovo dell’autorizzazione.':'Approvato: Agent 0.7 recupera la configurazione e registra il nodo.');
    })}><Icon name="devices" size={16}/>{enabled?'Disattiva motore sul dispositivo':'Abilita motore sul dispositivo'}</button></p>}
    {admin&&enabled&&<div className="panel">
      <strong>{policy.unattendedAllowed?'Accesso non presidiato abilitato':'Conferma locale richiesta'}</strong>
      <p>Desktop, terminale e file mantengono permessi separati. Le sessioni mostrano le notifiche del motore e vengono registrate nello storico.</p>
      <button disabled={busy} onClick={()=>{
        const next=!policy.unattendedAllowed;
        if(!confirm(next?'Abilitare accesso senza conferma locale su questo dispositivo? Gli operatori autorizzati potranno collegarsi anche senza un utente presente. Le sessioni esistenti saranno revocate.':'Ripristinare la conferma locale? Le sessioni esistenti saranno revocate.'))return;
        void work(async()=>{await api(`/admin/devices/${deviceId}/remote-provisioning/access-policy`,'PUT',{unattendedAllowed:next,expectedVersion:policy.accessPolicyVersion});await load();setMessage('Policy salvata. Chiusura delle sessioni precedenti in corso; avvia una nuova sessione.');});
      }}>{policy.unattendedAllowed?'Richiedi sempre conferma locale':'Abilita accesso non presidiato'}</button>
    </div>}
    {admin&&<details className="advanced"><summary>Associazione manuale avanzata</summary><form onSubmit={e=>{e.preventDefault();if(!confirm('Confermi che questo nodo MeshCentral appartiene al dispositivo selezionato?'))return;void work(async()=>{await api(`/admin/devices/${deviceId}/mesh`,'PUT',{nodeId:node});await load();});}}>
      <label>Identificativo nodo MeshCentral<input value={node} maxLength={128} required onChange={e=>setNode(e.target.value)} placeholder="node//..."/></label><button disabled={busy}>Associa nodo remoto</button>
      <p>Verifica l'identità del nodo: le sessioni saranno dirette a questo dispositivo.</p>
    </form></details>}
    <details className="advanced"><summary><Icon name="clock"/>Storico sessioni <span className="count">{sessions.length}</span></summary>
    {!sessions.length&&<p>Nessuna sessione registrata.</p>}
    {sessions.map(s=><p key={s.id}>{new Date(s.createdAt).toLocaleString('it-IT')} · {s.capability} · {s.state} · {s.consentRequired?'Con consenso locale':'Non presidiata'}
      {['active','revocation_pending'].includes(s.state)&&<button disabled={busy} onClick={()=>void work(async()=>{
        const response=await api<{state:string}|undefined>(`/devices/${deviceId}/remote-sessions/${s.id}`,'DELETE');await load();
        setMessage(response?.state==='revocation_pending'?'Chiusura in attesa di conferma del motore.':'Revoca confermata dal motore.');
      })}>Richiedi revoca</button>}</p>)}
    <p>Lo storico non contiene link di accesso. Solo il creatore o un amministratore globale può revocare.</p>
    </details>
  </section>;
}
