import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, refreshCsrf } from './api';
import './style.css';

type User = { id: string; email: string; globalPermissions: string[]; twoFactorAvailable: boolean };
type Branding = { name: string; logoUrl: string; primaryColor: string; theme: 'light' | 'dark' };
type Device = { id: string; name: string; groupId: string | null };
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
  const [tab, setTab] = useState('overview');
  const admin = user?.globalPermissions.includes('administration') ?? false;

  async function load() {
    const me = await api<User>('/auth/me');
    setUser(me);
    const [brand, list] = await Promise.all([api<Branding>('/branding'), api<Device[]>('/devices')]);
    setBranding(brand); setDraft(brand); setDevices(list);
    if (me.globalPermissions.includes('administration')) setAudit(await api<Audit[]>('/admin/audit'));
    await refreshCsrf();
  }
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
    void action(async () => { await api('/auth/login', 'POST', { email: form.get('email'), password: form.get('password') }); await load(); });
  }}><span className="mark">O</span><h1>OmniDesk</h1><p>Accedi alla console IT</p>
    <label>Email<input name="email" type="email" autoComplete="username" required maxLength={254}/></label>
    <label>Password<input name="password" type="password" autoComplete="current-password" required maxLength={1024}/></label>
    {error && <p role="alert" className="error">{error}</p>}<button disabled={busy}>{busy ? 'Accesso…' : 'Accedi'}</button>
    <small>Account creati dall’amministratore. 2FA prevista, non attiva nella milestone 1.</small>
  </form></main>;
  return <div className="shell"><aside>
    <div className="brand">{branding.logoUrl ? <img src={branding.logoUrl} alt="Logo" referrerPolicy="no-referrer"/> : <span className="mark">O</span>}<strong>{branding.name}</strong></div>
    <small>CONSOLE IT</small>
    <nav aria-label="Navigazione">{[['overview','Panoramica'],['devices','Dispositivi'],...(admin ? [['branding','Personalizzazione'],['users','Utenti'],['audit','Audit']] : [])].map(([id,label]) => <button key={id} className={tab === id ? 'active' : ''} onClick={() => { setTab(id); setError(''); setMessage(''); }}>{label}</button>)}</nav>
    <div className="account"><small>{user.email}</small><button disabled={busy} onClick={() => void action(async () => { await api('/auth/logout', 'POST'); setUser(null); setBranding(defaults); setAudit([]); setDevices([]); setTab('overview'); await refreshCsrf(); })}>Esci</button></div>
  </aside><main className="content"><header><div><small>OMNIDESK / MILESTONE 1</small><h1>{{overview:'Panoramica',devices:'Dispositivi',branding:'Personalizzazione',users:'Utenti',audit:'Audit amministrativo'}[tab]}</h1></div><span className="badge">Piano di gestione</span></header>
    {error && <p role="alert" className="error">{error}</p>}{message && <p role="status">{message}</p>}
    {tab === 'overview' && <><div className="stats"><article className="panel"><small>Dispositivi visibili</small><h2>{devices.length}</h2><p>Autorizzati per il tuo account</p></article><article className="panel"><small>Permessi globali</small><h2>{user.globalPermissions.length}</h2><p>{user.globalPermissions.join(', ') || 'Solo concessioni locali'}</p></article><article className="panel"><small>Autenticazione</small><h2>Identity</h2><p>2FA predisposta · non attiva</p></article></div><section className="panel"><h2>La base della tua console IT</h2><p>Autenticazione, autorizzazioni e configurazione del marchio disponibili.</p><p>Monitoraggio, inventario, controllo remoto e Windows Update saranno disponibili nelle prossime milestone.</p></section></>}
    {tab === 'devices' && <section className="panel"><h2>Parco macchine</h2>{devices.length === 0 ? <p>Nessun dispositivo visibile. L’enrollment dell’agente Windows è previsto nella milestone 2.</p> : <table><thead><tr><th>Nome</th><th>Identificativo</th></tr></thead><tbody>{devices.map(d => <tr key={d.id}><td>{d.name}</td><td>{d.id}</td></tr>)}</tbody></table>}</section>}
    {tab === 'branding' && admin && <form className="panel settings" onSubmit={e => { e.preventDefault(); void action(async () => { const saved = await api<Branding>('/branding','PUT',draft); setBranding(saved); setMessage('Personalizzazione salvata'); }); }}>
      <h2>Identità della dashboard</h2><label>Nome<input required maxLength={100} value={draft.name} onChange={e => setDraft({...draft,name:e.target.value})}/></label>
      <label>Logo (URL HTTPS)<input type="url" maxLength={2048} value={draft.logoUrl} onChange={e => setDraft({...draft,logoUrl:e.target.value})}/></label>
      <label>Colore principale<input type="color" value={draft.primaryColor} onChange={e => setDraft({...draft,primaryColor:e.target.value})}/></label>
      <label>Tema<select value={draft.theme} onChange={e => setDraft({...draft,theme:e.target.value as Branding['theme']})}><option value="dark">Scuro</option><option value="light">Chiaro</option></select></label>
      <button disabled={busy}>Salva personalizzazione</button>
    </form>}
    {tab === 'users' && admin && <form className="panel settings" onSubmit={e => { e.preventDefault(); const form = e.currentTarget; const values = new FormData(form); void action(async () => { await api('/admin/users','POST',{email:values.get('email'),password:values.get('password'),role:values.get('role') || null}); form.reset(); setMessage('Utente creato'); }); }}>
      <h2>Crea un account</h2><label>Email<input type="email" name="email" required maxLength={254}/></label><label>Password iniziale<input name="password" type="password" required minLength={14} maxLength={1024} autoComplete="new-password"/></label>
      <p>Minimo 14 caratteri, maiuscole, minuscole, numero e simbolo. Consegnare tramite un canale riservato.</p><label>Ruolo globale<select name="role"><option value="">Nessuno (solo concessioni locali)</option><option>Viewer</option><option>Operator</option><option>Administrator</option></select></label><button disabled={busy}>Crea utente</button><small>Concessioni per gruppo/dispositivo gestibili tramite API amministrativa.</small>
    </form>}
    {tab === 'audit' && admin && <section className="panel"><button disabled={busy} onClick={() => void action(async () => setAudit(await api<Audit[]>('/admin/audit')))}>Aggiorna</button><h2>Ultimi 100 eventi</h2><div className="table-wrap"><table><thead><tr><th>Data</th><th>Azione</th><th>Oggetto</th><th>Esito</th></tr></thead><tbody>{audit.map(a => <tr key={a.id}><td>{new Date(a.at).toLocaleString('it-IT')}</td><td>{a.action}</td><td>{a.target}</td><td>{a.outcome}</td></tr>)}</tbody></table></div></section>}
  </main></div>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
