import { useEffect, useState } from 'react';
import { Bell, BellRing, Check, MessageSquare, Paperclip, UserRound } from 'lucide-react';
import { api } from './api';
import './notifications.css';

type Notification = { id:string; kind:string; createdAt:string; readAt:string|null; ticketId:string; number:number; subject:string };
type Inbox = { unreadCount:number; notifications:Notification[] };

export function Notifications({onOpenTicket}:{onOpenTicket:(ticketId:string)=>void}) {
  const [inbox,setInbox]=useState<Inbox>({unreadCount:0,notifications:[]});
  const [open,setOpen]=useState(false);
  const [error,setError]=useState('');
  async function refresh() {
    try { setInbox(await api<Inbox>('/notifications')); setError(''); }
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
    <button type="button" className="secondary notification-trigger" aria-label={`Notifiche non lette: ${inbox.unreadCount}`} aria-expanded={open} onClick={()=>setOpen(value=>!value)}>
      <Bell size={17}/>{inbox.unreadCount>0&&<span className="notification-count">{inbox.unreadCount>99?'99+':inbox.unreadCount}</span>}
    </button>
    {open&&<section className="notification-panel" aria-label="Notifiche ticket">
      <div className="section-heading"><div><span className="eyebrow">AGGIORNAMENTI</span><h2>Notifiche</h2></div><button className="secondary compact" type="button" onClick={()=>void refresh()}><Bell size={14}/>Aggiorna</button></div>
      {error&&<p role="status">{error}</p>}
      {inbox.notifications.length===0?<p className="empty-state">Non ci sono notifiche recenti.</p>:<ul>{inbox.notifications.map(item=><li key={item.id} className={item.readAt?'':'unread'}><button type="button" className="notification-item" onClick={()=>void openTicket(item)}>
        {item.kind==='ticket.assigned'?<UserRound size={17}/>:item.kind==='ticket.attachment'?<Paperclip size={17}/>:item.kind==='ticket.sla.breached'?<BellRing size={17}/>:<MessageSquare size={17}/>}<span><strong>{item.kind==='ticket.new'?'Nuovo ticket':item.kind==='ticket.assigned'?'Ticket assegnato':item.kind==='ticket.attachment'?'Allegato pronto':item.kind==='ticket.sla.breached'?'SLA superato':'Risposta del richiedente'}</strong><small>OD-{String(item.number).padStart(6,'0')} · {item.subject} · {new Date(item.createdAt).toLocaleString('it-IT')}</small></span>{item.readAt?<Check size={15}/>:<span className="notification-dot"/>}
      </button></li>)}</ul>}
    </section>}
  </div>;
}
