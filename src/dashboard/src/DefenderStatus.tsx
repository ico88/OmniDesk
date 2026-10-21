import { useEffect, useState } from 'react';
import { AlertTriangle, Check, Clock3, Shield, ShieldAlert } from 'lucide-react';
import { api } from './api';

type DefenderExclusion = { kind: 'path' | 'process' | 'extension'; value: string };
type DefenderSnapshot = {
  observedAt: string;
  providerState: 'available' | 'unsupported' | 'error';
  antivirusEnabled: boolean | null;
  realTimeProtectionEnabled: boolean | null;
  tamperProtection: boolean | null;
  engineVersion: string | null;
  signatureVersion: string | null;
  signatureUpdatedAt: string | null;
  quickScanStartTime: string | null;
  fullScanStartTime: string | null;
  managementSource: 'unknown';
  exclusionsAvailable: boolean;
  exclusionsTruncated: boolean;
  exclusions: DefenderExclusion[] | null;
};
type DefenderResponse = { observedAt: string | null; receivedAt: string | null; snapshot: DefenderSnapshot | null };

const date = (value: string | null) => value ? new Date(value).toLocaleString('it-IT') : 'Non disponibile';
const yesNoUnknown = (value: boolean | null) => value === null ? 'Non disponibile' : value ? 'Attivo' : 'Disattivo';

export function DefenderStatus({ deviceId }: { deviceId: string }) {
  const [result, setResult] = useState<DefenderResponse | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  async function refresh() {
    setBusy(true); setError('');
    try { setResult(await api<DefenderResponse>(`/devices/${deviceId}/security/defender`)); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Impossibile leggere lo stato Defender.'); }
    finally { setBusy(false); }
  }
  useEffect(() => { void refresh(); }, [deviceId]);

  const snapshot = result?.snapshot;
  const stale = result?.observedAt ? Date.now() - new Date(result.observedAt).getTime() > 48 * 60 * 60 * 1000 : false;
  const statusLabel = !snapshot ? 'In attesa del primo report' : snapshot.providerState === 'available' ? stale ? 'Dato non aggiornato' : 'Stato rilevato' : snapshot.providerState === 'unsupported' ? 'Provider non disponibile' : 'Lettura non riuscita';
  const statusClass = !snapshot || snapshot.providerState !== 'available' ? 'offline' : stale ? 'offline' : snapshot.realTimeProtectionEnabled === false || snapshot.antivirusEnabled === false ? 'danger' : 'online';

  return <div className="defender-status">
    <div className="section-heading"><div><span className="eyebrow">RACCOLTA IN SOLA LETTURA</span><h3><Shield size={18}/>Microsoft Defender</h3></div><button className="secondary compact" type="button" disabled={busy} onClick={() => void refresh()}><Clock3 size={15}/>{busy ? 'Caricamento…' : 'Ricarica report'}</button></div>
    {error && <p className="error" role="alert">{error}</p>}
    <p className="muted">Lo stato viene raccolto dall’Agent circa una volta al giorno. OmniDesk non modifica Defender né applica esclusioni.</p>
    <div className="device-summary-grid defender-summary">
      <article className="device-info-card"><h4>Antivirus</h4><strong>{snapshot ? yesNoUnknown(snapshot.antivirusEnabled) : 'Non disponibile'}</strong><small>Servizio antivirus</small></article>
      <article className="device-info-card"><h4>Protezione in tempo reale</h4><strong>{snapshot ? yesNoUnknown(snapshot.realTimeProtectionEnabled) : 'Non disponibile'}</strong><small>Stato osservato dal computer</small></article>
      <article className="device-info-card"><h4>Tamper Protection</h4><strong>{snapshot ? yesNoUnknown(snapshot.tamperProtection) : 'Non disponibile'}</strong><small>Il valore può non essere esposto da Windows</small></article>
      <article className="device-info-card"><h4>Firme antivirus</h4><strong>{snapshot?.signatureVersion ?? 'Non disponibile'}</strong><small>Aggiornate: {date(snapshot?.signatureUpdatedAt ?? null)}</small></article>
    </div>
    <div className="section-heading defender-observation"><div><span className={`status-pill ${statusClass}`}>{statusLabel}</span><small>Osservato: {date(result?.observedAt ?? null)} · Ricevuto: {date(result?.receivedAt ?? null)}</small></div><small>Origine della policy: non determinata</small></div>
    {snapshot?.providerState === 'available' && <>
      <div className="defender-scan-times"><span>Motore: {snapshot.engineVersion ?? 'Non disponibile'}</span><span>Ultima scansione rapida: {date(snapshot.quickScanStartTime)}</span><span>Ultima scansione completa: {date(snapshot.fullScanStartTime)}</span></div>
      <h4>Esclusioni rilevate</h4>
      {!snapshot.exclusionsAvailable ? <p className="muted">Le esclusioni non sono state esposte dal provider.</p> : snapshot.exclusions?.length ? <div className="table-wrap"><table><thead><tr><th>Tipo</th><th>Valore</th><th>Origine</th></tr></thead><tbody>{snapshot.exclusions.map((item, index) => <tr key={`${item.kind}-${index}`}><td>{item.kind === 'path' ? 'Percorso' : item.kind === 'process' ? 'Processo' : 'Estensione'}</td><td><code>{item.value}</code></td><td>Sconosciuta</td></tr>)}</tbody></table></div> : <p><Check size={15}/>Nessuna esclusione riportata da Windows.</p>}
      {snapshot.exclusionsTruncated && <p className="muted"><AlertTriangle size={15}/>Elenco parziale: Windows ha riportato più valori di quelli raccolti.</p>}
    </>}
    {snapshot?.providerState === 'unsupported' && <p className="muted"><AlertTriangle size={15}/>Il provider Microsoft Defender non è disponibile su questo sistema.</p>}
    {snapshot?.providerState === 'error' && <p className="muted"><ShieldAlert size={15}/>Windows non ha restituito uno snapshot leggibile. L’errore non è classificato come blocco policy.</p>}
  </div>;
}
