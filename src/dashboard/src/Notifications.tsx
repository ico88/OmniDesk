import { useEffect, useState } from 'react';
import { Bell, BellRing, Check, MessageSquare, Paperclip, UserRound, MonitorX } from 'lucide-react';
import { api } from './api';
import './notifications.css';

type Notification = { id:string; kind:string; createdAt:string; readAt:string|null; ticketId:string; number:number; subject:string };
type Inbox = { unreadCount:number; notifications:Notification[] };
type DeviceAlert = { deviceId:string; name:string; changedAt:string; lastSeen:string };

export function Notifications({onOpenTicket,onOpenDevice}:{onOpenTicket:(ticketId:string)=>void;onOpenDevice:(deviceId:string)=>void}) {
  const [inbox,setInbox]=useState<Inbox>({unreadCount:0,notifications:[]});
  const [offlineDevices,setOfflineDevices]=useState<DeviceAlert[]>([]);
  const [open,setOpen]=useState(false);
  const [error,setError]=useState('');
  async function refresh() {
    try {
      const [inboxResult,alertsResult]=await Promise.allSettled([api<Inbox>('/notifications'),api<{offlineDevices:DeviceAlert[]}>('/notifications/devices')]);
      if(inboxResult.status==='rejected')throw inboxResult.reason;
      setInbox(inboxResult.value);
      if(alertsResult.status==='fulfilled'){setOfflineDevices(alertsResult.value.offlineDevices);setError('');}
      else {setOfflineDevices([]);setError('Allarmi dispositivo non disponibili');}
    }
    catch { setError('Notifiche non disponibili'); }
  }
  useEffect(()=>{
    void refresh();
    const timer=window.setInterval(()=>void refresh(),30000);
    return ()=>window.clearInterval(timer);
  },[]);
  async function openTicket(item:Notification) {
    try {
      if (!item.readAt) await api(`/notifications/${item.id}/read`,'POST');
      await refresh();
      setOpen(false);
      onOpenTicket(item.ticketId);
    } catch { setError('Impossibile aprire la notifica'); }
  }
  return <div className="notification-inbox">
    <button type="button" className="secondary notification-trigger" aria-label={`Aggiornamenti non letti: ${inbox.unreadCount+offlineDevices.length}`} aria-expanded={open} onClick={()=>setOpen(value=>!value)}>
      <Bell size={17}/>{inbox.unreadCount+offlineDevices.length>0&&<span className="notification-count">{inbox.unreadCount+offlineDevices.length>99?'99+':inbox.unreadCount+offlineDevices.length}</span>}
    </button>
    {open&&<section className="notification-panel" aria-label="Notifiche e stato dispositivi">
      <div className="section-heading"><div><span className="eyebrow">AGGIORNAMENTI</span><h2>Notifiche</h2></div><button className="secondary compact" type="button" onClick={()=>void refresh()}><Bell size={14}/>Aggiorna</button></div>
      {error&&<p role="status">{error}</p>}
      {offlineDevices.length>0&&<><small className="notification-group-label">DISPOSITIVI OFFLINE · {offlineDevices.length}</small><ul>{offlineDevices.map(item=><li key={item.deviceId} className="unread"><button type="button" className="notification-item" onClick={()=>{setOpen(false);onOpenDevice(item.deviceId);}}>
        <MonitorX size={17}/><span><strong>{item.name} non raggiungibile</strong><small>Ultimo heartbeat {new Date(item.lastSeen).toLocaleString('it-IT')} · offline dal {new Date(item.changedAt).toLocaleString('it-IT')}</small></span><span className="notification-dot"/>
      </button></li>)}</ul></>}
      {inbox.notifications.length===0&&offlineDevices.length===0?<p className="empty-state">Non ci sono notifiche recenti.</p>:inbox.notifications.length===0?null:<ul>{inbox.notifications.map(item=><li key={item.id} className={item.readAt?'':'unread'}><button type="button" className="notification-item" onClick={()=>void openTicket(item)}>
        {item.kind==='ticket.assigned'?<UserRound size={17}/>:item.kind==='ticket.attachment'?<Paperclip size={17}/>:item.kind==='ticket.sla.breached'||item.kind==='ticket.sla.escalated'?<BellRing size={17}/>:<MessageSquare size={17}/>}<span><strong>{item.kind==='ticket.new'?'Nuovo ticket':item.kind==='ticket.assigned'?'Ticket assegnato':item.kind==='ticket.attachment'?'Allegato pronto':item.kind==='ticket.sla.breached'?'SLA superato':item.kind==='ticket.sla.escalated'?'Escalazione SLA':'Risposta del richiedente'}</strong><small>OD-{String(item.number).padStart(6,'0')} · {item.subject} · {new Date(item.createdAt).toLocaleString('it-IT')}</small></span>{item.readAt?<Check size={15}/>:<span className="notification-dot"/>}
      </button></li>)}</ul>}
    </section>}
  </div>;
}
