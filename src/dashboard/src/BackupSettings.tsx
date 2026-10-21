import { useEffect, useState } from 'react';
import { api } from './api';

type BackupSettingsModel = {
  configured: boolean;
  enabled: boolean;
  server: string;
  share: string;
  username: string;
  passwordConfigured: boolean;
  domain: string;
  ageRecipient: string;
  scheduleTime: string;
  timeZoneId: string;
  keepCount: number;
  lastAttemptAt: string | null;
  lastSuccessAt: string | null;
  lastStatus: 'never' | 'running' | 'success' | 'failed';
  lastErrorCode: string | null;
};

const defaults: BackupSettingsModel = {
  configured: false, enabled: false, server: '', share: '', username: '', passwordConfigured: false,
  domain: '', ageRecipient: '', scheduleTime: '03:00', timeZoneId: 'Europe/Rome', keepCount: 7,
  lastAttemptAt: null, lastSuccessAt: null, lastStatus: 'never', lastErrorCode: null,
};

export function BackupSettings({ busy, onMessage, onError }: { busy: boolean; onMessage: (message: string) => void; onError: (message: string) => void }) {
  const [settings, setSettings] = useState(defaults);
  const [password, setPassword] = useState('');
  const [clearPassword, setClearPassword] = useState(false);
  const [loading, setLoading] = useState(true);

  async function load() {
    setSettings(await api<BackupSettingsModel>('/admin/backup-nas'));
  }
  useEffect(() => { void load().catch(e => onError(e instanceof Error ? e.message : 'Impostazioni backup non disponibili')).finally(() => setLoading(false)); }, []);

  return <section className="backup-settings-grid">
    <form key={`${settings.server}|${settings.share}|${settings.username}|${settings.ageRecipient}|${settings.scheduleTime}|${settings.timeZoneId}|${settings.keepCount}|${settings.enabled}`} className="panel settings backup-settings-form" onSubmit={event => {
      event.preventDefault();
      const form = new FormData(event.currentTarget);
      void (async () => {
        try {
          const next = {
            enabled: form.get('enabled') === 'on', server: form.get('server'), share: form.get('share'),
            username: form.get('username'), password: password || null, clearPassword,
            domain: form.get('domain'), ageRecipient: form.get('ageRecipient'),
            scheduleTime: form.get('scheduleTime'), timeZoneId: form.get('timeZoneId'),
            keepCount: Number(form.get('keepCount')),
          };
          await api('/admin/backup-nas', 'PUT', next);
          setPassword(''); setClearPassword(false); await load(); onMessage('Impostazioni backup salvate');
        } catch (e) { onError(e instanceof Error ? e.message : 'Salvataggio backup non riuscito'); }
      })();
    }}>
      <div><span className="eyebrow">ARCHIVIO SERVER</span><h2>Backup sul NAS</h2>
        <p>Invia una copia cifrata del server su Samba. La password viene cifrata da OmniDesk e non è mai mostrata dopo il salvataggio.</p></div>
      {loading ? <p>Caricamento impostazioni…</p> : <>
        <label className="check"><input type="checkbox" name="enabled" defaultChecked={settings.enabled}/> Attiva backup automatico</label>
        <div className="settings-fields-grid">
          <label>Indirizzo NAS<input name="server" required maxLength={253} placeholder="192.168.1.20" defaultValue={settings.server}/></label>
          <label>Share e cartella<input name="share" required maxLength={255} placeholder="Backup/OmniDesk" defaultValue={settings.share}/></label>
          <label>Utente Samba<input name="username" required maxLength={128} autoComplete="username" defaultValue={settings.username}/></label>
          <label>Dominio o workgroup<input name="domain" maxLength={128} autoComplete="organization" defaultValue={settings.domain}/></label>
          <label>{settings.passwordConfigured ? 'Nuova password Samba (vuoto per mantenere quella salvata)' : 'Password Samba'}<input name="password" type="password" autoComplete="new-password" maxLength={512} value={password} onChange={e => setPassword(e.target.value)}/></label>
          {settings.passwordConfigured && <label className="check"><input type="checkbox" checked={clearPassword} onChange={e => setClearPassword(e.target.checked)}/> Rimuovi password salvata</label>}
          <label>Recipient pubblico age<input name="ageRecipient" required maxLength={100} autoComplete="off" placeholder="age1…" defaultValue={settings.ageRecipient}/></label>
          <label>Orario giornaliero<input name="scheduleTime" type="time" required defaultValue={settings.scheduleTime}/></label>
          <label>Fuso orario<input name="timeZoneId" required maxLength={100} defaultValue={settings.timeZoneId}/></label>
          <label>Copie da conservare<input name="keepCount" type="number" min={1} max={365} required defaultValue={settings.keepCount}/></label>
        </div>
        <p className="muted">La chiave privata age non va inserita qui: conservala offline. Senza quella chiave non sarà possibile decifrare i backup.</p>
        <button disabled={busy}>Salva impostazioni backup</button>
      </>}
    </form>
    <section className="panel backup-status-panel"><span className="eyebrow">ULTIMA ESECUZIONE</span><h2>{settings.lastStatus === 'success' ? 'Completata' : settings.lastStatus === 'failed' ? 'Non riuscita' : settings.lastStatus === 'running' ? 'In corso' : 'Mai eseguita'}</h2>
      <p>Ultimo tentativo: {settings.lastAttemptAt ? new Date(settings.lastAttemptAt).toLocaleString('it-IT') : '—'}</p>
      <p>Ultimo upload riuscito: {settings.lastSuccessAt ? new Date(settings.lastSuccessAt).toLocaleString('it-IT') : '—'}</p>
      {settings.lastErrorCode && <p role="status">Codice errore: {settings.lastErrorCode}</p>}
      <small>Il sistema controlla la pianificazione ogni cinque minuti. Il primo upload avverrà all’orario impostato se il backup automatico è attivo.</small>
    </section>
  </section>;
}
