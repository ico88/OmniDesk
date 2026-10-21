import React, { useEffect, useRef, useState } from 'react';
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
import { BackupSettings } from './BackupSettings';

type User = { id: string; email: string; displayName: string | null; emailNotificationsEnabled: boolean; emailNotificationsAvailable: boolean; globalPermissions: string[]; twoFactorAvailable: boolean; twoFactorEnabled: boolean };
type Branding = { name: string; logoUrl: string; primaryColor: string; theme: 'light' | 'dark' };
type Device = { id: string; name: string; groupId: string | null; siteId: string | null; online: boolean; revoked: boolean; lastSeen: string | null };
type Group = { id: string; name: string };
type Site = { id: string; name: string; isArchived: boolean };
type Audit = { id: number; at: string; action: string; target: string; outcome: string };
type EmailLoginChallenge = { requiresTwoFactor: true; challengeToken: string; maskedEmail: string; expiresInSeconds: number };
const defaults: Branding = { name: 'OmniDesk', logoUrl: '', primaryColor: '#2563eb', theme: 'dark' };

function DeviceOverview({ devices, refreshedAt, busy, onRefresh, onOpen }: { devices: Device[]; refreshedAt: Date | null; busy: boolean; onRefresh: () => void; onOpen: (device: Device) => void }) {
  const active = devices.filter(device => !device.revoked);
  const online = active.filter(device => device.online);
  const offline = active.filter(device => !device.online);
  const revoked = devices.filter(device => device.revoked);
  const attention = [...revoked, ...offline].sort((a, b) => (b.lastSeen ?? '').localeCompare(a.lastSeen ?? '')).slice(0, 6);
  const recent = [...devices].filter(device => device.lastSeen).sort((a, b) => (b.lastSeen ?? '').localeCompare(a.lastSeen ?? '')).slice(0, 6);
  const when = (value: string | null) => value ? new Date(value).toLocaleString('it-IT', { dateStyle: 'short', timeStyle: 'short' }) : 'Nessun contatto registrato';
  return <>
    <section className="overview-heading">
      <div><span className="eyebrow">STATO DEL PARCO</span><h2>Dispositivi e attività</h2><p>Stato basato sull’ultimo heartbeat ricevuto dal server.</p></div>
      <div className="overview-refresh"><small>{refreshedAt ? `Aggiornato ${refreshedAt.toLocaleTimeString('it-IT', { hour: '2-digit', minute: '2-digit' })}` : 'Caricamento stato…'}</small><button className="secondary" disabled={busy} onClick={onRefresh}><Icon name="refresh"/>Aggiorna</button></div>
    </section>
    <div className="stats overview-stats">
      {[[devices.length,'Dispositivi visibili','devices'],[online.length,'Online','check'],[offline.length,'Offline','security'],[revoked.length,'Revocati','close']].map(([value,label,icon])=><article key={String(label)} className="panel"><div className="stat-title"><small>{label}</small><Icon name={String(icon)}/></div><h2>{value}</h2><p>{label === 'Dispositivi visibili' ? 'Nel tuo ambito di accesso' : label === 'Online' ? 'Heartbeat recente' : label === 'Offline' ? 'Nessun heartbeat recente' : 'Credenziale revocata'}</p></article>)}
    </div>
    <div className="overview-columns">
      <section className="panel overview-list"><div className="section-heading"><div><span className="eyebrow">DA CONTROLLARE</span><h2>Dispositivi offline <span className="count">{offline.length + revoked.length}</span></h2></div></div>
        {attention.length === 0 ? <p className="overview-empty">Tutti i dispositivi autorizzati risultano online.</p> : <div className="overview-device-list">{attention.map(device=><article key={device.id}><span className={`status-pill ${device.revoked?'danger':'offline'}`}>{device.revoked?'Revocato':'Offline'}</span><div className="overview-device-info"><strong>{device.name}</strong><small>Ultimo contatto: {when(device.lastSeen)}</small></div><button className="secondary compact" onClick={()=>onOpen(device)}>Apri</button></article>)}</div>}
      </section>
      <section className="panel overview-list"><div className="section-heading"><div><span className="eyebrow">ATTIVITÀ RECENTE</span><h2>Ultimi contatti</h2></div></div>
        {recent.length === 0 ? <p className="overview-empty">Non sono ancora arrivati heartbeat dai dispositivi.</p> : <div className="overview-device-list">{recent.map(device=><article key={device.id}><span className={`status-pill ${device.revoked?'danger':device.online?'online':'offline'}`}>{device.revoked?'Revocato':device.online?'Online':'Offline'}</span><div className="overview-device-info"><strong>{device.name}</strong><small>Ultimo contatto: {when(device.lastSeen)}</small></div><button className="secondary compact" onClick={()=>onOpen(device)}>Apri</button></article>)}</div>}
      </section>
    </div>
  </>;
}

function passwordStrength(password: string) {
  const checks = [
    password.length >= 14,
    /\p{Ll}/u.test(password),
    /\p{Lu}/u.test(password),
    /\p{Nd}/u.test(password),
    /[^\p{L}\p{N}]/u.test(password),
  ];
  const score = checks.filter(Boolean).length;
  const labels = ['Inserisci una password', 'Molto debole', 'Debole', 'Discreta', 'Buona', 'Forte'];
  return { score, label: labels[score], valid: checks.every(Boolean) };
}

function App() {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [branding, setBranding] = useState(defaults);
  const [draft, setDraft] = useState(defaults);
  const [devices, setDevices] = useState<Device[]>([]);
  const [devicesRefreshedAt, setDevicesRefreshedAt] = useState<Date | null>(null);
  const [audit, setAudit] = useState<Audit[]>([]);
  const [widgets, setWidgets] = useState<string[]>(['devices','permissions','authentication']);
  const [totpSetup, setTotpSetup] = useState<{sharedKey:string;authenticatorUri:string}|null>(null);
  const [emailLoginChallenge, setEmailLoginChallenge] = useState<EmailLoginChallenge | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [groups, setGroups] = useState<Group[]>([]);
  const [sites, setSites] = useState<Site[]>([]);
  const [enrollment, setEnrollment] = useState('');
  const [addDeviceOpen, setAddDeviceOpen] = useState(false);
  const addDeviceDialog = useRef<HTMLDialogElement>(null);
  const [status, setStatus] = useState<DeviceStatus | null>(null);
  const initialTicket = new URLSearchParams(window.location.search).get('ticket');
  const [tab, setTab] = useState('overview');
  const [focusTicketId, setFocusTicketId] = useState<string|null>(null);
  const [portalFocusTicketId, setPortalFocusTicketId] = useState<string|null>(null);
  const [search,setSearch]=useState('');
  const admin = user?.globalPermissions.includes('administration') ?? false;
  const canReadKnowledge = user?.globalPermissions.some(p=>p==='knowledge.view'||p==='knowledge.manage') ?? false;

  useEffect(() => {
    const dialog = addDeviceDialog.current;
    if (!dialog) return;
    if (addDeviceOpen && !dialog.open) dialog.showModal();
    else if (!addDeviceOpen && dialog.open) dialog.close();
  }, [addDeviceOpen]);

  const filteredDevices=devices.filter(d=>d.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()));

  async function load() {
    const me = await api<User>('/auth/me');
    setUser(me);
    const [brand, list] = await Promise.all([api<Branding>('/branding'), api<Device[]>('/devices')]);
    setBranding(brand); setDraft(brand); setDevices(list); setDevicesRefreshedAt(new Date()); setWidgets((await api<{widgets:string[]}>('/dashboard/layout')).widgets);
    if (me.globalPermissions.includes('administration')) { const [events, groupList, siteList] = await Promise.all([api<Audit[]>('/admin/audit'), api<Group[]>('/admin/groups'), api<Site[]>('/admin/sites')]); setAudit(events); setGroups(groupList); setSites(siteList); }
    await refreshCsrf();
  }
  useEffect(() => {
    if (tab === 'devices' && admin) Promise.all([api<Group[]>('/admin/groups'), api<Site[]>('/admin/sites')]).then(([groupList, siteList]) => { setGroups(groupList); setSites(siteList); }).catch(e => setError(e instanceof Error ? e.message : 'Errore caricamento gruppi e sedi'));
  }, [tab, admin]);
  useEffect(()=>{
    if(!user||!['overview','devices','support'].includes(tab))return;
    let active=true;
    void api<Device[]>('/devices').then(list=>{if(active){setDevices(list);setDevicesRefreshedAt(new Date());}}).catch(e=>{if(active)setError(e instanceof Error?e.message:'Elenco dispositivi non disponibile');});
    return()=>{active=false;};
  },[tab,user?.id]);
  useEffect(()=>{
    if(!user||!['overview','devices'].includes(tab))return;
    let active=true;
    const timer=window.setInterval(()=>{void api<Device[]>('/devices').then(list=>{if(active){setDevices(list);setDevicesRefreshedAt(new Date());}}).catch(e=>{if(active)setError(e instanceof Error?e.message:'Stato dispositivi non aggiornato');});},30_000);
    return()=>{active=false;window.clearInterval(timer);};
  },[tab,user?.id]);
  useEffect(() => { load().catch(() => setUser(null)).finally(() => setLoading(false)); }, []);
  useEffect(() => {
    document.documentElement.dataset.theme = branding.theme;
    document.documentElement.style.setProperty('--primary', branding.primaryColor);
    document.title = branding.name;
  }, [branding]);
  async function action(work: () => Promise<void>) {
    setBusy(true); setError(''); setMessage('');
    try { await work(); } catch (e) { setError(e instanceof Error ? e.message : 'Errore inatteso'); }
    finally { setBusy(false); }
  }
  async function openTicket(ticketId: string) {
    const supportTickets = await api<{id:string}[]>(`/support?ticketId=${encodeURIComponent(ticketId)}`).catch(() => []);
    if (supportTickets.some(ticket => ticket.id === ticketId)) {
      setFocusTicketId(ticketId); setTab('support'); return;
    }
    const portalTickets = await api<{id:string}[]>('/portal/tickets');
    if (portalTickets.some(ticket => ticket.id === ticketId)) {
      setPortalFocusTicketId(ticketId); setTab('myTickets'); return;
    }
    setError('Il ticket non è più disponibile per questo account.');
  }
  useEffect(() => {
    if (user && initialTicket) void openTicket(initialTicket).catch(() => setError('Il ticket non è disponibile per questo account.'));
  }, [user?.id, initialTicket]);
  if (loading) return <main className="login"><p>Connessione a OmniDesk…</p></main>;
  if (!user) return <main className="login">{emailLoginChallenge ? <form className="panel login-card" onSubmit={e => {
    e.preventDefault(); const form = new FormData(e.currentTarget);
    void action(async () => { await api('/auth/login/email-code', 'POST', { challengeToken: emailLoginChallenge.challengeToken, code: form.get('code') }); setEmailLoginChallenge(null); await load(); });
  }}><img className="brand-mark" src="/favicon.svg" alt=""/><h1>Verifica accesso</h1><p>Abbiamo inviato un codice temporaneo a <strong>{emailLoginChallenge.maskedEmail}</strong>. Scade tra 10 minuti.</p>
    <label>Codice email<input name="code" type="text" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" minLength={6} maxLength={6} required autoFocus/></label>
    {error && <p role="alert" className="error">{error}</p>}{message && <p role="status" className="notice">{message}</p>}
    <button disabled={busy}>{busy ? 'Verifica…' : 'Verifica e accedi'}</button>
    <button type="button" className="secondary" disabled={busy} onClick={() => void action(async () => { await api('/auth/login/email-code/resend', 'POST', { challengeToken: emailLoginChallenge.challengeToken }); setMessage('Nuovo codice inviato. Il codice precedente non è più valido.'); })}>Invia un nuovo codice</button>
    <button type="button" className="login-back" disabled={busy} onClick={() => { setEmailLoginChallenge(null); setError(''); setMessage(''); }}>Torna all’accesso</button>
  </form> : <form className="panel login-card" onSubmit={e => {
    e.preventDefault(); const form = new FormData(e.currentTarget);
    void action(async () => { const result = await api<EmailLoginChallenge | undefined>('/auth/login', 'POST', { email: form.get('email'), password: form.get('password'), code:form.get('code') || undefined, recoveryCode:form.get('recoveryCode') || undefined }); if (result?.requiresTwoFactor) { setEmailLoginChallenge(result); return; } await load(); });
  }}><img className="brand-mark" src="/favicon.svg" alt=""/><h1>OmniDesk</h1><p>Accedi alla console IT</p>
    <label>Email<input name="email" type="email" autoComplete="username" required maxLength={254}/></label>
    <label>Password<input name="password" type="password" autoComplete="current-password" required maxLength={1024}/></label>
    <details className="login-options"><summary>Usa un codice Authenticator o di recupero</summary><label>Codice Authenticator<input name="code" inputMode="numeric" autoComplete="one-time-code" maxLength={20}/></label><label>Oppure codice di recupero<input name="recoveryCode" autoComplete="off" maxLength={100}/></label></details>
    {error && <p role="alert" className="error">{error}</p>}<button disabled={busy}>{busy ? 'Accesso…' : 'Accedi'}</button>
    <small>Se richiesto dall’amministratore, completa l’accesso con il codice temporaneo inviato via email.</small>
  </form>}</main>;
  return <div className="shell"><aside>
    <div className="brand">{branding.logoUrl ? <img src={branding.logoUrl} alt="Logo" referrerPolicy="no-referrer"/> : <img className="brand-mark" src="/favicon.svg" alt=""/>}<strong>{branding.name}</strong></div>
    <small className="nav-label">WORKSPACE</small>
    <nav aria-label="Navigazione">{[['overview','Panoramica'],['devices','Dispositivi'],['security','Account e sicurezza'],['myTickets','I miei ticket'],['support','Gestione ticket'],...(canReadKnowledge ? [['knowledge','Knowledge base']] : []),...(admin ? [['branding','Personalizzazione'],['backups','Backup'],['users','Utenti'],['audit','Audit']] : [])].map(([id,label]) => <button key={id} className={tab === id ? 'active' : ''} aria-current={tab===id?'page':undefined} onClick={() => { if (id !== 'security') { setNewPassword(''); setConfirmPassword(''); } setTab(id); setTotpSetup(null); setRecoveryCodes([]); setEnrollment(''); setStatus(null);  setError(''); setMessage(''); }}><Icon name={id}/><span>{label}</span></button>)}</nav>
    <div className="account"><small>{user.email}</small><button disabled={busy} onClick={() => void action(async () => { await api('/auth/logout', 'POST'); setUser(null); setNewPassword(''); setConfirmPassword(''); setTotpSetup(null); setRecoveryCodes([]); setEnrollment(''); setStatus(null); setGroups([]); setBranding(defaults); setAudit([]); setDevices([]); setTab('overview'); await refreshCsrf(); })}><Icon name="logout"/>Esci</button></div>
  </aside><main className="content" aria-busy={busy}><header><div><small className="eyebrow">WORKSPACE / GESTIONE IT</small><h1>{{overview:'Panoramica',devices:'Dispositivi',branding:'Personalizzazione',backups:'Backup server',users:'Utenti',audit:'Audit amministrativo',security:'Account e sicurezza',myTickets:'I miei ticket',support:'Gestione ticket',knowledge:'Knowledge base'}[tab]}</h1></div><div className="header-actions"><Notifications onOpenTicket={openTicket} onOpenDevice={id=>action(async()=>{const next=await api<DeviceStatus>(`/devices/${id}/status`);setStatus(next);setTab('devices');})}/><span className="badge"><Icon name="security" size={15}/>{admin?'Amministratore':'Accesso limitato'}</span></div></header>
    {error && <p role="alert" className="error">{error}</p>}{message && <p role="status" className="notice"><Icon name="check"/>{message}</p>}
    {tab === 'support' && <SupportDesk sites={sites} devices={devices} visibleDeviceIds={devices.map(d=>d.id)} focusTicketId={focusTicketId} onTicketFocused={()=>setFocusTicketId(null)} onOpenDevice={id=>action(async()=>{const next=await api<DeviceStatus>(`/devices/${id}/status`);setStatus(next);setTab('devices');})}/>}
    {tab === 'myTickets' && <CustomerPortal onMessage={setMessage} focusTicketId={portalFocusTicketId} onTicketFocused={()=>setPortalFocusTicketId(null)}/>}
    {tab === 'knowledge' && canReadKnowledge && <KnowledgeBase canManage={user.globalPermissions.includes('knowledge.manage')} onMessage={setMessage}/>}
    {tab === 'overview' && <><DeviceOverview devices={devices} refreshedAt={devicesRefreshedAt} busy={busy} onRefresh={()=>void action(async()=>{const list=await api<Device[]>('/devices');setDevices(list);setDevicesRefreshedAt(new Date());})} onOpen={device=>void action(async()=>{setStatus(await api<DeviceStatus>(`/devices/${device.id}/status`));setTab('devices');})}/><section className="panel welcome"><div><span className="eyebrow">IL TUO SPAZIO DI LAVORO</span><h2>Assistenza, senza perdere il filo.</h2><p>Trova un dispositivo, avvia una sessione autorizzata o segui le richieste di assistenza.</p></div><div className="action-row"><button onClick={()=>setTab('devices')}><Icon name="devices"/>Vai ai dispositivi</button><button className="secondary" onClick={()=>setTab('support')}><Icon name="support"/>Apri assistenza</button></div></section></>}
    {tab === 'devices' && <><section className="panel device-list"><div className="section-heading"><div><span className="eyebrow">DISPOSITIVI</span><h2>Parco macchine <span className="count">{devices.length}</span></h2></div><div className="action-row">{admin&&<button onClick={()=>{setEnrollment('');setAddDeviceOpen(true);}}><Icon name="plus"/>Aggiungi dispositivo</button>}<button className="secondary" disabled={busy} onClick={() => void action(async () => setDevices(await api<Device[]>('/devices')))}><Icon name="refresh"/>Aggiorna</button></div></div>
      <label className="search-field"><Icon name="search"/><input aria-label="Cerca dispositivo" placeholder="Cerca un dispositivo per nome…" value={search} onChange={e=>setSearch(e.target.value)}/></label>
      {devices.length===0?<div className="empty-state"><Icon name="devices" size={36}/><h3>Il tuo parco macchine parte da qui</h3><p>Registra un Agent Windows per visualizzare il primo dispositivo.</p></div>:filteredDevices.length===0?<p>Nessun dispositivo corrisponde alla ricerca.</p>:<div className="table-wrap"><table><thead><tr><th>Dispositivo</th><th>Stato Agent</th><th>Gruppo</th><th>Sede</th><th></th></tr></thead><tbody>{filteredDevices.map(d=><tr className={status?.id===d.id?'selected-row':''} key={d.id}><td><span className="device-name"><span className="device-symbol"><Icon name="devices"/></span><span title={d.id}>{d.name}</span></span></td><td><span className={`status-pill ${d.revoked?'danger':d.online?'online':'offline'}`} title={d.lastSeen?`Ultimo heartbeat ${new Date(d.lastSeen).toLocaleString('it-IT')}`:'Nessun heartbeat ricevuto'}>{d.revoked?'Revocato':d.online?'Online':'Offline'}</span></td><td>{groups.find(g=>g.id===d.groupId)?.name??(d.groupId?'Gruppo assegnato':'Senza gruppo')}</td><td>{sites.find(s=>s.id===d.siteId)?.name??'Sede non disponibile'}</td><td className="row-action"><button className="secondary" disabled={busy} onClick={()=>void action(async()=>{setStatus(await api<DeviceStatus>(`/devices/${d.id}/status`));})}>Apri<Icon name="arrow" size={16}/></button></td></tr>)}</tbody></table></div>}
    </section>
      {status && <DeviceWorkspace key={status.id} device={status} admin={admin} groups={groups} sites={sites} onMessage={setMessage} onDeviceChanged={async message => {
        const id = status.id;
        const [updated, list] = await Promise.all([api<DeviceStatus>(`/devices/${id}/status`), api<Device[]>('/devices')]);
        setStatus(current => current?.id === id ? updated : current); setDevices(list); setMessage(message);
      }}/>}
      {admin&&<dialog ref={addDeviceDialog} className="add-device-dialog" onCancel={event=>{event.preventDefault();setAddDeviceOpen(false);}} onClose={()=>setAddDeviceOpen(false)}><div className="section-heading"><div><span className="eyebrow">NUOVO DISPOSITIVO</span><h2>Aggiungi un dispositivo</h2></div><button type="button" className="secondary compact" aria-label="Chiudi" onClick={()=>setAddDeviceOpen(false)}><Icon name="close"/></button></div><p>Segui i due passaggi per scaricare l’Agent e registrare il computer in OmniDesk.</p><div className="device-enrollment-steps"><section><span className="step-number">1</span><AgentDownload/></section><section><span className="step-number">2</span><form className="panel settings enrollment" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); setEnrollment(''); void action(async () => { const issued = await api<{token:string}>('/admin/enrollment-tokens','POST',{groupId:f.get('group') || null, siteId:f.get('site'), expiresInMinutes:30}); setEnrollment(issued.token); }); }}><h2>Registra il computer</h2><label>Sede<select aria-label="Sede" name="site" required>{sites.filter(s => !s.isArchived).map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label><label>Gruppo<select aria-label="Gruppo" name="group"><option value="">Senza gruppo</option>{groups.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label><button disabled={busy || sites.every(s => s.isArchived)}>Genera token monouso (30 minuti)</button>{enrollment && <><p>Copia il token nel prompt dell’installer. È monouso e viene mostrato solo qui.</p><textarea aria-label="Token enrollment" readOnly value={enrollment} rows={3}/><button type="button" className="secondary" onClick={() => setEnrollment('')}>Nascondi token</button></>}</form></section></div></dialog>}
    </>}
    {tab === 'branding' && admin && <form className="panel settings" onSubmit={e => { e.preventDefault(); void action(async () => { const saved = await api<Branding>('/branding','PUT',draft); setBranding(saved); setMessage('Personalizzazione salvata'); }); }}>
      <h2>Identità della dashboard</h2><label>Nome<input required maxLength={100} value={draft.name} onChange={e => setDraft({...draft,name:e.target.value})}/></label>
      <label>Logo (URL HTTPS)<input type="url" maxLength={2048} value={draft.logoUrl} onChange={e => setDraft({...draft,logoUrl:e.target.value})}/></label>
      <label>Colore principale<input type="color" value={draft.primaryColor} onChange={e => setDraft({...draft,primaryColor:e.target.value})}/></label>
      <label>Tema<select aria-label="Tema" value={draft.theme} onChange={e => setDraft({...draft,theme:e.target.value as Branding['theme']})}><option value="dark">Scuro</option><option value="light">Chiaro</option></select></label>
      <button disabled={busy}>Salva personalizzazione</button>
    </form>}
    {tab === 'backups' && admin && <BackupSettings busy={busy} onMessage={setMessage} onError={setError}/>}
    {tab === 'security' && <div className="security-settings-grid"><form className="panel settings security-form" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void action(async () => { await api('/auth/profile','PUT',{displayName:f.get('displayName')}); setUser(await api<User>('/auth/me')); setMessage('Nome operatore aggiornato'); }); }}><h2>Nome operatore</h2><p>Questo nome sarà mostrato all’utente del PC durante le sessioni remote.</p><label>Nome visualizzato<input name="displayName" autoComplete="name" maxLength={80} defaultValue={user.displayName ?? ''} placeholder={user.email}/></label><button disabled={busy}>Salva nome</button></form><form className="panel settings security-form" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void action(async () => { await api('/auth/email-notifications','PUT',{enabled:f.get('enabled') === 'on'}); setUser(await api<User>('/auth/me')); setMessage('Preferenza email aggiornata'); }); }}><h2>Notifiche email</h2><p>Ricevi avvisi per i ticket che puoi consultare e quando un dispositivo visibile passa offline. I messaggi non includono risposte, note interne o allegati.</p>{user.emailNotificationsAvailable ? <label className="check"><input type="checkbox" name="enabled" defaultChecked={user.emailNotificationsEnabled}/> Invia notifiche a {user.email}</label> : <p>Non disponibili: il server deve configurare SMTP sicuro e l’account deve avere un indirizzo email.</p>}<button disabled={busy || !user.emailNotificationsAvailable}>Salva preferenza</button></form><form className="panel settings security-form" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); const form = e.currentTarget; const password = String(f.get('next') ?? ''); if (password !== String(f.get('confirm') ?? '')) { setError('Le due password non coincidono.'); return; } if (!passwordStrength(password).valid) { setError('La password deve avere almeno 14 caratteri, maiuscole, minuscole, numeri e simboli.'); return; } void action(async () => { await api('/auth/password','POST',{currentPassword:f.get('current'),newPassword:password,code:f.get('code') || undefined}); form.reset(); setNewPassword(''); setConfirmPassword(''); setMessage('Password aggiornata'); }); }}><h2>Cambia password</h2><label>Password attuale<input type="password" name="current" autoComplete="current-password" required/></label><label>Nuova password<input type="password" name="next" autoComplete="new-password" minLength={14} maxLength={1024} required value={newPassword} onChange={e => setNewPassword(e.target.value)} aria-describedby="password-strength"/></label><div id="password-strength" className={`password-strength strength-${passwordStrength(newPassword).score}`}><div className="password-strength-heading"><span>Sicurezza password</span><strong>{passwordStrength(newPassword).label}</strong></div><div className="password-strength-track" role="progressbar" aria-label="Sicurezza password" aria-valuemin={0} aria-valuemax={5} aria-valuenow={passwordStrength(newPassword).score}><span style={{width:`${passwordStrength(newPassword).score * 20}%`}}/></div><small>Minimo 14 caratteri, maiuscole, minuscole, numero e simbolo.</small></div><label>Conferma nuova password<input type="password" name="confirm" autoComplete="new-password" minLength={14} maxLength={1024} required value={confirmPassword} onChange={e => setConfirmPassword(e.target.value)} aria-invalid={confirmPassword.length > 0 && confirmPassword !== newPassword} aria-describedby="password-match"/></label><small id="password-match" className={confirmPassword.length === 0 ? 'password-match' : confirmPassword === newPassword ? 'password-match matches' : 'password-match mismatch'} aria-live="polite">{confirmPassword.length === 0 ? 'Ripeti la nuova password.' : confirmPassword === newPassword ? 'Le password coincidono.' : 'Le password non coincidono.'}</small>{user.twoFactorEnabled && <label>Codice 2FA<input name="code" autoComplete="one-time-code" required inputMode="numeric"/></label>}<button disabled={busy || !passwordStrength(newPassword).valid || newPassword !== confirmPassword}>Aggiorna password</button></form>
      {user.twoFactorAvailable ? <form className="panel settings security-form" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void action(async () => { if (user.twoFactorEnabled) { await api('/auth/totp/disable','POST',{password:f.get('password'),code:f.get('code')}); setRecoveryCodes([]); setTotpSetup(null); } else if (totpSetup) { const result = await api<{recoveryCodes:string[]}>('/auth/totp/enable','POST',{password:f.get('password'),code:f.get('code')}); setRecoveryCodes(result.recoveryCodes); setTotpSetup(null); } else setTotpSetup(await api('/auth/totp/setup','POST',{password:f.get('password')})); setUser(await api<User>('/auth/me')); }); }}><h2>Autenticazione a due fattori</h2><p>{user.twoFactorEnabled ? 'Attiva' : 'Non attiva'}</p><label>Password attuale<input type="password" name="password" required autoComplete="current-password"/></label>{totpSetup && <><p>Inserisci questa chiave nell’app Authenticator (TOTP, 6 cifre, 30 secondi). Non condividerla.</p><code>{totpSetup.sharedKey}</code></>}{(totpSetup || user.twoFactorEnabled) && <label>Codice Authenticator<input name="code" inputMode="numeric" autoComplete="one-time-code" required maxLength={20}/></label>}<button disabled={busy}>{user.twoFactorEnabled ? 'Disattiva 2FA' : totpSetup ? 'Verifica e attiva' : 'Configura Authenticator'}</button></form> : <section className="panel"><h2>Autenticazione a due fattori</h2><p>Disponibile nel codice, disattivata nella configurazione del server.</p></section>}
      {recoveryCodes.length > 0 && <section className="panel"><h2>Codici di recupero</h2><p>Salvali in un posto riservato. Ogni codice può essere usato una sola volta.</p><pre>{recoveryCodes.join('\n')}</pre><button onClick={() => setRecoveryCodes([])}>Ho salvato i codici</button></section>}
    </div>}
    {tab === 'users' && admin && <AccessManagement currentUserId={user.id}/>}
    {tab === 'audit' && admin && <section className="panel"><button disabled={busy} onClick={() => void action(async () => setAudit(await api<Audit[]>('/admin/audit')))}>Aggiorna</button><h2>Ultimi 100 eventi</h2><div className="table-wrap"><table><thead><tr><th>Data</th><th>Azione</th><th>Oggetto</th><th>Esito</th></tr></thead><tbody>{audit.map(a => <tr key={a.id}><td>{new Date(a.at).toLocaleString('it-IT')}</td><td>{a.action}</td><td>{a.target}</td><td>{a.outcome}</td></tr>)}</tbody></table></div></section>}
  </main></div>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
