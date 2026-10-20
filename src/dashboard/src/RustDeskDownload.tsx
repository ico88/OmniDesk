import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type Info = {
  available: boolean;
  url: string | null;
  release: { version: string; sha256: string; size: number } | null;
};

export function RustDeskDownload() {
  const [info, setInfo] = useState<Info | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    void api<Info>('/admin/downloads/rustdesk')
      .then(setInfo)
      .catch(cause => setError(cause instanceof Error ? cause.message : 'Download non disponibile'));
  }, []);

  return <section className="panel download-card">
    <span className="eyebrow">ACCESSO REMOTO</span>
    <h2><Icon name="download"/>Client RustDesk</h2>
    {error && <p role="alert">{error}</p>}
    {!info && !error && <p>Verifica pacchetto disponibile…</p>}
    {info && !info.available && <p>Il client RustDesk verificato non è ancora pubblicato.</p>}
    {info?.available && info.release && info.url && <>
      <p>Versione {info.release.version} · Windows x64 · {(info.release.size / 1048576).toFixed(1)} MB</p>
      <a className="button" href={info.url} download><Icon name="download"/>Scarica RustDesk per Windows</a>
      <p>Per configurare il client, usa i parametri nella scheda RustDesk qui sopra. L’installazione non imposta password, non abilita l’accesso non presidiato e non modifica i permessi del dispositivo.</p>
      <details><summary>Checksum SHA256 dell’installer</summary><code style={{ overflowWrap: 'anywhere' }}>{info.release.sha256}</code></details>
    </>}
  </section>;
}
