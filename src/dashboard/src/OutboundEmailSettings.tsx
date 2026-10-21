import { useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import './outbound-email.css';

type EmailSettings = {
  configured: boolean; enabled: boolean; host: string; port: 465 | 587; fromAddress: string;
  fromName: string; dashboardUrl: string; username: string; passwordConfigured: boolean;
  source: 'environment' | 'dashboard';
};

const empty: EmailSettings = {
  configured: false, enabled: false, host: '', port: 587, fromAddress: '', fromName: 'OmniDesk',
  dashboardUrl: window.location.origin, username: '', passwordConfigured: false, source: 'dashboard',
};

export function OutboundEmailSettings({ busy, onMessage, onError }: {
  busy: boolean; onMessage: (message: string) => void; onError: (message: string) => void;
}) {
  const [settings, setSettings] = useState<EmailSettings>(empty);
  const [loading, setLoading] = useState(true);
  const [password, setPassword] = useState('');
  const [clearPassword, setClearPassword] = useState(false);
  const [testRecipient, setTestRecipient] = useState('');
  const [sendingTest, setSendingTest] = useState(false);

  async function load() { setSettings(await api<EmailSettings>('/admin/outbound-email')); }
  useEffect(() => { void load().catch(e => onError(e instanceof Error ? e.message : 'Impostazioni email non disponibili')).finally(() => setLoading(false)); }, []);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      await api('/admin/outbound-email', 'PUT', {
        enabled: settings.enabled, host: settings.host, port: settings.port,
        fromAddress: settings.fromAddress, fromName: settings.fromName,
        dashboardUrl: settings.dashboardUrl, username: settings.username,
        password: password || null, clearPassword,
      });
      setPassword(''); setClearPassword(false); await load(); onMessage('Configurazione SMTP salvata');
    } catch (e) { onError(e instanceof Error ? e.message : 'Salvataggio SMTP non riuscito'); }
  }

  async function sendTest() {
    setSendingTest(true);
    try {
      await api('/admin/outbound-email/test', 'POST', { recipient: testRecipient });
      onMessage(`Email di prova inviata a ${testRecipient}`);
    } catch (e) { onError(e instanceof Error ? e.message : 'Invio email di prova non riuscito'); }
    finally { setSendingTest(false); }
  }

  const update = <K extends keyof EmailSettings>(key: K, value: EmailSettings[K]) => setSettings(current => ({ ...current, [key]: value }));
  return <div className="outbound-email-settings">
    <section className="panel settings">
      <div className="section-heading"><div><span className="eyebrow">POSTA IN USCITA</span><h2>Server SMTP</h2></div><span className={`status-pill ${settings.configured ? 'status-online' : 'status-offline'}`}>{settings.configured ? 'Configurato' : 'Non configurato'}</span></div>
      <p>Usato per codici email già abilitati, notifiche ai ticket e dispositivi offline. La password viene cifrata sul server; non viene mai restituita a questo pannello.</p>
      {settings.source === 'environment' && <p className="notice">È attiva una configurazione fornita dal server. Salvando qui, le nuove impostazioni del pannello avranno la precedenza.</p>}
      {loading ? <p>Caricamento impostazioni…</p> : <form className="settings-form" onSubmit={save}>
        <label className="check"><input type="checkbox" checked={settings.enabled} onChange={e => update('enabled', e.target.checked)}/> Abilita invio email</label>
        <div className="email-settings-grid">
          <label>Host SMTP<input required={settings.enabled} autoComplete="off" maxLength={253} value={settings.host} onChange={e => update('host', e.target.value)} placeholder="smtp.example.org"/></label>
          <label>Porta e sicurezza<select value={settings.port} onChange={e => update('port', Number(e.target.value) as 465 | 587)}><option value={587}>587 · STARTTLS</option><option value={465}>465 · TLS implicito</option></select></label>
          <label>Indirizzo mittente<input required={settings.enabled} type="email" autoComplete="off" maxLength={320} value={settings.fromAddress} onChange={e => update('fromAddress', e.target.value)} placeholder="assistenza@example.org"/></label>
          <label>Nome mittente<input required={settings.enabled} autoComplete="organization" maxLength={128} value={settings.fromName} onChange={e => update('fromName', e.target.value)}/></label>
          <label>Username SMTP<input required={settings.enabled} autoComplete="username" maxLength={320} value={settings.username} onChange={e => update('username', e.target.value)}/></label>
          <label>Password SMTP<input type="password" autoComplete="new-password" maxLength={512} value={password} onChange={e => { setPassword(e.target.value); setClearPassword(false); }} placeholder={settings.passwordConfigured ? 'Salvata; lascia vuoto per mantenerla' : 'Inserisci la password'}/></label>
          <label className="email-dashboard-url">URL dashboard<input required type="url" autoComplete="url" maxLength={2048} value={settings.dashboardUrl} onChange={e => update('dashboardUrl', e.target.value)} placeholder="https://omnidesk.example.org"/><small>Link inserito nelle notifiche; deve usare HTTPS.</small></label>
        </div>
        {settings.passwordConfigured && <label className="check"><input type="checkbox" checked={clearPassword} onChange={e => { setClearPassword(e.target.checked); if (e.target.checked) setPassword(''); }}/> Rimuovi la password salvata</label>}
        <div className="settings-actions"><button disabled={busy || loading}>Salva SMTP</button><small>{settings.passwordConfigured ? 'Password già configurata.' : 'Salva una password per attivare SMTP autenticato.'}</small></div>
      </form>}
    </section>
    <section className="panel settings">
      <h2>Verifica invio</h2><p>L’invio parte solo dopo aver premuto il pulsante. Riceverai una email di prova all’indirizzo indicato.</p>
      <form className="smtp-test-form" onSubmit={e => { e.preventDefault(); void sendTest(); }}><label>Destinatario di prova<input type="email" required maxLength={320} value={testRecipient} onChange={e => setTestRecipient(e.target.value)} placeholder="tu@example.org"/></label><button type="submit" className="secondary" disabled={busy || sendingTest || !settings.configured || !testRecipient}>Invia email di prova</button></form>
      <p className="muted">La 2FA email non viene attivata da questa schermata: resta separata e disabilitata finché un amministratore non la abilita con una modifica dedicata.</p>
    </section>
  </div>;
}
