import { useEffect, useState } from 'react';
import { api } from './api';

type Session = {id:string;capability:string;state:string;createdAt:string;expiresAt:string|null};
type Availability = {configured:boolean;mapped:boolean;revoked:boolean;nodeId:string|null};
export function RemoteSessions({deviceId,admin}:{deviceId:string;admin:boolean}) {
  const [sessions,setSessions]=useState<Session[]>([]);
  const [availability,setAvailability]=useState<Availability|null>(null);
  const [node,setNode]=useState('');
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('');
  async function load() {
    const [rows,config]=await Promise.all([api<Session[]>(`/devices/${deviceId}/remote-sessions`),api<Availability>(`/devices/${deviceId}/remote-sessions/availability`)]);
    setSessions(rows);setAvailability(config);setNode(config.nodeId??'');
  }
  async function work(action:()=>Promise<void>) {
    setBusy(true);setMessage('');
    try {await action();}catch(e){setMessage(e instanceof Error?e.message:'Operazione non riuscita');}finally{setBusy(false);}
  }
  useEffect(()=>{void work(load);},[deviceId]);
  return <details><summary>Motore e storico sessioni remote</summary>
    <p>{!availability?'Configurazione in verifica':availability.revoked?'Dispositivo revocato':!availability.configured?'Motore remoto non configurato':!availability.mapped?'Associazione MeshCentral mancante':'Configurazione presente. Connessione, consenso e desktop richiedono verifica sul dispositivo.'}</p>
    {message&&<p role="status">{message}</p>}
    <button disabled={busy} onClick={()=>void work(load)}>Aggiorna sessioni</button>
    {admin&&<form onSubmit={e=>{e.preventDefault();if(!confirm('Confermi che questo nodo MeshCentral appartiene al dispositivo selezionato?'))return;void work(async()=>{await api(`/admin/devices/${deviceId}/mesh`,'PUT',{nodeId:node});await load();});}}>
      <label>Identificativo nodo MeshCentral<input value={node} maxLength={128} required onChange={e=>setNode(e.target.value)} placeholder="node//..."/></label><button disabled={busy}>Associa nodo remoto</button>
      <p>Verifica l'identità del nodo: le sessioni saranno dirette a questo dispositivo.</p>
    </form>}
    {sessions.map(s=><p key={s.id}>{new Date(s.createdAt).toLocaleString('it-IT')} · {s.capability} · {s.state}
      {['active','revocation_pending'].includes(s.state)&&<button disabled={busy} onClick={()=>void work(async()=>{
        const response=await api<{state:string}|undefined>(`/devices/${deviceId}/remote-sessions/${s.id}`,'DELETE');await load();
        setMessage(response?.state==='revocation_pending'?'Chiusura in attesa di conferma del motore.':'Revoca confermata dal motore.');
      })}>Richiedi revoca</button>}</p>)}
    <p>Lo storico non contiene link di accesso. Solo il creatore o un amministratore globale può revocare.</p>
  </details>;
}
