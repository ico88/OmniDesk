import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, refreshCsrf } from './api';
import './style.css';
import { AccessManagement } from './AccessManagement';
import { SupportDesk } from './SupportDesk';
import { DeviceWorkspace, type DeviceStatus } from './DeviceWorkspace';
import { AgentDownload } from './AgentDownload';
import { Icon } from './Icon';
import { KnowledgeBase } from './KnowledgeBase';
import { Notifications } from './Notifications';
import { CustomerPortal } from './CustomerPortal';
import { TicketWorkflowAdmin } from './TicketWorkflowAdmin';
import { SlaPolicyAdmin } from './SlaPolicyAdmin';
import { EmailIntakeInbox } from './EmailIntakeInbox';

type User = { id: string; email: string; displayName: string | null; emailNotificationsEnabled: boolean; emailNotificationsAvailable: boolean; globalPermissions: string[]; twoFactorAvailable: boolean; twoFactorEnabled: boolean };
type Branding = { name: string; logoUrl: string; primaryColor: string; theme: 'light' | 'dark' };
type Device = { id: string; name: string; groupId: string | null; siteId: string | null; online: boolean; revoked: boolean; lastSeen: string | null };
type Group = { id: string; name: string };
type Site = { id: string; name: string; isArchived: boolean };
type Audit = { id: number; at: string; action: string; target: string; outcome: string };
const defaults: Branding = { name: 'OmniDesk', logoUrl: '', primaryColor: '#2563eb', theme: 'dark' };

function App() {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [branding, setBranding] = useState(defaults);
  const [draft, setDraft] = useState(defaults);
  const [devices, setDevices] = useState<Device[]>([]);
  const [audit, setAudit] = useState<Audit[]>([]);
  const [widgets, setWidgets] = useState<string[]>(['devices','permissions','authentication']);
  const [totpSetup, setTotpSetup] = useState<{sharedKey:string;authenticatorUri:string}|null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [groups, setGroups] = useState<Group[]>([]);
  const [sites, setSites] = useState<Site[]>([]);
  const [enrollment, setEnrollment] = useState('');
  const [status, setStatus] = useState<DeviceStatus | null>(null);
  const initialTicket = new URLSearchParams(window.location.search).get('ticket');
  const [tab, setTab] = useState(initialTicket ? 'support' : 'overview');
  const [focusTicketId, setFocusTicketId] = useState<string|null>(initialTicket);
  const [search,setSearch]=useState('');
  const [canManageSla,setCanManageSla]=useState(false);
  const admin = user?.globalPermissions.includes('administration') ?? false;
  const canReadKnowledge = user?.globalPermissions.some(p=>p==='knowledge.view'||p==='knowledge.manage') ?? false;
  const canManageEmailIntake = user?.globalPermissions.includes('tickets.manage') ?? false;

  const filteredDevices=devices.filter(d=>d.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()));

  async function load() {
    const me = await api<User>('/auth/me');
    setUser(me);
    const [brand, list] = await Promise.all([api<Branding>('/branding'), api<Device[]>('/devices')]);
    setBranding(brand); setDraft(brand); setDevices(list); setWidgets((await api<{widgets:string[]}>('/dashboard/layout')).widgets);
    if (me.globalPermissions.includes('administration')) { const [events, groupList, siteList] = await Promise.all([api<Audit[]>('/admin/audit'), api<Group[]>('/admin/groups'), api<Site[]>('/admin/sites')]); setAudit(events); setGroups(groupList); setSites(siteList); }
    await refreshCsrf();
  }
  useEffect(() => {
    if (tab === 'devices' && admin) Promise.all([api<Group[]>('/admin/groups'), api<Site[]>('/admin/sites')]).then(([groupList, siteList]) => { setGroups(groupList); setSites(siteList); }).catch(e => setError(e instanceof Error ? e.message : 'Errore caricamento gruppi e sedi'));
  }, [tab, admin]);
  useEffect(()=>{
    if(!user||!['devices','support'].includes(tab))return;
    let active=true;
    void api<Device[]>('/devices').then(list=>{if(active)setDevices(list);}).catch(e=>{if(active)setError(e instanceof Error?e.message:'Elenco dispositivi non disponibile');});
    return()=>{active=false;};
  },[tab,user?.id]);
  useEffect(()=>{
    if(!user||tab!=='devices')return;
    let active=true;
    const timer=window.setInterval(()=>{void api<Device[]>('/devices').then(list=>{if(active)setDevices(list);}).catch(e=>{if(active)setError(e instanceof Error?e.message:'Stato dispositivi non aggiornato');});},30_000);
    return()=>{active=false;window.clearInterval(timer);};
  },[tab,user?.id]);
  useEffect(() => { load().catch(() => setUser(null)).finally(() => setLoading(false)); }, []);
  useEffect(() => {
    document.documentElement.dataset.theme = branding.theme;
    document.documentElement.style.setProperty('--primary', branding.primaryColor);
    document.title = branding.name;
  }, [branding]);
  useEffect(()=>{
    if(!user)return;
    let active=true;
    void api('/admin/support-sla/scopes').then(()=>{if(active)setCanManageSla(true);}).catch(()=>{if(active)setCanManageSla(false);});
    return()=>{active=false;};
  },[user?.id]);
  async function action(work: () => Promise<void>) {
    setBusy(true); setError(''); setMessage('');
    try { await work(); } catch (e) { setError(e instanceof Error ? e.message : 'Errore inatteso'); }
    finally { setBusy(false); }
  }
  if (loading) return <main className="login"><p>Connessione a OmniDesk…</p></main>;
  if (!user) return <main className="login"><form className="panel login-card" onSubmit={e => {
    e.preventDefault(); const form = new FormData(e.currentTarget);
    void action(async () => { await api('/auth/login', 'POST', { email: form.get('email'), password: form.get('password'), code:form.get('code') || undefined, recoveryCode:form.get('recoveryCode') || undefined }); await load(); });
  }}><img className="brand-mark" src="/favicon.svg" alt=""/><h1>OmniDesk</h1><p>Accedi alla console IT</p>
    <label>Email<input name="email" type="email" autoComplete="username" required maxLength={254}/></label>
    <label>Password<input name="password" type="password" autoComplete="current-password" required maxLength={1024}/></label>
    <details className="login-options"><summary>Usa un codice 2FA o di recupero</summary><label>Codice 2FA (se attiva)<input name="code" inputMode="numeric" autoComplete="one-time-code" maxLength={20}/></label><label>Oppure codice di recupero<input name="recoveryCode" autoComplete="off" maxLength={100}/></label></details>
    {error && <p role="alert" className="error">{error}</p>}<button disabled={busy}>{busy ? 'Accesso…' : 'Accedi'}</button>
    <small>Account creati dall’amministratore. 2FA opzionale, inizialmente disattivata.</small>
  </form></main>;
  return <div className="shell"><aside>
    <div className="brand">{branding.logoUrl ? <img src={branding.logoUrl} alt="Logo" referrerPolicy="no-referrer"/> : <img className="brand-mark" src="/favicon.svg" alt=""/>}<strong>{branding.name}</strong></div>
    <small className="nav-label">WORKSPACE</small>
    <nav aria-label="Navigazione">{[['overview','Panoramica'],['devices','Dispositivi'],['security','Account e sicurezza'],['myTickets','I miei ticket'],['support','Assistenza'],...(canManageEmailIntake ? [['emailIntakes','Email da valutare']] : []),...(canReadKnowledge ? [['knowledge','Knowledge base']] : []),...(canManageSla ? [['slaPolicies','Policy SLA']] : []),...(admin ? [['branding','Personalizzazione'],['users','Utenti'],['workflows','Workflow ticket'],['audit','Audit']] : [])].map(([id,label]) => <button key={id} className={tab === id ? 'active' : ''} aria-current={tab===id?'page':undefined} onClick={() => { setTab(id); setTotpSetup(null); setRecoveryCodes([]); setEnrollment(''); setStatus(null);  setError(''); setMessage(''); }}><Icon name={id}/><span>{label}</span></button>)}</nav>
    <div className="account"><small>{user.email}</small><button disabled={busy} onClick={() => void action(async () => { await api('/auth/logout', 'POST'); setUser(null); setTotpSetup(null); setRecoveryCodes([]); setEnrollment(''); setStatus(null); setGroups([]); setBranding(defaults); setAudit([]); setDevices([]); setTab('overview'); await refreshCsrf(); })}><Icon name="logout"/>Esci</button></div>
  </aside><main className="content" aria-busy={busy}><header><div><small className="eyebrow">WORKSPACE / GESTIONE IT</small><h1>{{overview:'Panoramica',devices:'Dispositivi',branding:'Personalizzazione',users:'Utenti',workflows:'Workflow ticket',slaPolicies:'Policy SLA',audit:'Audit amministrativo',security:'Account e sicurezza',myTickets:'I miei ticket',support:'Assistenza',emailIntakes:'Email da valutare',knowledge:'Knowledge base'}[tab]}</h1></div><div className="header-actions"><Notifications onOpenTicket={id=>{setFocusTicketId(id);setTab('support');}} onOpenDevice={id=>action(async()=>{const next=await api<DeviceStatus>(`/devices/${id}/status`);setStatus(next);setTab('devices');})}/><span className="badge"><Icon name="security" size={15}/>{admin?'Amministratore':'Accesso limitato'}</span></div></header>
    {error && <p role="alert" className="error">{error}</p>}{message && <p role="status" className="notice"><Icon name="check"/>{message}</p>}
    {tab === 'support' && <SupportDesk sites={sites} devices={devices} visibleDeviceIds={devices.map(d=>d.id)} focusTicketId={focusTicketId} onTicketFocused={()=>setFocusTicketId(null)} onOpenDevice={id=>action(async()=>{const next=await api<DeviceStatus>(`/devices/${id}/status`);setStatus(next);setTab('devices');})}/>}
    {tab === 'emailIntakes' && canManageEmailIntake && <EmailIntakeInbox devices={devices} onMessage={setMessage} onOpenTicket={id=>{setFocusTicketId(id);setTab('support');}}/>}
    {tab === 'myTickets' && <CustomerPortal onMessage={setMessage}/>}
    {tab === 'knowledge' && canReadKnowledge && <KnowledgeBase canManage={user.globalPermissions.includes('knowledge.manage')} onMessage={setMessage}/>}
    {tab === 'slaPolicies' && canManageSla && <SlaPolicyAdmin onMessage={setMessage}/>}
    {tab === 'overview' && <><div className="stats">{widgets.map(widget => <article key={widget} className="panel">{widget === 'devices' ? <><div className="stat-title"><small>Dispositivi visibili</small><Icon name="devices"/></div><h2>{devices.length}</h2><p>Autorizzati per il tuo account</p></> : widget === 'permissions' ? <><div className="stat-title"><small>Permessi globali</small><Icon name="users"/></div><h2>{user.globalPermissions.length}</h2><p>{user.globalPermissions.join(', ') || 'Solo concessioni locali'}</p></> : <><div className="stat-title"><small>Autenticazione</small><Icon name="security"/></div><h2>Identity</h2><p>{user.twoFactorEnabled ? '2FA attiva' : '2FA non attiva'}</p></>}</article>)}</div><details className="panel"><summary>Personalizza i tuoi riquadri</summary>{[['devices','Dispositivi'],['permissions','Permessi'],['authentication','Autenticazione']].map(([id,label]) => <label key={id}><input style={{width:'auto'}} type="checkbox" checked={widgets.includes(id)} disabled={busy} onChange={e => { const previous = widgets; const next = e.target.checked ? [...widgets,id] : widgets.filter(x => x !== id); setWidgets(next); void action(async () => { try { await api('/dashboard/layout','PUT',{widgets:next}); } catch (error) { setWidgets(previous); throw error; } }); }}/>{label}</label>)}<button disabled={busy || widgets.length < 2} onClick={() => void action(async () => { const next=[...widgets.slice(1),widgets[0]]; await api('/dashboard/layout','PUT',{widgets:next}); setWidgets(next); })}>Ruota ordine riquadri</button></details><section className="panel welcome"><div><span className="eyebrow">IL TUO SPAZIO DI LAVORO</span><h2>Assistenza, senza perdere il filo.</h2><p>Trova un dispositivo, avvia una sessione autorizzata o segui le richieste di assistenza.</p></div><div className="action-row"><button onClick={()=>setTab('devices')}><Icon name="devices"/>Vai ai dispositivi</button><button className="secondary" onClick={()=>setTab('support')}><Icon name="support"/>Apri assistenza</button></div></section></>}
    {tab === 'devices' && <><section className="panel device-list"><div className="section-heading"><div><span className="eyebrow">DISPOSITIVI</span><h2>Parco macchine <span className="count">{devices.length}</span></h2></div><button className="secondary" disabled={busy} onClick={() => void action(async () => setDevices(await api<Device[]>('/devices')))}><Icon name="refresh"/>Aggiorna</button></div>
      <label className="search-field"><Icon name="search"/><input aria-label="Cerca dispositivo" placeholder="Cerca un dispositivo per nome…" value={search} onChange={e=>setSearch(e.target.value)}/></label>
      {devices.length===0?<div className="empty-state"><Icon name="devices" size={36}/><h3>Il tuo parco macchine parte da qui</h3><p>Registra un Agent Windows per visualizzare il primo dispositivo.</p></div>:filteredDevices.length===0?<p>Nessun dispositivo corrisponde alla ricerca.</p>:<div className="table-wrap"><table><thead><tr><th>Dispositivo</th><th>Stato Agent</th><th>Gruppo</th><th>Sede</th><th></th></tr></thead><tbody>{filteredDevices.map(d=><tr className={status?.id===d.id?'selected-row':''} key={d.id}><td><span className="device-name"><span className="device-symbol"><Icon name="devices"/></span><span title={d.id}>{d.name}</span></span></td><td><span className={`status-pill ${d.revoked?'danger':d.online?'online':'offline'}`} title={d.lastSeen?`Ultimo heartbeat ${new Date(d.lastSeen).toLocaleString('it-IT')}`:'Nessun heartbeat ricevuto'}>{d.revoked?'Revocato':d.online?'Online':'Offline'}</span></td><td>{groups.find(g=>g.id===d.groupId)?.name??(d.groupId?'Gruppo assegnato':'Senza gruppo')}</td><td>{sites.find(s=>s.id===d.siteId)?.name??'Sede non disponibile'}</td><td className="row-action"><button className="secondary" disabled={busy} onClick={()=>void action(async()=>{setStatus(await api<DeviceStatus>(`/devices/${d.id}/status`));})}>Apri<Icon name="arrow" size={16}/></button></td></tr>)}</tbody></table></div>}
    </section>
      {status && <DeviceWorkspace key={status.id} device={status} admin={admin} groups={groups} sites={sites} onMessage={setMessage} onDeviceChanged={async message => {
        const id = status.id;
        const [updated, list] = await Promise.all([api<DeviceStatus>(`/devices/${id}/status`), api<Device[]>('/devices')]);
        setStatus(current => current?.id === id ? updated : current); setDevices(list); setMessage(message);
      }}/>}
      {admin && <div className="device-onboarding"><AgentDownload/><details className="panel enrollment" open><summary><Icon name="plus"/>Registra un dispositivo</summary><form className="settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); setEnrollment(''); void action(async () => { const issued = await api<{token:string}>('/admin/enrollment-tokens','POST',{groupId:f.get('group') || null, siteId:f.get('site'), expiresInMinutes:30}); setEnrollment(issued.token); }); }}><h2>Registra un dispositivo</h2><label>Sede<select aria-label="Sede" name="site" required>{sites.filter(s => !s.isArchived).map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label><label>Gruppo<select aria-label="Gruppo" name="group"><option value="">Senza gruppo</option>{groups.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label><button disabled={busy || sites.every(s => s.isArchived)}>Genera token monouso (30 minuti)</button>{enrollment && <><p>Copia questo token nel prompt dell’installer Windows. È monouso e viene mostrato solo qui.</p><textarea aria-label="Token enrollment" readOnly value={enrollment} rows={3} style={{width:'100%'}}/><button type="button" onClick={() => setEnrollment('')}>Nascondi token</button></>}</form></details></div>}
    </>}
    {tab === 'branding' && admin && <form className="panel settings" onSubmit={e => { e.preventDefault(); void action(async () => { const saved = await api<Branding>('/branding','PUT',draft); setBranding(saved); setMessage('Personalizzazione salvata'); }); }}>
      <h2>Identità della dashboard</h2><label>Nome<input required maxLength={100} value={draft.name} onChange={e => setDraft({...draft,name:e.target.value})}/></label>
      <label>Logo (URL HTTPS)<input type="url" maxLength={2048} value={draft.logoUrl} onChange={e => setDraft({...draft,logoUrl:e.target.value})}/></label>
      <label>Colore principale<input type="color" value={draft.primaryColor} onChange={e => setDraft({...draft,primaryColor:e.target.value})}/></label>
      <label>Tema<select aria-label="Tema" value={draft.theme} onChange={e => setDraft({...draft,theme:e.target.value as Branding['theme']})}><option value="dark">Scuro</option><option value="light">Chiaro</option></select></label>
      <button disabled={busy}>Salva personalizzazione</button>
    </form>}
    {tab === 'security' && <><form className="panel settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void action(async () => { await api('/auth/profile','PUT',{displayName:f.get('displayName')}); setUser(await api<User>('/auth/me')); setMessage('Nome operatore aggiornato'); }); }}><h2>Nome operatore</h2><p>Questo nome sarà mostrato all’utente del PC durante le sessioni remote.</p><label>Nome visualizzato<input name="displayName" autoComplete="name" maxLength={80} defaultValue={user.displayName ?? ''} placeholder={user.email}/></label><button disabled={busy}>Salva nome</button></form><form className="panel settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void action(async () => { await api('/auth/email-notifications','PUT',{enabled:f.get('enabled') === 'on'}); setUser(await api<User>('/auth/me')); setMessage('Preferenza email aggiornata'); }); }}><h2>Notifiche email</h2><p>Ricevi un avviso generico quando cambia un ticket che puoi consultare. Il messaggio non contiene risposte, note interne o allegati.</p>{user.emailNotificationsAvailable ? <label className="check"><input type="checkbox" name="enabled" defaultChecked={user.emailNotificationsEnabled}/> Invia notifiche a {user.email}</label> : <p>Non disponibili: il server deve configurare SMTP sicuro e l’account deve avere un indirizzo email.</p>}<button disabled={busy || !user.emailNotificationsAvailable}>Salva preferenza</button></form><form className="panel settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); const form = e.currentTarget; void action(async () => { await api('/auth/password','POST',{currentPassword:f.get('current'),newPassword:f.get('next'),code:f.get('code') || undefined}); form.reset(); setMessage('Password aggiornata'); }); }}><h2>Cambia password</h2><label>Password attuale<input type="password" name="current" autoComplete="current-password" required/></label><label>Nuova password<input type="password" name="next" autoComplete="new-password" minLength={14} maxLength={1024} required/></label>{user.twoFactorEnabled && <label>Codice 2FA<input name="code" autoComplete="one-time-code" required inputMode="numeric"/></label>}<button disabled={busy}>Aggiorna password</button></form>
      {user.twoFactorAvailable ? <form className="panel settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void action(async () => { if (user.twoFactorEnabled) { await api('/auth/totp/disable','POST',{password:f.get('password'),code:f.get('code')}); setRecoveryCodes([]); setTotpSetup(null); } else if (totpSetup) { const result = await api<{recoveryCodes:string[]}>('/auth/totp/enable','POST',{password:f.get('password'),code:f.get('code')}); setRecoveryCodes(result.recoveryCodes); setTotpSetup(null); } else setTotpSetup(await api('/auth/totp/setup','POST',{password:f.get('password')})); setUser(await api<User>('/auth/me')); }); }}><h2>Autenticazione a due fattori</h2><p>{user.twoFactorEnabled ? 'Attiva' : 'Non attiva'}</p><label>Password attuale<input type="password" name="password" required autoComplete="current-password"/></label>{totpSetup && <><p>Inserisci questa chiave nell’app Authenticator (TOTP, 6 cifre, 30 secondi). Non condividerla.</p><code>{totpSetup.sharedKey}</code></>}{(totpSetup || user.twoFactorEnabled) && <label>Codice Authenticator<input name="code" inputMode="numeric" autoComplete="one-time-code" required maxLength={20}/></label>}<button disabled={busy}>{user.twoFactorEnabled ? 'Disattiva 2FA' : totpSetup ? 'Verifica e attiva' : 'Configura Authenticator'}</button></form> : <section className="panel"><h2>Autenticazione a due fattori</h2><p>Disponibile nel codice, disattivata nella configurazione del server.</p></section>}
      {recoveryCodes.length > 0 && <section className="panel"><h2>Codici di recupero</h2><p>Salvali in un posto riservato. Ogni codice può essere usato una sola volta.</p><pre>{recoveryCodes.join('\n')}</pre><button onClick={() => setRecoveryCodes([])}>Ho salvato i codici</button></section>}
    </>}
    {tab === 'users' && admin && <AccessManagement currentUserId={user.id}/>}
    {tab === 'workflows' && admin && <TicketWorkflowAdmin sites={sites} onMessage={setMessage}/>}
    {tab === 'audit' && admin && <section className="panel"><button disabled={busy} onClick={() => void action(async () => setAudit(await api<Audit[]>('/admin/audit')))}>Aggiorna</button><h2>Ultimi 100 eventi</h2><div className="table-wrap"><table><thead><tr><th>Data</th><th>Azione</th><th>Oggetto</th><th>Esito</th></tr></thead><tbody>{audit.map(a => <tr key={a.id}><td>{new Date(a.at).toLocaleString('it-IT')}</td><td>{a.action}</td><td>{a.target}</td><td>{a.outcome}</td></tr>)}</tbody></table></div></section>}
  </main></div>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
