import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, refreshCsrf } from './api';
import './style.css';
import { AccessManagement } from './AccessManagement';
import { DeviceManagement } from './DeviceManagement';
import { SupportDesk } from './SupportDesk';
import { RemoteSessions } from './RemoteSessions';
import { AgentDownload } from './AgentDownload';
import { Icon } from './Icon';

type User = { id: string; email: string; globalPermissions: string[]; twoFactorAvailable: boolean; twoFactorEnabled: boolean };
type Branding = { name: string; logoUrl: string; primaryColor: string; theme: 'light' | 'dark' };
type Device = { id: string; name: string; groupId: string | null };
type Group = { id: string; name: string };
type DeviceStatus = { id: string; groupId: string | null; capabilities: string[]; name: string; online: boolean; revoked: boolean; lastSeen: string | null; metrics: {cpuPercent:number|null; totalMemoryBytes:number; availableMemoryBytes:number; networkReceivedBytes:number; networkSentBytes:number; uptimeSeconds:number; disks:{name:string;totalBytes:number;freeBytes:number}[]} | null; inventory: { operatingSystem:string; architecture:string; manufacturer:string|null; model:string|null; processor:string|null; logicalProcessors:number; software:{name:string;version:string|null;publisher:string|null}[]; warnings:string[]} | null };
type UpdateItem = {UpdateId:string;Revision:number;Title:string;Kb:string[];ResultCode:number|null};
type UpdateJob = {id:string;kind:string;state:string;createdAt:string;result:string|null};
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
  const [jobs, setJobs] = useState<UpdateJob[]>([]);
  const [selectedUpdates, setSelectedUpdates] = useState<UpdateItem[]>([]);
  const [groups, setGroups] = useState<Group[]>([]);
  const [enrollment, setEnrollment] = useState('');
  const [remote, setRemote] = useState<{id:string;url:string;expiresAt:string;deviceId:string} | null>(null);
  const [status, setStatus] = useState<DeviceStatus | null>(null);
  const [tab, setTab] = useState('overview');
  const [search,setSearch]=useState('');
  const [remoteReady,setRemoteReady]=useState(false);
  const admin = user?.globalPermissions.includes('administration') ?? false;

  const filteredDevices=devices.filter(d=>d.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()));

  async function load() {
    const me = await api<User>('/auth/me');
    setUser(me);
    const [brand, list] = await Promise.all([api<Branding>('/branding'), api<Device[]>('/devices')]);
    setBranding(brand); setDraft(brand); setDevices(list); setWidgets((await api<{widgets:string[]}>('/dashboard/layout')).widgets);
    if (me.globalPermissions.includes('administration')) { setAudit(await api<Audit[]>('/admin/audit')); setGroups(await api<Group[]>('/admin/groups')); }
    await refreshCsrf();
  }
  useEffect(() => {
    if (tab === 'devices' && admin) api<Group[]>('/admin/groups').then(setGroups).catch(e => setError(e instanceof Error ? e.message : 'Errore caricamento gruppi'));
  }, [tab, admin]);
  useEffect(()=>{
    if(!user||!['devices','support'].includes(tab))return;
    let active=true;
    void api<Device[]>('/devices').then(list=>{if(active)setDevices(list);}).catch(e=>{if(active)setError(e instanceof Error?e.message:'Elenco dispositivi non disponibile');});
    return()=>{active=false;};
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
  if (loading) return <main className="login"><p>Connessione a OmniDesk…</p></main>;
  if (!user) return <main className="login"><form className="panel login-card" onSubmit={e => {
    e.preventDefault(); const form = new FormData(e.currentTarget);
    void action(async () => { await api('/auth/login', 'POST', { email: form.get('email'), password: form.get('password'), code:form.get('code') || undefined, recoveryCode:form.get('recoveryCode') || undefined }); await load(); });
  }}><span className="mark"><Icon name="devices" size={24}/></span><h1>OmniDesk</h1><p>Accedi alla console IT</p>
    <label>Email<input name="email" type="email" autoComplete="username" required maxLength={254}/></label>
    <label>Password<input name="password" type="password" autoComplete="current-password" required maxLength={1024}/></label>
    <details className="login-options"><summary>Usa un codice 2FA o di recupero</summary><label>Codice 2FA (se attiva)<input name="code" inputMode="numeric" autoComplete="one-time-code" maxLength={20}/></label><label>Oppure codice di recupero<input name="recoveryCode" autoComplete="off" maxLength={100}/></label></details>
    {error && <p role="alert" className="error">{error}</p>}<button disabled={busy}>{busy ? 'Accesso…' : 'Accedi'}</button>
    <small>Account creati dall’amministratore. 2FA opzionale, inizialmente disattivata.</small>
  </form></main>;
  return <div className="shell"><aside>
    <div className="brand">{branding.logoUrl ? <img src={branding.logoUrl} alt="Logo" referrerPolicy="no-referrer"/> : <span className="mark"><Icon name="devices" size={24}/></span>}<strong>{branding.name}</strong></div>
    <small className="nav-label">WORKSPACE</small>
    <nav aria-label="Navigazione">{[['overview','Panoramica'],['devices','Dispositivi'],['security','Account e sicurezza'],['support','Assistenza'],...(admin ? [['branding','Personalizzazione'],['users','Utenti'],['audit','Audit']] : [])].map(([id,label]) => <button key={id} className={tab === id ? 'active' : ''} aria-current={tab===id?'page':undefined} onClick={() => { setTab(id); setTotpSetup(null); setRecoveryCodes([]); setJobs([]); setSelectedUpdates([]); setRemote(null); setEnrollment(''); setStatus(null); setRemoteReady(false); setError(''); setMessage(''); }}><Icon name={id}/><span>{label}</span></button>)}</nav>
    <div className="account"><small>{user.email}</small><button disabled={busy} onClick={() => void action(async () => { await api('/auth/logout', 'POST'); setUser(null); setTotpSetup(null); setRecoveryCodes([]); setRemote(null); setEnrollment(''); setStatus(null); setGroups([]); setBranding(defaults); setAudit([]); setDevices([]); setTab('overview'); await refreshCsrf(); })}><Icon name="logout"/>Esci</button></div>
  </aside><main className="content" aria-busy={busy}><header><div><small className="eyebrow">WORKSPACE / GESTIONE IT</small><h1>{{overview:'Panoramica',devices:'Dispositivi',branding:'Personalizzazione',users:'Utenti',audit:'Audit amministrativo',security:'Account e sicurezza',support:'Assistenza'}[tab]}</h1></div><span className="badge"><Icon name="security" size={15}/>{admin?'Amministratore':'Accesso limitato'}</span></header>
    {error && <p role="alert" className="error">{error}</p>}{message && <p role="status" className="notice"><Icon name="check"/>{message}</p>}
    {tab === 'support' && <SupportDesk visibleDeviceIds={devices.map(d=>d.id)} onOpenDevice={id=>action(async()=>{const next=await api<DeviceStatus>(`/devices/${id}/status`);setRemote(null);setRemoteReady(false);setJobs([]);setSelectedUpdates([]);setStatus(next);setTab('devices');})}/>}
    {tab === 'overview' && <><div className="stats">{widgets.map(widget => <article key={widget} className="panel">{widget === 'devices' ? <><div className="stat-title"><small>Dispositivi visibili</small><Icon name="devices"/></div><h2>{devices.length}</h2><p>Autorizzati per il tuo account</p></> : widget === 'permissions' ? <><div className="stat-title"><small>Permessi globali</small><Icon name="users"/></div><h2>{user.globalPermissions.length}</h2><p>{user.globalPermissions.join(', ') || 'Solo concessioni locali'}</p></> : <><div className="stat-title"><small>Autenticazione</small><Icon name="security"/></div><h2>Identity</h2><p>{user.twoFactorEnabled ? '2FA attiva' : '2FA non attiva'}</p></>}</article>)}</div><details className="panel"><summary>Personalizza i tuoi riquadri</summary>{[['devices','Dispositivi'],['permissions','Permessi'],['authentication','Autenticazione']].map(([id,label]) => <label key={id}><input style={{width:'auto'}} type="checkbox" checked={widgets.includes(id)} disabled={busy} onChange={e => { const previous = widgets; const next = e.target.checked ? [...widgets,id] : widgets.filter(x => x !== id); setWidgets(next); void action(async () => { try { await api('/dashboard/layout','PUT',{widgets:next}); } catch (error) { setWidgets(previous); throw error; } }); }}/>{label}</label>)}<button disabled={busy || widgets.length < 2} onClick={() => void action(async () => { const next=[...widgets.slice(1),widgets[0]]; await api('/dashboard/layout','PUT',{widgets:next}); setWidgets(next); })}>Ruota ordine riquadri</button></details><section className="panel welcome"><div><span className="eyebrow">IL TUO SPAZIO DI LAVORO</span><h2>Assistenza, senza perdere il filo.</h2><p>Trova un dispositivo, avvia una sessione autorizzata o segui le richieste di assistenza.</p></div><div className="action-row"><button onClick={()=>setTab('devices')}><Icon name="devices"/>Vai ai dispositivi</button><button className="secondary" onClick={()=>setTab('support')}><Icon name="support"/>Apri assistenza</button></div></section></>}
    {tab === 'devices' && <><section className="panel device-list"><div className="section-heading"><div><span className="eyebrow">DISPOSITIVI</span><h2>Parco macchine <span className="count">{devices.length}</span></h2></div><button className="secondary" disabled={busy} onClick={() => void action(async () => setDevices(await api<Device[]>('/devices')))}><Icon name="refresh"/>Aggiorna</button></div>
      <label className="search-field"><Icon name="search"/><input aria-label="Cerca dispositivo" placeholder="Cerca un dispositivo per nome…" value={search} onChange={e=>setSearch(e.target.value)}/></label>
      {devices.length===0?<div className="empty-state"><Icon name="devices" size={36}/><h3>Il tuo parco macchine parte da qui</h3><p>Registra un Agent Windows per visualizzare il primo dispositivo.</p></div>:filteredDevices.length===0?<p>Nessun dispositivo corrisponde alla ricerca.</p>:<div className="table-wrap"><table><thead><tr><th>Dispositivo</th><th>Gruppo</th><th></th></tr></thead><tbody>{filteredDevices.map(d=><tr className={status?.id===d.id?'selected-row':''} key={d.id}><td><span className="device-name"><span className="device-symbol"><Icon name="devices"/></span><span title={d.id}>{d.name}</span></span></td><td>{groups.find(g=>g.id===d.groupId)?.name??(d.groupId?'Gruppo assegnato':'Senza gruppo')}</td><td className="row-action"><button className="secondary" disabled={busy} onClick={()=>void action(async()=>{setRemoteReady(false);setRemote(null);setStatus(await api<DeviceStatus>(`/devices/${d.id}/status`));setJobs([]);setSelectedUpdates([]);})}>Apri<Icon name="arrow" size={16}/></button></td></tr>)}</tbody></table></div>}
    </section>
      {status && <section className="panel device-detail"><div className="section-heading"><h2><Icon name="devices" size={25}/>{status.name}</h2><span className={`status-pill ${status.revoked?'danger':status.online?'online':'offline'}`}>{status.revoked?'Revocato':status.online?'Online':'Offline'}</span></div><p className="last-seen">Ultimo contatto: {status.lastSeen ? new Date(status.lastSeen).toLocaleString('it-IT') : 'Mai'}</p>
        {admin && <details className="advanced"><summary><Icon name="settings"/>Gestione dispositivo</summary><DeviceManagement key={`${status.id}:${status.name}:${status.groupId}:${status.revoked}`} device={status} groups={groups} onChanged={async message => {
          const id = status.id;
          const [updated, list] = await Promise.all([api<DeviceStatus>(`/devices/${id}/status`), api<Device[]>('/devices')]);
          setStatus(current => current?.id === id ? updated : current); setDevices(list); setRemote(null); setJobs([]); setSelectedUpdates([]); setMessage(message);
        }}/></details>}
        <div className="action-row remote-actions">{[['remote.control','Controllo remoto'],['terminal','Terminale'],['files','File']].filter(([cap]) => status.capabilities.includes(cap)).map(([cap,label]) => <button key={cap} disabled={busy || status.revoked || !remoteReady} onClick={() => { const popup=window.open('about:blank','_blank');if(popup)popup.opener=null;void action(async () => { try {setRemote(null); const created = await api<{id:string;url:string;expiresAt:string}>(`/devices/${status.id}/remote-sessions`,'POST',{capability:cap}); setRemote({...created,deviceId:status.id});if(popup&&!popup.closed)popup.location.replace(created.url); }catch(e){popup?.close();throw e;} }); }}><Icon name={cap==='remote.control'?'devices':cap==='terminal'?'terminal':'files'}/>{label}</button>)}</div>
        {remote && remote.deviceId === status.id && <p className="session-ready"><span>Sessione autorizzata pronta</span><a className="button" href={remote.url} target="_blank" rel="noreferrer noopener">Apri sessione autorizzata</a> · Scade alle {new Date(remote.expiresAt).toLocaleTimeString('it-IT')} <button disabled={busy} onClick={() => void action(async () => { const result = await api<{state:string} | undefined>(`/devices/${remote.deviceId}/remote-sessions/${remote.id}`,'DELETE'); setMessage(result?.state === 'revocation_pending' ? 'Revoca richiesta: il server ritenta la chiusura. Il motore non ha ancora confermato.' : 'Revoca confermata dal motore.'); setRemote(null); })}>Revoca sessione</button></p>}
        <RemoteSessions key={status.id} deviceId={status.id} admin={admin} onAvailability={setRemoteReady}/>
        {status.metrics && <><div className="metrics-strip">
          <article><span><Icon name="cpu"/>CPU</span><strong>{status.metrics.cpuPercent===null?'Non disponibile':status.metrics.cpuPercent.toFixed(1)+'%'}</strong><small>Ultimo campione ricevuto</small>{status.metrics.cpuPercent!==null&&<div className="meter"><i style={{width:Math.min(100,Math.max(0,status.metrics.cpuPercent))+'%'}}/></div>}</article>
          <article><span><Icon name="devices"/>Memoria disponibile</span><strong>{(status.metrics.availableMemoryBytes/1073741824).toFixed(1)} <small>GiB</small></strong><small>su {(status.metrics.totalMemoryBytes/1073741824).toFixed(1)} GiB totali</small></article>
          <article><span><Icon name="clock"/>Uptime</span><strong>{Math.floor(status.metrics.uptimeSeconds/3600)} <small>ore</small></strong><small>Dall’ultimo avvio del sistema</small></article>
        </div><details className="advanced"><summary>Archiviazione e rete</summary><p>Traffico cumulativo: {(status.metrics.networkReceivedBytes/1073741824).toFixed(2)} GiB ricevuti · {(status.metrics.networkSentBytes/1073741824).toFixed(2)} GiB inviati</p>{status.metrics.disks.map(d=><p key={d.name}>{d.name} · {(d.freeBytes/1073741824).toFixed(1)} / {(d.totalBytes/1073741824).toFixed(1)} GiB liberi</p>)}</details></>}
        {status.capabilities.includes('updates') && <details className="advanced"><summary><Icon name="refresh"/>Windows Update</summary><section><h2>Windows Update</h2><button disabled={busy || status.revoked} onClick={() => void action(async () => { await api(`/devices/${status.id}/updates/scan`,'POST',{requestId:crypto.randomUUID()}); setJobs(await api<UpdateJob[]>(`/devices/${status.id}/updates`)); })}>Avvia scansione</button> <button disabled={busy} onClick={() => void action(async () => { setJobs(await api<UpdateJob[]>(`/devices/${status.id}/updates`)); setSelectedUpdates([]); })}>Aggiorna lavori</button><p>I lavori vengono eseguiti dall’agente. Nessun riavvio automatico.</p>
          {jobs.map(job => <article key={job.id}><p>{job.kind==='scan'?'Scansione':'Installazione'} · {({queued:'In coda',leased:'In esecuzione',succeeded:'Completato',failed:'Non riuscito',cancelled:'Annullato'} as Record<string,string>)[job.state]??job.state} · {new Date(job.createdAt).toLocaleString('it-IT')}</p>{job.state === 'queued' && <button disabled={busy} onClick={() => void action(async () => { await api(`/devices/${status.id}/updates/${job.id}`,'DELETE'); setJobs(await api<UpdateJob[]>(`/devices/${status.id}/updates`)); })}>Annulla</button>}{job.result && (() => { const r = JSON.parse(job.result) as {Succeeded:boolean;RebootRequired:boolean;Error:string|null;Updates:UpdateItem[]}; return <><p>{r.Succeeded ? 'Completato' : 'Operazione non riuscita'}{r.RebootRequired ? ' · Riavvio richiesto' : ''}</p>{r.Error&&<details className="advanced"><summary>Dettagli errore</summary><code>{r.Error}</code></details>}{job.kind === 'scan' && r.Succeeded && r.Updates.map(u => <label key={u.UpdateId}><input style={{width:'auto'}} type="checkbox" checked={selectedUpdates.some(x => x.UpdateId === u.UpdateId)} onChange={e => setSelectedUpdates(e.target.checked ? [...selectedUpdates.filter(x => x.UpdateId !== u.UpdateId),u] : selectedUpdates.filter(x => x.UpdateId !== u.UpdateId))}/>{u.Title} · {u.Kb.join(', ')}</label>)}</>; })()}</article>)}
          {selectedUpdates.length > 0 && <button disabled={busy || status.revoked} onClick={() => void action(async () => { await api(`/devices/${status.id}/updates/install`,'POST',{requestId:crypto.randomUUID(),updates:selectedUpdates.map(u => ({updateId:u.UpdateId,revision:u.Revision}))}); setSelectedUpdates([]); setJobs(await api<UpdateJob[]>(`/devices/${status.id}/updates`)); })}>Installa i {selectedUpdates.length} aggiornamenti selezionati (accetta EULA)</button>}
        </section></details>}
        {status.inventory && <details className="advanced"><summary><Icon name="files"/>Inventario e software</summary><h2>Inventario</h2><p>{status.inventory.operatingSystem} · {status.inventory.architecture}</p><p>{status.inventory.manufacturer} {status.inventory.model} · {status.inventory.processor} · {status.inventory.logicalProcessors} CPU logiche</p>{status.inventory.warnings.map((w,i) => <p key={i}>{w}</p>)}<div className="table-wrap"><table><thead><tr><th>Software</th><th>Versione</th><th>Produttore</th></tr></thead><tbody>{status.inventory.software.map((x,i) => <tr key={i}><td>{x.name}</td><td>{x.version}</td><td>{x.publisher}</td></tr>)}</tbody></table></div></details>}
      </section>}
      {admin && <div className="device-onboarding"><AgentDownload/><details className="panel enrollment" open><summary><Icon name="plus"/>Registra un dispositivo</summary><form className="settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); setEnrollment(''); void action(async () => { const issued = await api<{token:string}>('/admin/enrollment-tokens','POST',{groupId:f.get('group') || null, expiresInMinutes:30}); setEnrollment(issued.token); }); }}><h2>Registra un dispositivo</h2><label>Gruppo<select aria-label="Gruppo" name="group"><option value="">Senza gruppo</option>{groups.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label><button disabled={busy}>Genera token monouso (30 minuti)</button>{enrollment && <><p>Copia questo token nel prompt dell’installer Windows. È monouso e viene mostrato solo qui.</p><textarea aria-label="Token enrollment" readOnly value={enrollment} rows={3} style={{width:'100%'}}/><button type="button" onClick={() => setEnrollment('')}>Nascondi token</button></>}</form></details></div>}
    </>}
    {tab === 'branding' && admin && <form className="panel settings" onSubmit={e => { e.preventDefault(); void action(async () => { const saved = await api<Branding>('/branding','PUT',draft); setBranding(saved); setMessage('Personalizzazione salvata'); }); }}>
      <h2>Identità della dashboard</h2><label>Nome<input required maxLength={100} value={draft.name} onChange={e => setDraft({...draft,name:e.target.value})}/></label>
      <label>Logo (URL HTTPS)<input type="url" maxLength={2048} value={draft.logoUrl} onChange={e => setDraft({...draft,logoUrl:e.target.value})}/></label>
      <label>Colore principale<input type="color" value={draft.primaryColor} onChange={e => setDraft({...draft,primaryColor:e.target.value})}/></label>
      <label>Tema<select aria-label="Tema" value={draft.theme} onChange={e => setDraft({...draft,theme:e.target.value as Branding['theme']})}><option value="dark">Scuro</option><option value="light">Chiaro</option></select></label>
      <button disabled={busy}>Salva personalizzazione</button>
    </form>}
    {tab === 'security' && <><form className="panel settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); const form = e.currentTarget; void action(async () => { await api('/auth/password','POST',{currentPassword:f.get('current'),newPassword:f.get('next'),code:f.get('code') || undefined}); form.reset(); setMessage('Password aggiornata'); }); }}><h2>Cambia password</h2><label>Password attuale<input type="password" name="current" autoComplete="current-password" required/></label><label>Nuova password<input type="password" name="next" autoComplete="new-password" minLength={14} maxLength={1024} required/></label>{user.twoFactorEnabled && <label>Codice 2FA<input name="code" autoComplete="one-time-code" required inputMode="numeric"/></label>}<button disabled={busy}>Aggiorna password</button></form>
      {user.twoFactorAvailable ? <form className="panel settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void action(async () => { if (user.twoFactorEnabled) { await api('/auth/totp/disable','POST',{password:f.get('password'),code:f.get('code')}); setRecoveryCodes([]); setTotpSetup(null); } else if (totpSetup) { const result = await api<{recoveryCodes:string[]}>('/auth/totp/enable','POST',{password:f.get('password'),code:f.get('code')}); setRecoveryCodes(result.recoveryCodes); setTotpSetup(null); } else setTotpSetup(await api('/auth/totp/setup','POST',{password:f.get('password')})); setUser(await api<User>('/auth/me')); }); }}><h2>Autenticazione a due fattori</h2><p>{user.twoFactorEnabled ? 'Attiva' : 'Non attiva'}</p><label>Password attuale<input type="password" name="password" required autoComplete="current-password"/></label>{totpSetup && <><p>Inserisci questa chiave nell’app Authenticator (TOTP, 6 cifre, 30 secondi). Non condividerla.</p><code>{totpSetup.sharedKey}</code></>}{(totpSetup || user.twoFactorEnabled) && <label>Codice Authenticator<input name="code" inputMode="numeric" autoComplete="one-time-code" required maxLength={20}/></label>}<button disabled={busy}>{user.twoFactorEnabled ? 'Disattiva 2FA' : totpSetup ? 'Verifica e attiva' : 'Configura Authenticator'}</button></form> : <section className="panel"><h2>Autenticazione a due fattori</h2><p>Disponibile nel codice, disattivata nella configurazione del server.</p></section>}
      {recoveryCodes.length > 0 && <section className="panel"><h2>Codici di recupero</h2><p>Salvali in un posto riservato. Ogni codice può essere usato una sola volta.</p><pre>{recoveryCodes.join('\n')}</pre><button onClick={() => setRecoveryCodes([])}>Ho salvato i codici</button></section>}
    </>}
    {tab === 'users' && admin && <AccessManagement currentUserId={user.id}/>}
    {tab === 'audit' && admin && <section className="panel"><button disabled={busy} onClick={() => void action(async () => setAudit(await api<Audit[]>('/admin/audit')))}>Aggiorna</button><h2>Ultimi 100 eventi</h2><div className="table-wrap"><table><thead><tr><th>Data</th><th>Azione</th><th>Oggetto</th><th>Esito</th></tr></thead><tbody>{audit.map(a => <tr key={a.id}><td>{new Date(a.at).toLocaleString('it-IT')}</td><td>{a.action}</td><td>{a.target}</td><td>{a.outcome}</td></tr>)}</tbody></table></div></section>}
  </main></div>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
