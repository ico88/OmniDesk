import { useEffect, useState } from 'react';
import { Activity, AlertTriangle, Cpu, HardDrive, MemoryStick, Monitor, Network, Server, ShieldCheck, Timer } from 'lucide-react';
import { api } from './api';
import { DevicePanel, DeviceTabs, type DeviceTabId } from './DeviceTabs';
import { DeviceManagement } from './DeviceManagement';
import { RustDeskRemote } from './RustDeskRemote';
import { RustDeskDownload } from './RustDeskDownload';
import { DefenderStatus } from './DefenderStatus';
import { SupportDesk } from './SupportDesk';
import { Icon } from './Icon';
import { reportsFilteredNetworkAdapters } from './deviceNetwork';

type MetricDisk = { name: string; totalBytes: number; freeBytes: number };
type SmartDisk = { name: string; status: 'unavailable' | 'predicted_failure' | 'no_failure_predicted' };
type NetworkAdapter = { name: string; type: string; macAddress: string; ipAddresses: string[] };
export type DeviceStatus = {
  id: string; name: string; groupId: string | null; siteId: string | null; capabilities: string[]; online: boolean; revoked: boolean; lastSeen: string | null;
  metrics: { cpuPercent: number | null; totalMemoryBytes: number; availableMemoryBytes: number; networkReceivedBytes: number; networkSentBytes: number; uptimeSeconds: number; disks: MetricDisk[]; agentVersion?: string | null; networkAdapters?: NetworkAdapter[] | null; smartDisks?: SmartDisk[] | null } | null;
  inventory: { operatingSystem: string; architecture: string; manufacturer: string | null; model: string | null; serialNumber: string | null; processor: string | null; logicalProcessors: number; software: { name: string; version: string | null; publisher: string | null }[]; warnings: string[] } | null;
};
type Group = { id: string; name: string };
type UpdateItem = { UpdateId: string; Revision: number; Title: string; Kb: string[]; ResultCode: number | null };
type AgentUpdateResult = { Succeeded: boolean; RebootRequired: boolean; Error: string | null; Updates: UpdateItem[]; FailureCategory?: string | null; PreviousVersion?: string | null; TargetVersion?: string | null; InstalledVersion?: string | null; RollbackState?: string | null; WindowsInstallerExitCode?: number | null };
type UpdateJob = { id: string; kind: string; state: string; createdAt: string; completedAt?: string | null; result: string | null };
type DiagnosticJob = UpdateJob & { completedAt: string | null };
type DiagnosticData = { Kind: string; CollectedAt: string; Processes?: { ProcessId: number; Name: string; StartedAt: string; CanTerminate: boolean }[]; Services?: { Name: string; DisplayName: string; State: string; StartType: string }[]; Events?: { Log: string; TimeCreated: string; Id: number; Level: string; Source: string; Message: string }[] };
type ServiceJob = UpdateJob & { completedAt: string | null; payload?: string };
type WindowsDownload = { available: boolean; url: string | null; release: { version: string; msiSha256?: string | null; msiSize?: number | null } | null };

const jobLabels: Record<string, string> = { queued: 'In coda', leased: 'In esecuzione', succeeded: 'Completato', failed: 'Non riuscito', cancelled: 'Annullato' };
const updateFailureLabels: Record<string, string> = { download_failed: 'Download del pacchetto non riuscito', integrity_check_failed: 'Verifica integrità del pacchetto non riuscita', file_access_denied: 'Accesso ai file dell’Agent negato', invalid_package: 'Pacchetto o metadati non validi', rollback_package_unavailable: 'Pacchetto di ripristino non disponibile', file_io_failed: 'Errore durante la scrittura del pacchetto', installer_result_unknown: 'Esito dell’installazione non verificato', msi_install_failed: 'Windows Installer non ha completato l’installazione', post_install_health_failed: 'Il servizio non ha superato il controllo dopo l’aggiornamento' };
const rollbackLabels: Record<string, string> = { not_started: 'Ripristino non avviato', not_verified: 'Ripristino non verificato', installer_reverted: 'Windows Installer ha annullato l’installazione', service_unhealthy: 'Servizio non disponibile dopo il ripristino', not_needed: 'Non necessario', succeeded: 'Versione precedente ripristinata', failed: 'Ripristino automatico non riuscito' };
function UpdateResultDetails({ raw, kind, selectedUpdates, onSelectUpdate }: { raw: string; kind: string; selectedUpdates: UpdateItem[]; onSelectUpdate: (item: UpdateItem, selected: boolean) => void }) {
  try {
    const result = JSON.parse(raw) as AgentUpdateResult;
    return <div className="update-result">
      <p>{result.Succeeded ? kind === 'agent.update' ? 'Agent aggiornato correttamente' : 'Operazione completata' : 'Aggiornamento non riuscito'}{result.RebootRequired ? ' · Riavvio di Windows necessario' : result.Succeeded && kind === 'agent.update' ? ' · Riavvio di Windows non necessario' : ''}</p>
      {kind === 'agent.update' && <div className="update-diagnostics">
        {result.FailureCategory && <p><strong>Diagnosi:</strong> {updateFailureLabels[result.FailureCategory] ?? 'Errore Agent non classificato'}</p>}
        {(result.PreviousVersion || result.TargetVersion || result.InstalledVersion) && <p><strong>Versioni:</strong> {result.PreviousVersion ?? '—'} → {result.TargetVersion ?? '—'}{result.InstalledVersion ? ` · installata ${result.InstalledVersion}` : ''}</p>}
        {result.RollbackState && <p><strong>Ripristino:</strong> {rollbackLabels[result.RollbackState] ?? 'Stato non riconosciuto'}</p>}
        {result.WindowsInstallerExitCode != null && <p><strong>Windows Installer:</strong> codice {result.WindowsInstallerExitCode}</p>}
      </div>}
      {result.Error && <p className="error" role="alert">{result.Error}</p>}
      {kind === 'scan' && result.Succeeded && result.Updates.map(item => <label className="update-choice" key={`${item.UpdateId}-${item.Revision}`}><input type="checkbox" checked={selectedUpdates.some(x => x.UpdateId === item.UpdateId)} onChange={event => onSelectUpdate(item, event.target.checked)}/>{item.Title} · {item.Kb.join(', ')}</label>)}
    </div>;
  } catch { return <p>Risultato non leggibile; richiedi di nuovo l’elenco lavori.</p>; }
}
const elapsed = (from: string, to = Date.now()) => {
  const seconds = Math.max(0, Math.floor((to - new Date(from).getTime()) / 1000));
  return seconds < 60 ? `${seconds} secondi` : `${Math.floor(seconds / 60)} min ${seconds % 60} sec`;
};
const byteCount = (bytes: number) => `${(bytes / 1073741824).toFixed(1)} GiB`;
const usableSerial = (serial: string | null | undefined) => {
  const value = serial?.trim();
  return value && !['to be filled by o.e.m.', 'system serial number', 'default string', 'unknown', 'none', 'not specified'].includes(value.toLowerCase()) ? value : null;
};
const versionAtLeast = (value: string | null | undefined, minimum: string) => {
  if (!value) return false;
  const a = value.split('.').map(Number); const b = minimum.split('.').map(Number);
  if (a.length !== 3 || a.some(x => !Number.isInteger(x) || x < 0)) return false;
  for (let i = 0; i < 3; i++) { if ((a[i] ?? 0) !== (b[i] ?? 0)) return (a[i] ?? 0) > (b[i] ?? 0); }
  return true;
};

export function DeviceWorkspace({ device, admin, groups, sites, onDeviceChanged, onMessage }: {
  device: DeviceStatus;
  admin: boolean;
  groups: Group[];
  sites: { id: string; name: string; isArchived?: boolean }[];
  onDeviceChanged: (message: string) => Promise<void>;
  onMessage: (message: string) => void;
}) {
  const [activeTab, setActiveTab] = useState<DeviceTabId>('desktop');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [jobs, setJobs] = useState<UpdateJob[]>([]);
  const [jobsCheckedAt, setJobsCheckedAt] = useState(Date.now());
  const [selectedUpdates, setSelectedUpdates] = useState<UpdateItem[]>([]);
  const [windowsRelease, setWindowsRelease] = useState<WindowsDownload | null>(null);
  const [diagnosticJobs, setDiagnosticJobs] = useState<DiagnosticJob[]>([]);
  const [serviceJobs, setServiceJobs] = useState<ServiceJob[]>([]);
  const [processJobs, setProcessJobs] = useState<ServiceJob[]>([]);

  async function work(action: () => Promise<void>) {
    setBusy(true);
    setError('');
    try { await action(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Operazione non riuscita.'); }
    finally { setBusy(false); }
  }

  async function loadJobs() { setJobs((await api<UpdateJob[]>(`/devices/${device.id}/updates`)).filter(job => ['scan', 'install', 'agent.update'].includes(job.kind))); setSelectedUpdates([]); }
  async function loadDiagnostics() { setDiagnosticJobs(await api<DiagnosticJob[]>(`/devices/${device.id}/diagnostics`)); }
  async function loadServiceJobs() { setServiceJobs(await api<ServiceJob[]>(`/devices/${device.id}/services/jobs`)); }
  async function loadProcessJobs() { setProcessJobs(await api<ServiceJob[]>(`/devices/${device.id}/processes/jobs`)); }
  async function requestProcessKill(processId: number, processName: string, startedAt: string) {
    if (!window.confirm(`Confermi la terminazione del processo “${processName}” (PID ${processId}) su ${device.name}? Il lavoro non può essere annullato dopo l’avvio.`)) return;
    await work(async () => {
      await api(`/devices/${device.id}/processes/actions`, 'POST', { requestId: crypto.randomUUID(), processId, processName, startedAt });
      await loadProcessJobs();
    });
  }
  async function requestServiceAction(serviceName: string, action: 'start' | 'stop' | 'restart') {
    const verb = action === 'start' ? 'avviare' : action === 'stop' ? 'arrestare' : 'riavviare';
    if (!window.confirm(`Confermi di ${verb} il servizio Windows “${serviceName}” sul dispositivo ${device.name}?`)) return;
    await work(async () => {
      await api(`/devices/${device.id}/services/actions`, 'POST', { requestId: crypto.randomUUID(), serviceName, action });
      await loadServiceJobs();
    });
  }

  useEffect(() => {
    let active = true;
    if (admin) void api<WindowsDownload>('/admin/downloads/windows').then(value => { if (active) setWindowsRelease(value); }).catch(() => { if (active) setWindowsRelease({ available: false, url: null, release: null }); });
    else setWindowsRelease(null);
    return () => { active = false; };
  }, [admin]);

  useEffect(() => {
    if (activeTab === 'updates' && device.capabilities.includes('updates')) void work(loadJobs);
  }, [activeTab, device.id]);

  useEffect(() => {
    if (activeTab !== 'updates' || !device.capabilities.includes('updates')) return;
    let active = true;
    const refresh = async () => {
      try {
        const rows = await api<UpdateJob[]>(`/devices/${device.id}/updates`);
        if (active) { setJobs(rows.filter(job => ['scan', 'install', 'agent.update'].includes(job.kind))); setJobsCheckedAt(Date.now()); }
      } catch { /* Keep the last known state; manual refresh reports errors. */ }
    };
    const timer = window.setInterval(() => void refresh(), 5000);
    return () => { active = false; window.clearInterval(timer); };
  }, [activeTab, device.id, device.capabilities]);

  useEffect(() => {
    if (activeTab !== 'processes' && activeTab !== 'services' && activeTab !== 'events') return;
    if (!count('diagnostics.view')) return;
    let active = true;
    const refresh = async () => { try { const rows = await api<DiagnosticJob[]>(`/devices/${device.id}/diagnostics`); if (active) setDiagnosticJobs(rows); } catch { /* The page shows a useful error on explicit refresh/request. */ } };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 4000);
    return () => { active = false; window.clearInterval(timer); };
  }, [activeTab, device.id, device.capabilities]);

  useEffect(() => {
    if (activeTab !== 'services' || !device.capabilities.includes('services.manage')) return;
    let active = true;
    const refresh = async () => { try { const rows = await api<ServiceJob[]>(`/devices/${device.id}/services/jobs`); if (active) setServiceJobs(rows); } catch { /* Explicit actions surface request errors in the panel. */ } };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 4000);
    return () => { active = false; window.clearInterval(timer); };
  }, [activeTab, device.id, device.capabilities]);

  useEffect(() => {
    if (activeTab !== 'processes' || !device.capabilities.includes('processes.manage')) return;
    let active = true;
    const refresh = async () => { try { const rows = await api<ServiceJob[]>(`/devices/${device.id}/processes/jobs`); if (active) setProcessJobs(rows); } catch { /* Explicit actions surface request errors in the panel. */ } };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 4000);
    return () => { active = false; window.clearInterval(timer); };
  }, [activeTab, device.id, device.capabilities]);

  const metrics = device.metrics;
  const networkInventoryCurrent = reportsFilteredNetworkAdapters(metrics?.agentVersion);
  const inventory = device.inventory;
  const count = (capability: string) => device.capabilities.includes(capability);
  return <section className="panel device-detail device-workspace">
    <div className="section-heading device-workspace-heading">
      <div><span className="eyebrow">DISPOSITIVO</span><h2><Monitor size={22}/>{device.name}</h2><small>{inventory?.operatingSystem ?? 'Sistema operativo non disponibile'} · Agent {metrics?.agentVersion ?? 'non rilevato'} · Ultimo contatto {device.lastSeen ? new Date(device.lastSeen).toLocaleString('it-IT') : 'mai'}</small></div>
      <span className={`status-pill ${device.revoked ? 'danger' : device.online ? 'online' : 'offline'}`}>{device.revoked ? 'Revocato' : device.online ? 'Online' : 'Offline'}</span>
    </div>
    <DeviceTabs active={activeTab} onChange={tab => { setActiveTab(tab); setError(''); }}/>
    {error && <p className="error" role="alert">{error}</p>}

    {activeTab === 'overview' && <DevicePanel id="overview">
      {metrics ? <div className="metrics-strip device-metrics">
        <article><span><Cpu size={16}/>CPU</span><strong>{metrics.cpuPercent === null ? 'N/D' : `${metrics.cpuPercent.toFixed(1)}%`}</strong><small>Ultimo campione ricevuto</small>{metrics.cpuPercent !== null && <div className="meter"><i style={{ width: `${Math.min(100, Math.max(0, metrics.cpuPercent))}%` }}/></div>}</article>
        <article><span><MemoryStick size={16}/>Memoria disponibile</span><strong>{byteCount(metrics.availableMemoryBytes)}</strong><small>su {byteCount(metrics.totalMemoryBytes)} totali</small><div className="meter"><i style={{ width: `${100 - (metrics.availableMemoryBytes / metrics.totalMemoryBytes * 100)}%` }}/></div></article>
        <article><span><Timer size={16}/>Uptime</span><strong>{Math.floor(metrics.uptimeSeconds / 3600)} <small>ore</small></strong><small>Dall’ultimo avvio del sistema</small></article>
      </div> : <div className="remote-empty-state"><Activity size={20}/>Metriche non ancora ricevute dall’agente.</div>}
      {(inventory || metrics) && <div className="device-summary-grid">
        {inventory && <article className="device-info-card"><h3><Server size={16}/>Sistema</h3><p>{inventory.operatingSystem} · {inventory.architecture}</p><p>{inventory.manufacturer ?? 'Produttore non rilevato'} {inventory.model ?? ''}</p><small>{inventory.processor ?? 'CPU non rilevata'} · {inventory.logicalProcessors} processori logici</small><small className="serial-number">Numero di serie: {usableSerial(inventory.serialNumber) ?? 'non disponibile'}</small></article>}
        {metrics && <article className="device-info-card"><h3><HardDrive size={16}/>Archiviazione</h3>{metrics.disks.length ? metrics.disks.map(d => <div className="disk-row" key={d.name}><span>{d.name}</span><strong>{byteCount(d.freeBytes)} liberi</strong><small>di {byteCount(d.totalBytes)}</small><div className="meter"><i style={{ width: `${Math.min(100, Math.max(0, (d.totalBytes - d.freeBytes) / d.totalBytes * 100))}%` }}/></div></div>) : <p>Dati disco non disponibili.</p>}<div className="smart-health"><strong>Stato SMART</strong>{metrics.smartDisks?.length ? metrics.smartDisks.map((disk,index)=>{const failure=disk.status==='predicted_failure';const unavailable=disk.status==='unavailable';return <div className="smart-health-row" key={`${disk.name}-${index}`}><span>{disk.name}</span><span className={`status-pill ${failure?'danger':unavailable?'offline':'online'}`}>{failure?'Guasto predetto':unavailable?'Non disponibile':'Nessun guasto predetto'}</span></div>;}) : <small>Non disponibile: dipende dal supporto di disco, controller e driver.</small>}<small>La previsione SMART è un indicatore del dispositivo, non una garanzia sullo stato del disco.</small></div></article>}
        {metrics && <article className="device-info-card"><h3><Network size={16}/>Rete</h3>
          {!networkInventoryCurrent ? <p className="network-stale-note"><AlertTriangle size={14}/>Agent {metrics?.agentVersion ?? 'installato'}: elenco rete non filtrato. Aggiorna alla versione 0.8.12 o successiva; i dati si aggiorneranno al prossimo heartbeat.</p> : metrics?.networkAdapters?.length ? <div className="network-adapter-list">{metrics.networkAdapters.map((adapter, index) => <div className="network-adapter" key={`${adapter.name}-${index}`}>
            <strong>{adapter.type === 'Wireless80211' ? 'Wi-Fi' : adapter.type.startsWith('Ethernet') ? 'Ethernet' : adapter.type} · {adapter.name}</strong>
            <span>IP {adapter.ipAddresses.length ? adapter.ipAddresses.join(', ') : 'non disponibile'}</span>
            <span>MAC {adapter.macAddress || 'non disponibile'}</span>
          </div>)}</div> : <p>Interfacce attive non ancora rilevate nell’ultimo heartbeat.</p>}
          {metrics && <><p>{byteCount(metrics.networkReceivedBytes)} ricevuti · {byteCount(metrics.networkSentBytes)} inviati</p><small>Contatori cumulativi dall’avvio dell’agente</small></>}
        </article>}
      </div>}
      {!!inventory?.warnings.length && <div className="device-warning-list">{inventory.warnings.map((warning, index) => <p key={index}><AlertTriangle size={15}/>{warning}</p>)}</div>}
      {inventory && <div className="device-summary-footer"><span>Software rilevato</span><strong>{inventory.software.length}</strong><button className="secondary compact" onClick={() => setActiveTab('software')}>Apri inventario <Icon name="arrow" size={14}/></button></div>}
    </DevicePanel>}

    {activeTab === 'desktop' && <DevicePanel id="desktop"><div className="remote-workspace">
      <div className="remote-workspace-main">
        <RustDeskRemote deviceId={device.id} deviceName={device.name} admin={admin} allowed={count('remote.control')} revoked={device.revoked} onMessage={onMessage}/>
        {admin&&<RustDeskDownload/>}
      </div>
      <aside className="remote-device-sidebar"><div className="device-sidebar-heading"><span className="eyebrow">DETTAGLI E DIAGNOSTICA</span><ShieldCheck size={18}/></div>
        <div className="sidebar-status"><span className={`connection-indicator ${device.online ? 'ready' : ''}`}/><strong>{device.online ? 'Agent online' : 'Agent offline'}</strong><small>{device.lastSeen ? `Ultimo heartbeat ${new Date(device.lastSeen).toLocaleTimeString('it-IT')}` : 'Nessun heartbeat ricevuto'}</small></div>
        <dl>{[['Sistema operativo', inventory?.operatingSystem], ['Modello', [inventory?.manufacturer, inventory?.model].filter(Boolean).join(' ')], ['CPU', inventory?.processor], ['Memoria', metrics ? `${byteCount(metrics.availableMemoryBytes)} liberi` : undefined]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value || 'Dato non disponibile'}</dd></div>)}</dl>
        <p className="sidebar-note">Lo stato online dell’Agent non conferma la disponibilità di RustDesk su questo dispositivo.</p>
      </aside>
    </div></DevicePanel>}

    {activeTab === 'defender' && <DevicePanel id="defender">{count('endpointsecurity.defender.view') ? <DefenderStatus key={device.id} deviceId={device.id}/> : <p>Non hai il permesso endpointsecurity.defender.view su questo dispositivo.</p>}</DevicePanel>}

    {activeTab === 'software' && <DevicePanel id="software"><div className="section-heading"><div><span className="eyebrow">INVENTARIO</span><h3>Software installato</h3></div><span className="count">{inventory?.software.length ?? 0}</span></div>
      {!inventory ? <p>Inventario non ancora ricevuto dall’agente.</p> : <div className="table-wrap"><table><thead><tr><th>Software</th><th>Versione</th><th>Produttore</th></tr></thead><tbody>{inventory.software.map((app, index) => <tr key={`${app.name}-${index}`}><td>{app.name}</td><td>{app.version || '—'}</td><td>{app.publisher || '—'}</td></tr>)}</tbody></table></div>}
    </DevicePanel>}

    {activeTab === 'updates' && <DevicePanel id="updates"><div className="section-heading"><div><span className="eyebrow">GESTIONE</span><h3>Aggiornamenti Windows</h3></div><button className="secondary compact" disabled={busy || !count('updates')} onClick={() => void work(loadJobs)}><Icon name="refresh" size={15}/>Aggiorna</button></div>
      {!count('updates') ? <p>Non hai il permesso per gestire gli aggiornamenti di questo dispositivo.</p> : <>
        <article className="update-job-card"><div><strong>OmniDesk Agent</strong><span>Installato: {metrics?.agentVersion ?? 'versione non rilevata'}</span></div>{admin ? <><small>{!versionAtLeast(metrics?.agentVersion, '0.8.18') ? windowsRelease?.available && windowsRelease.release?.version && versionAtLeast(windowsRelease.release.version, '0.8.18') ? `Installa una volta il kit ${windowsRelease.release.version} dal passaggio “Scarica Agent” per attivare il controllo salute e il ripristino automatico.` : 'Il controllo salute e il ripristino automatico richiedono Agent 0.8.18; il kit aggiornato non è ancora disponibile.' : windowsRelease?.available && windowsRelease.release?.msiSha256 ? `Release disponibile: ${windowsRelease.release.version}` : 'Release MSI verificata non disponibile sul server'}</small>{!versionAtLeast(metrics?.agentVersion, '0.8.18') && windowsRelease?.url && windowsRelease.release?.version && versionAtLeast(windowsRelease.release.version, '0.8.18') && <a className="button secondary compact" href={windowsRelease.url} download>Scarica kit Agent {windowsRelease.release.version}</a>}<button disabled={busy || device.revoked || !device.online || !versionAtLeast(metrics?.agentVersion, '0.8.18') || !windowsRelease?.available || !windowsRelease.release?.msiSha256 || !windowsRelease.release?.msiSize || !windowsRelease.release.version || versionAtLeast(metrics?.agentVersion, windowsRelease.release.version)} onClick={() => {
          const target = windowsRelease?.release?.version;
          if (!target || !window.confirm(`Aggiornare OmniDesk Agent a ${target}? Il servizio si arresterà e ripartirà durante l’installazione. Non è previsto un riavvio di Windows.`)) return;
          void work(async () => { await api(`/admin/devices/${device.id}/agent-update`, 'POST', { requestId: crypto.randomUUID() }); await loadJobs(); });
        }}>Aggiorna Agent</button></> : <small>Solo un amministratore globale può aggiornare il software Agent.</small>}</article>
        <div className="action-row"><button disabled={busy || device.revoked} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/scan`, 'POST', { requestId: crypto.randomUUID() }); await loadJobs(); })}>Avvia scansione</button><p>I lavori vengono eseguiti dall’agente. Nessun riavvio automatico.</p></div>
        {!jobs.length && <p>Nessun lavoro di aggiornamento registrato.</p>}
        {jobs.map(job => <article className="update-job-card" key={job.id}><div><strong>{job.kind === 'scan' ? 'Scansione' : job.kind === 'install' ? 'Installazione aggiornamenti' : 'Aggiornamento Agent'}</strong><span className={`status-pill ${job.state === 'succeeded' ? 'online' : job.state === 'failed' ? 'danger' : 'offline'}`}>{jobLabels[job.state] ?? job.state}</span></div><small>{new Date(job.createdAt).toLocaleString('it-IT')}{job.state === 'queued' || job.state === 'leased' ? ` · ${elapsed(job.createdAt, jobsCheckedAt)}` : ''}</small>
          {job.kind === 'agent.update' && job.state === 'leased' && <p className="muted">Installazione MSI in corso. Il servizio Agent può riavviarsi durante l’operazione; OmniDesk non riavvia Windows. La pagina controlla l’esito automaticamente ogni 5 secondi.</p>}
          {job.kind === 'agent.update' && job.state === 'leased' && Date.now() - new Date(job.createdAt).getTime() >= 5 * 60_000 && <p className="network-stale-note" role="status"><AlertTriangle size={15}/>Nessun esito ricevuto dopo 5 minuti: non significa che l’installazione sia fallita. Serve la conferma dell’Agent; ultimo controllo {new Date(jobsCheckedAt).toLocaleTimeString('it-IT')}.</p>}
          {job.state === 'queued' && <button className="secondary compact" disabled={busy} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/${job.id}`, 'DELETE'); await loadJobs(); })}>Annulla</button>}
          {job.result && <UpdateResultDetails raw={job.result} kind={job.kind} selectedUpdates={selectedUpdates} onSelectUpdate={(item, selected) => setSelectedUpdates(selected ? [...selectedUpdates.filter(x => x.UpdateId !== item.UpdateId), item] : selectedUpdates.filter(x => x.UpdateId !== item.UpdateId))}/>}
        </article>)}
        {!!selectedUpdates.length && <button disabled={busy || device.revoked} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/install`, 'POST', { requestId: crypto.randomUUID(), updates: selectedUpdates.map(item => ({ updateId: item.UpdateId, revision: item.Revision })) }); setSelectedUpdates([]); await loadJobs(); })}>Installa {selectedUpdates.length} aggiornamenti selezionati</button>}
      </>}
    </DevicePanel>}

    {activeTab === 'tickets' && <DevicePanel id="tickets">{count('tickets.view') ? <SupportDesk key={device.id} sites={sites} devices={[{id:device.id,name:device.name,siteId:device.siteId}]} deviceId={device.id} visibleDeviceIds={[]} onOpenDevice={async () => undefined}/> : <p>Non hai il permesso tickets.view su questo dispositivo.</p>}</DevicePanel>}

    {(['processes', 'services', 'events'] as const).includes(activeTab as 'processes' | 'services' | 'events') && <DevicePanel id={activeTab}>
      {(() => {
        const kind = activeTab as 'processes' | 'services' | 'events';
        const title = kind === 'processes' ? 'Processi attivi' : kind === 'services' ? 'Servizi Windows' : 'Eventi recenti';
        const jobKind = `diagnostics.${kind}`;
        const latest = diagnosticJobs.find(job => job.kind === jobKind);
        let data: DiagnosticData | null = null;
        if (latest?.state === 'succeeded' && latest.result) { try { data = JSON.parse(latest.result).DiagnosticJson ? JSON.parse(JSON.parse(latest.result).DiagnosticJson) as DiagnosticData : null; } catch { data = null; } }
        return <>
          <div className="section-heading"><div><span className="eyebrow">{kind === 'services' ? 'INVENTARIO E CONTROLLO AUTORIZZATO' : 'DIAGNOSTICA IN SOLA LETTURA'}</span><h3>{title}</h3></div><div className="action-row"><button className="secondary compact" disabled={busy || !count('diagnostics.view') || device.revoked || !device.online} onClick={() => void work(loadDiagnostics)}><Icon name="refresh" size={15}/>Aggiorna</button><button disabled={busy || !count('diagnostics.view') || device.revoked || !device.online} onClick={() => void work(async () => { await api(`/devices/${device.id}/diagnostics/${kind}`, 'POST', { requestId: crypto.randomUUID() }); await loadDiagnostics(); })}>Raccogli dati</button></div></div>
          {!count('diagnostics.view') ? <p>Non hai il permesso diagnostics.view su questo dispositivo.</p> : <>
            {latest && <p className="muted">{jobLabels[latest.state] ?? latest.state}{latest.completedAt ? ` · ${new Date(latest.completedAt).toLocaleString('it-IT')}` : ''}{latest.state === 'failed' && latest.result ? ` · ${JSON.parse(latest.result).Error ?? 'Raccolta non riuscita'}` : ''}</p>}
            {!latest && <p>Nessuna raccolta richiesta. I dati vengono letti solo dopo aver premuto “Raccogli dati”.</p>}
            {kind === 'processes' && <>
              {!count('processes.manage') && <p className="muted">La terminazione richiede il permesso separato <code>processes.manage</code>.</p>}
              <div className="table-wrap"><table><thead><tr><th>PID</th><th>Processo</th><th>Avvio</th><th>Azioni</th></tr></thead><tbody>{(data?.Processes ?? []).map((item, i) => <tr key={`${item.ProcessId}-${i}`}><td>{item.ProcessId}</td><td>{item.Name}</td><td>{new Date(item.StartedAt).toLocaleString('it-IT')}</td><td>{count('processes.manage') && (item.CanTerminate ? <button className="secondary compact" disabled={busy || device.revoked || !device.online} onClick={() => void requestProcessKill(item.ProcessId, item.Name, item.StartedAt)}>Termina…</button> : <span className="muted">Protetto</span>)}</td></tr>)}</tbody></table></div>
              {count('processes.manage') && <div className="service-action-history"><h4>Interventi sui processi</h4>{!processJobs.length && <p className="muted">Nessuna azione registrata.</p>}{processJobs.map(job => {
                let result: { Succeeded?: boolean; Error?: string | null } | null = null;
                try { result = job.result ? JSON.parse(job.result) as { Succeeded?: boolean; Error?: string | null } : null; } catch { result = null; }
                let target = 'Processo selezionato';
                try { const payload = JSON.parse(job.payload ?? '{}') as { ProcessId?: number; ProcessName?: string }; target = `${payload.ProcessName ?? 'Processo'} · PID ${payload.ProcessId ?? '?'}`; } catch { /* corrupted legacy result */ }
                return <article className="update-job-card" key={job.id}><div><strong>Terminazione · {job.id.slice(0, 8)}</strong><span className={`status-pill ${job.state === 'succeeded' ? 'online' : job.state === 'failed' ? 'danger' : 'offline'}`}>{jobLabels[job.state] ?? job.state}</span></div><small>{new Date(job.createdAt).toLocaleString('it-IT')}</small><small>{target}</small>{result?.Error && <small>{result.Error}</small>}{job.state === 'queued' && <button className="secondary compact" disabled={busy} onClick={() => void work(async () => { await api(`/devices/${device.id}/processes/jobs/${job.id}`, 'DELETE'); await loadProcessJobs(); })}>Annulla</button>}</article>;
              })}</div>}
            </>}
            {kind === 'services' && <>
              {!count('services.manage') && <p className="muted">La modifica richiede il permesso separato <code>services.manage</code>.</p>}
              <div className="table-wrap"><table><thead><tr><th>Servizio</th><th>Nome visualizzato</th><th>Stato</th><th>Avvio</th><th>Azioni</th></tr></thead><tbody>{(data?.Services ?? []).map(item => <tr key={item.Name}><td>{item.Name}</td><td>{item.DisplayName}</td><td>{item.State}</td><td>{item.StartType}</td><td>{count('services.manage') && <div className="action-row">
                {item.State !== 'Running' && <button className="secondary compact" disabled={busy || device.revoked || !device.online || item.StartType === 'Disabled'} onClick={() => void requestServiceAction(item.Name, 'start')}>Avvia</button>}
                {item.State !== 'Stopped' && <button className="secondary compact" disabled={busy || device.revoked || !device.online} onClick={() => void requestServiceAction(item.Name, 'stop')}>Arresta</button>}
                <button className="secondary compact" disabled={busy || device.revoked || !device.online || item.StartType === 'Disabled'} onClick={() => void requestServiceAction(item.Name, 'restart')}>Riavvia</button>
              </div>}</td></tr>)}</tbody></table></div>
              {count('services.manage') && <div className="service-action-history"><h4>Interventi sui servizi</h4>{!serviceJobs.length && <p className="muted">Nessuna azione registrata.</p>}{serviceJobs.map(job => {
                let result: { Succeeded?: boolean; Error?: string | null } | null = null;
                try { result = job.result ? JSON.parse(job.result) as { Succeeded?: boolean; Error?: string | null } : null; } catch { result = null; }
                const action = job.kind.replace('service.', '');
                return <article className="update-job-card" key={job.id}><div><strong>{action === 'start' ? 'Avvio' : action === 'stop' ? 'Arresto' : 'Riavvio'} · {job.id.slice(0, 8)}</strong><span className={`status-pill ${job.state === 'succeeded' ? 'online' : job.state === 'failed' ? 'danger' : 'offline'}`}>{jobLabels[job.state] ?? job.state}</span></div><small>{new Date(job.createdAt).toLocaleString('it-IT')}</small>{result?.Error && <small>{result.Error}</small>}{job.state === 'queued' && <button className="secondary compact" disabled={busy} onClick={() => void work(async () => { await api(`/devices/${device.id}/services/jobs/${job.id}`, 'DELETE'); await loadServiceJobs(); })}>Annulla</button>}</article>;
              })}</div>}
            </>}
            {kind === 'events' && <div className="table-wrap"><table><thead><tr><th>Data</th><th>Registro</th><th>Livello</th><th>Origine</th><th>Messaggio</th></tr></thead><tbody>{(data?.Events ?? []).map((item, i) => <tr key={`${item.Log}-${item.Id}-${i}`}><td>{new Date(item.TimeCreated).toLocaleString('it-IT')}</td><td>{item.Log}</td><td>{item.Level}</td><td>{item.Source}</td><td>{item.Message}</td></tr>)}</tbody></table></div>}
            {data && <small>Raccolti il {new Date(data.CollectedAt).toLocaleString('it-IT')} · la modifica servizi richiede conferma ed è verificata dal server e dall’Agent.</small>}
          </>}
        </>;
      })()}
    </DevicePanel>}
    {admin && <details className="advanced device-management"><summary><Icon name="settings"/>Configurazione dispositivo</summary><DeviceManagement key={`${device.id}:${device.name}:${device.groupId}:${device.siteId}:${device.revoked}`} device={device} groups={groups} sites={sites} onChanged={onDeviceChanged}/></details>}
  </section>;
}

function UnavailablePanel({ title, detail, icon: Icon }: { title: string; detail: string; icon: typeof Activity }) {
  return <DevicePanel id={title === 'Processi' ? 'processes' : title === 'Servizi Windows' ? 'services' : 'events'}><div className="unavailable-panel"><span className="unavailable-icon"><Icon size={22}/></span><span className="eyebrow">IN SVILUPPO</span><h3>{title}</h3><p>{detail}</p><small>Prima di abilitarla servono endpoint Agent, permessi dedicati e audit lato server.</small></div></DevicePanel>;
}
