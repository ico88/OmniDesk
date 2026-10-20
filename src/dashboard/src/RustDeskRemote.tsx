import { useEffect, useState } from 'react';
import { Clipboard, ExternalLink, RefreshCw, ShieldCheck } from 'lucide-react';
import { api } from './api';

type RustDeskConfig = {
  configured: boolean;
  mapped: boolean;
  peerId: string | null;
  idServer: string | null;
  relayServer: string | null;
  apiServer: string | null;
  publicKey: string | null;
};

export function RustDeskRemote({ deviceId, deviceName, admin, allowed, revoked, onMessage }: {
  deviceId: string;
  deviceName: string;
  admin: boolean;
  allowed: boolean;
  revoked: boolean;
  onMessage: (message: string) => void;
}) {
  const [config, setConfig] = useState<RustDeskConfig | null>(null);
  const [peerId, setPeerId] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');

  async function load() {
    const result = await api<RustDeskConfig>(`/devices/${deviceId}/rustdesk`);
    setConfig(result);
    setPeerId(result.peerId ?? '');
  }

  useEffect(() => {
    let active = true;
    void api<RustDeskConfig>(`/devices/${deviceId}/rustdesk`).then(result => {
      if (active) { setConfig(result); setPeerId(result.peerId ?? ''); }
    }).catch(error => {
      if (active) setMessage(error instanceof Error ? error.message : 'Configurazione RustDesk non disponibile.');
    });
    return () => { active = false; };
  }, [deviceId]);

  async function work(action: () => Promise<void>) {
    setBusy(true); setMessage('');
    try { await action(); }
    catch (error) { setMessage(error instanceof Error ? error.message : 'Operazione non riuscita.'); }
    finally { setBusy(false); }
  }

  async function launch() {
    if (!window.confirm(`Aprire il desktop di ${deviceName} con RustDesk? L’accesso sarà registrato in OmniDesk; autorizzazione e conferma della connessione sono gestite anche dal client RustDesk.`)) return;
    await work(async () => {
      const result = await api<{ url: string }>(`/devices/${deviceId}/rustdesk/connect`, 'POST');
      window.location.href = result.url;
      onMessage(`Avvio RustDesk richiesto per ${deviceName}.`);
    });
  }

  async function copy(label: string, value: string) {
    try { await navigator.clipboard.writeText(value); setMessage(`${label} copiato.`); }
    catch { setMessage(`Non è stato possibile copiare ${label.toLowerCase()}. Seleziona il valore e copialo manualmente.`); }
  }

  return <section className="rustdesk-panel" aria-label="Desktop remoto RustDesk">
    <div className="section-heading">
      <div><span className="eyebrow">DESKTOP REMOTO</span><h3>RustDesk</h3></div>
      <button type="button" className="secondary compact" disabled={busy} onClick={() => void work(load)}><RefreshCw size={15}/>Aggiorna</button>
    </div>
    <p>OmniDesk verifica il permesso <code>remote.control</code> e registra la richiesta di avvio. La connessione si apre nel client RustDesk installato su questo computer; conferma, stato e chiusura sono gestiti dal client.</p>
    {message && <p role="status" className="rustdesk-message">{message}</p>}
    {!config ? <div className="remote-empty-state">Configurazione RustDesk in caricamento…</div> : <>
      {!config.configured && <p role="alert" className="error">Configurazione del server RustDesk incompleta sul server OmniDesk.</p>}
      {config.mapped ? <p className="rustdesk-device-id"><ShieldCheck size={16}/>ID RustDesk associato: <strong>{config.peerId}</strong></p> : <p className="remote-empty-state">Questo dispositivo non è ancora associato a un ID RustDesk.</p>}
      <button type="button" disabled={busy || !allowed || revoked || !config.configured || !config.mapped} onClick={() => void launch()}><ExternalLink size={16}/>Collegati con RustDesk</button>
      {!allowed && <p className="muted">Non hai il permesso remote.control per questo dispositivo.</p>}
      {revoked && <p className="error">Dispositivo revocato: collegamento disabilitato.</p>}

      {config.configured && (allowed || admin) && <details className="advanced rustdesk-settings">
        <summary>Configurazione client RustDesk</summary>
        <p>Inserisci questi valori in Impostazioni → Rete del client RustDesk. Il server API resta vuoto con il server OSS. La chiave è pubblica e serve a verificare l’identità del server.</p>
        <dl>
          {[
            ['ID server', config.idServer ?? ''],
            ['Relay server', config.relayServer ?? ''],
            ['Server API', config.apiServer ?? ''],
            ['Chiave pubblica', config.publicKey ?? '']
          ].map(([label, value]) => <div key={label}><dt>{label}</dt><dd><code>{value || '—'}</code>{value && <button type="button" className="secondary compact" onClick={() => void copy(label, value)} aria-label={`Copia ${label}`}><Clipboard size={14}/>Copia</button>}</dd></div>)}
        </dl>
      </details>}

      {admin && <details className="advanced rustdesk-settings">
        <summary>Associa ID RustDesk a questo dispositivo</summary>
        <form onSubmit={event => {
          event.preventDefault();
          if (!window.confirm(`Confermi che l’ID RustDesk ${peerId} appartiene a ${deviceName}?`)) return;
          void work(async () => {
            await api(`/admin/devices/${deviceId}/rustdesk`, 'PUT', { peerId });
            await load();
            setMessage('Associazione RustDesk salvata.');
          });
        }}>
          <label>ID RustDesk del dispositivo<input inputMode="numeric" autoComplete="off" pattern="[0-9]{6,20}" maxLength={20} required value={peerId} onChange={event => setPeerId(event.target.value)} placeholder="ID mostrato dal client RustDesk"/></label>
          <button type="submit" disabled={busy || !/^[0-9]{6,20}$/.test(peerId)}>Salva associazione</button>
        </form>
        {config.mapped && <button type="button" className="secondary compact" disabled={busy} onClick={() => {
          if (!window.confirm(`Rimuovere l’associazione RustDesk di ${deviceName}?`)) return;
          void work(async () => {
            await api(`/admin/devices/${deviceId}/rustdesk`, 'DELETE');
            await load();
            setMessage('Associazione RustDesk rimossa.');
          });
        }}>Rimuovi associazione</button>}
        <p>L’associazione viene salvata nello storico amministrativo. Verifica l’ID sul dispositivo prima di confermare.</p>
      </details>}
    </>}
  </section>;
}
