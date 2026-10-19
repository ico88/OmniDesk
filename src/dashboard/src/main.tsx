import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, refreshCsrf } from './api';
import './style.css';
import { AccessManagement } from './AccessManagement';
import { DeviceManagement } from './DeviceManagement';
import { SupportDesk } from './SupportDesk';
import { RemoteSessions } from './RemoteSessions';
import { AgentDownload } from './AgentDownload';

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
  const admin = user?.globalPermissions.includes('administration') ?? false;

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
  }}><span className="mark">O</span><h1>OmniDesk</h1><p>Accedi alla console IT</p>
    <label>Email<input name="email" type="email" autoComplete="username" required maxLength={254}/></label>
    <label>Password<input name="password" type="password" autoComplete="current-password" required maxLength={1024}/></label>
    <label>Codice 2FA (se attiva)<input name="code" inputMode="numeric" autoComplete="one-time-code" maxLength={20}/></label><label>Oppure codice di recupero<input name="recoveryCode" autoComplete="off" maxLength={100}/></label>
    {error && <p role="alert" className="error">{error}</p>}<button disabled={busy}>{busy ? 'Accesso…' : 'Accedi'}</button>
    <small>Account creati dall’amministratore. 2FA opzionale, inizialmente disattivata.</small>
  </form></main>;
  return <div className="shell"><aside>
    <div className="brand">{branding.logoUrl ? <img src={branding.logoUrl} alt="Logo" referrerPolicy="no-referrer"/> : <span className="mark">O</span>}<strong>{branding.name}</strong></div>
    <small>CONSOLE IT</small>
    <nav aria-label="Navigazione">{[['overview','Panoramica'],['devices','Dispositivi'],['security','Account e sicurezza'],['support','Assistenza'],...(admin ? [['branding','Personalizzazione'],['users','Utenti'],['audit','Audit']] : [])].map(([id,label]) => <button key={id} className={tab === id ? 'active' : ''} onClick={() => { setTab(id); setTotpSetup(null); setRecoveryCodes([]); setJobs([]); setSelectedUpdates([]); setRemote(null); setEnrollment(''); setStatus(null); setError(''); setMessage(''); }}>{label}</button>)}</nav>
    <div className="account"><small>{user.email}</small><button disabled={busy} onClick={() => void action(async () => { await api('/auth/logout', 'POST'); setUser(null); setTotpSetup(null); setRecoveryCodes([]); setRemote(null); setEnrollment(''); setStatus(null); setGroups([]); setBranding(defaults); setAudit([]); setDevices([]); setTab('overview'); await refreshCsrf(); })}>Esci</button></div>
  </aside><main className="content"><header><div><small>OMNIDESK / CONSOLE IT</small><h1>{{overview:'Panoramica',devices:'Dispositivi',branding:'Personalizzazione',users:'Utenti',audit:'Audit amministrativo',security:'Account e sicurezza',support:'Assistenza'}[tab]}</h1></div><span className="badge">Piano di gestione</span></header>
    {error && <p role="alert" className="error">{error}</p>}{message && <p role="status">{message}</p>}
    {tab === 'support' && <SupportDesk/>}
    {tab === 'overview' && <><div className="stats">{widgets.map(widget => <article key={widget} className="panel">{widget === 'devices' ? <><small>Dispositivi visibili</small><h2>{devices.length}</h2><p>Autorizzati per il tuo account</p></> : widget === 'permissions' ? <><small>Permessi globali</small><h2>{user.globalPermissions.length}</h2><p>{user.globalPermissions.join(', ') || 'Solo concessioni locali'}</p></> : <><small>Autenticazione</small><h2>Identity</h2><p>{user.twoFactorEnabled ? '2FA attiva' : '2FA non attiva'}</p></>}</article>)}</div><details className="panel"><summary>Personalizza i tuoi riquadri</summary>{[['devices','Dispositivi'],['permissions','Permessi'],['authentication','Autenticazione']].map(([id,label]) => <label key={id}><input style={{width:'auto'}} type="checkbox" checked={widgets.includes(id)} disabled={busy} onChange={e => { const previous = widgets; const next = e.target.checked ? [...widgets,id] : widgets.filter(x => x !== id); setWidgets(next); void action(async () => { try { await api('/dashboard/layout','PUT',{widgets:next}); } catch (error) { setWidgets(previous); throw error; } }); }}/>{label}</label>)}<button disabled={busy || widgets.length < 2} onClick={() => void action(async () => { const next=[...widgets.slice(1),widgets[0]]; await api('/dashboard/layout','PUT',{widgets:next}); setWidgets(next); })}>Ruota ordine riquadri</button></details><section className="panel"><h2>La base della tua console IT</h2><p>Autenticazione, autorizzazioni e configurazione del marchio disponibili.</p><p>Enrollment, snapshot di monitoraggio e inventario disponibili con il nuovo agente Windows. Controllo remoto e Windows Update sono nelle milestone successive.</p></section></>}
    {tab === 'devices' && <>{admin && <AgentDownload/>}<section className="panel"><h2>Parco macchine</h2><button disabled={busy} onClick={() => void action(async () => setDevices(await api<Device[]>('/devices')))}>Aggiorna</button>{devices.length === 0 ? <p>Nessun dispositivo visibile. Registra un agente Windows con un token di enrollment.</p> : <table><thead><tr><th>Nome</th><th>Identificativo</th><th>Stato</th></tr></thead><tbody>{devices.map(d => <tr key={d.id}><td>{d.name}</td><td>{d.id}</td><td><button disabled={busy} onClick={() => void action(async () => { setStatus(await api<DeviceStatus>(`/devices/${d.id}/status`)); setJobs([]); setSelectedUpdates([]); })}>Apri</button></td></tr>)}</tbody></table>}</section>
      {status && <section className="panel"><h2>{status.name}</h2><p>{status.revoked ? 'Identità revocata' : status.online ? 'Heartbeat recente' : 'Offline / nessun heartbeat'} · Ultimo contatto: {status.lastSeen ? new Date(status.lastSeen).toLocaleString('it-IT') : 'Mai'}</p>
        {admin && <DeviceManagement key={`${status.id}:${status.name}:${status.groupId}:${status.revoked}`} device={status} groups={groups} onChanged={async message => {
          const id = status.id;
          const [updated, list] = await Promise.all([api<DeviceStatus>(`/devices/${id}/status`), api<Device[]>('/devices')]);
          setStatus(current => current?.id === id ? updated : current); setDevices(list); setRemote(null); setJobs([]); setSelectedUpdates([]); setMessage(message);
        }}/>}
        <div>{[['remote.control','Controllo remoto'],['terminal','Terminale'],['files','File']].filter(([cap]) => status.capabilities.includes(cap)).map(([cap,label]) => <button key={cap} disabled={busy || status.revoked} onClick={() => void action(async () => { setRemote(null); const created = await api<{id:string;url:string;expiresAt:string}>(`/devices/${status.id}/remote-sessions`,'POST',{capability:cap}); setRemote({...created,deviceId:status.id}); })}>{label}</button>)}</div>
        {remote && remote.deviceId === status.id && <p><a href={remote.url} target="_blank" rel="noreferrer noopener">Apri sessione autorizzata</a> · Scade alle {new Date(remote.expiresAt).toLocaleTimeString('it-IT')} <button disabled={busy} onClick={() => void action(async () => { const result = await api<{state:string} | undefined>(`/devices/${remote.deviceId}/remote-sessions/${remote.id}`,'DELETE'); setMessage(result?.state === 'revocation_pending' ? 'Revoca richiesta: il server ritenta la chiusura. Il motore non ha ancora confermato.' : 'Revoca confermata dal motore.'); setRemote(null); })}>Revoca sessione</button></p>}
        <RemoteSessions key={status.id} deviceId={status.id} admin={admin}/>
        {status.metrics && <><p>CPU: {status.metrics.cpuPercent === null ? 'Non disponibile' : status.metrics.cpuPercent.toFixed(1) + '%'} · RAM disponibile: {(status.metrics.availableMemoryBytes / 1073741824).toFixed(1)} / {(status.metrics.totalMemoryBytes / 1073741824).toFixed(1)} GiB · Uptime: {Math.floor(status.metrics.uptimeSeconds / 3600)} ore</p><p>Rete cumulativa: ricevuti {status.metrics.networkReceivedBytes.toLocaleString()} byte · inviati {status.metrics.networkSentBytes.toLocaleString()} byte</p>{status.metrics.disks.map(d => <p key={d.name}>{d.name} · {(d.freeBytes / 1073741824).toFixed(1)} / {(d.totalBytes / 1073741824).toFixed(1)} GiB liberi</p>)}</>}
        {status.capabilities.includes('updates') && <section><h2>Windows Update</h2><button disabled={busy || status.revoked} onClick={() => void action(async () => { await api(`/devices/${status.id}/updates/scan`,'POST',{requestId:crypto.randomUUID()}); setJobs(await api<UpdateJob[]>(`/devices/${status.id}/updates`)); })}>Avvia scansione</button> <button disabled={busy} onClick={() => void action(async () => { setJobs(await api<UpdateJob[]>(`/devices/${status.id}/updates`)); setSelectedUpdates([]); })}>Aggiorna lavori</button><p>I lavori vengono eseguiti dall’agente. Nessun riavvio automatico.</p>
          {jobs.map(job => <article key={job.id}><p>{job.kind} · {job.state} · {new Date(job.createdAt).toLocaleString('it-IT')}</p>{job.state === 'queued' && <button disabled={busy} onClick={() => void action(async () => { await api(`/devices/${status.id}/updates/${job.id}`,'DELETE'); setJobs(await api<UpdateJob[]>(`/devices/${status.id}/updates`)); })}>Annulla</button>}{job.result && (() => { const r = JSON.parse(job.result) as {Succeeded:boolean;RebootRequired:boolean;Error:string|null;Updates:UpdateItem[]}; return <><p>{r.Succeeded ? 'Completato' : 'Non riuscito'}{r.RebootRequired ? ' · Riavvio richiesto' : ''} {r.Error}</p>{job.kind === 'scan' && r.Succeeded && r.Updates.map(u => <label key={u.UpdateId}><input style={{width:'auto'}} type="checkbox" checked={selectedUpdates.some(x => x.UpdateId === u.UpdateId)} onChange={e => setSelectedUpdates(e.target.checked ? [...selectedUpdates.filter(x => x.UpdateId !== u.UpdateId),u] : selectedUpdates.filter(x => x.UpdateId !== u.UpdateId))}/>{u.Title} · {u.Kb.join(', ')}</label>)}</>; })()}</article>)}
          {selectedUpdates.length > 0 && <button disabled={busy || status.revoked} onClick={() => void action(async () => { await api(`/devices/${status.id}/updates/install`,'POST',{requestId:crypto.randomUUID(),updates:selectedUpdates.map(u => ({updateId:u.UpdateId,revision:u.Revision}))}); setSelectedUpdates([]); setJobs(await api<UpdateJob[]>(`/devices/${status.id}/updates`)); })}>Installa i {selectedUpdates.length} aggiornamenti selezionati (accetta EULA)</button>}
        </section>}
        {status.inventory && <><h2>Inventario</h2><p>{status.inventory.operatingSystem} · {status.inventory.architecture}</p><p>{status.inventory.manufacturer} {status.inventory.model} · {status.inventory.processor} · {status.inventory.logicalProcessors} CPU logiche</p>{status.inventory.warnings.map((w,i) => <p key={i}>{w}</p>)}<div className="table-wrap"><table><thead><tr><th>Software</th><th>Versione</th><th>Produttore</th></tr></thead><tbody>{status.inventory.software.map((x,i) => <tr key={i}><td>{x.name}</td><td>{x.version}</td><td>{x.publisher}</td></tr>)}</tbody></table></div></>}
      </section>}
      {admin && <form className="panel settings" onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); setEnrollment(''); void action(async () => { const issued = await api<{token:string}>('/admin/enrollment-tokens','POST',{groupId:f.get('group') || null, expiresInMinutes:30}); setEnrollment(issued.token); }); }}><h2>Registra un dispositivo</h2><label>Gruppo<select aria-label="Gruppo" name="group"><option value="">Senza gruppo</option>{groups.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label><button disabled={busy}>Genera token monouso (30 minuti)</button>{enrollment && <><p>Salva il token in un file riservato per il comando enroll dell’agente. Il token viene mostrato solo qui.</p><textarea aria-label="Token enrollment" readOnly value={enrollment} rows={3} style={{width:'100%'}}/><button type="button" onClick={() => setEnrollment('')}>Nascondi token</button></>}</form>}
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
