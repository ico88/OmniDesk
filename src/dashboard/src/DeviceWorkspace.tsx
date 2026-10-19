import { useEffect, useState } from 'react';
import { Activity, AlertTriangle, Cpu, HardDrive, MemoryStick, Monitor, Network, Server, ShieldCheck, Timer, Wrench } from 'lucide-react';
import { api } from './api';
import { DevicePanel, DeviceTabs, type DeviceTabId } from './DeviceTabs';
import { DeviceManagement } from './DeviceManagement';
import { RemoteConsole } from './RemoteConsole';
import { RemoteSessions } from './RemoteSessions';
import { SupportDesk } from './SupportDesk';
import { Icon } from './Icon';

type MetricDisk = { name: string; totalBytes: number; freeBytes: number };
export type DeviceStatus = {
  id: string; name: string; groupId: string | null; siteId: string | null; capabilities: string[]; online: boolean; revoked: boolean; lastSeen: string | null;
  metrics: { cpuPercent: number | null; totalMemoryBytes: number; availableMemoryBytes: number; networkReceivedBytes: number; networkSentBytes: number; uptimeSeconds: number; disks: MetricDisk[]; agentVersion?: string | null } | null;
  inventory: { operatingSystem: string; architecture: string; manufacturer: string | null; model: string | null; processor: string | null; logicalProcessors: number; software: { name: string; version: string | null; publisher: string | null }[]; warnings: string[] } | null;
};
type Group = { id: string; name: string };
type UpdateItem = { UpdateId: string; Revision: number; Title: string; Kb: string[]; ResultCode: number | null };
type UpdateJob = { id: string; kind: string; state: string; createdAt: string; result: string | null };
type DiagnosticJob = UpdateJob & { completedAt: string | null };
type DiagnosticData = { Kind: string; CollectedAt: string; Processes?: { ProcessId: number; Name: string; StartedAt: string }[]; Services?: { Name: string; DisplayName: string; State: string; StartType: string }[]; Events?: { Log: string; TimeCreated: string; Id: number; Level: string; Source: string; Message: string }[] };
type ServiceJob = UpdateJob & { completedAt: string | null; payload?: string };
type WindowsDownload = { available: boolean; release: { version: string; msiSha256?: string | null; msiSize?: number | null } | null };

const jobLabels: Record<string, string> = { queued: 'In coda', leased: 'In esecuzione', succeeded: 'Completato', failed: 'Non riuscito', cancelled: 'Annullato' };
const byteCount = (bytes: number) => `${(bytes / 1073741824).toFixed(1)} GiB`;
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
  const [activeTab, setActiveTab] = useState<DeviceTabId>('overview');
  const [remoteReady, setRemoteReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [jobs, setJobs] = useState<UpdateJob[]>([]);
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

  async function loadJobs() { setJobs(await api<UpdateJob[]>(`/devices/${device.id}/updates`)); setSelectedUpdates([]); }
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
    const loadAvailability = async () => {
      try {
        const state = await api<{ configured: boolean; mapped: boolean; revoked: boolean; enabled: boolean }>(`/devices/${device.id}/remote-sessions/availability`);
        if (active) setRemoteReady(state.configured && state.mapped && !state.revoked && state.enabled);
      } catch { if (active) setRemoteReady(false); }
    };
    void loadAvailability();
    const timer = window.setInterval(() => void loadAvailability(), 15000);
    return () => { active = false; window.clearInterval(timer); };
  }, [device.id]);

  useEffect(() => {
    let active = true;
    if (admin) void api<WindowsDownload>('/admin/downloads/windows').then(value => { if (active) setWindowsRelease(value); }).catch(() => { if (active) setWindowsRelease({ available: false, release: null }); });
    else setWindowsRelease(null);
    return () => { active = false; };
  }, [admin]);

  useEffect(() => {
    if (activeTab === 'updates' && device.capabilities.includes('updates')) void work(loadJobs);
  }, [activeTab, device.id]);

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
  const inventory = device.inventory;
  const count = (capability: string) => device.capabilities.includes(capability);
  const remoteProps = {
    deviceId: device.id,
    deviceName: device.name,
    ready: remoteReady,
    revoked: device.revoked,
    onMessage,
  };

  return <section className="panel device-detail device-workspace">
    <div className="section-heading device-workspace-heading">
      <div><span className="eyebrow">DISPOSITIVO</span><h2><Monitor size={22}/>{device.name}</h2><small>{inventory?.operatingSystem ?? 'Sistema operativo non disponibile'} · Ultimo contatto {device.lastSeen ? new Date(device.lastSeen).toLocaleString('it-IT') : 'mai'}</small></div>
      <span className={`status-pill ${device.revoked ? 'danger' : device.online ? 'online' : 'offline'}`}>{device.revoked ? 'Revocato' : device.online ? 'Online' : 'Offline'}</span>
    </div>
    {admin && <details className="advanced device-management"><summary><Icon name="settings"/>Gestione dispositivo</summary><DeviceManagement key={`${device.id}:${device.name}:${device.groupId}:${device.siteId}:${device.revoked}`} device={device} groups={groups} sites={sites} onChanged={onDeviceChanged}/></details>}
    <DeviceTabs active={activeTab} onChange={tab => { setActiveTab(tab); setError(''); }}/>
    {error && <p className="error" role="alert">{error}</p>}

    {activeTab === 'overview' && <DevicePanel id="overview">
      {metrics ? <div className="metrics-strip device-metrics">
        <article><span><Cpu size={16}/>CPU</span><strong>{metrics.cpuPercent === null ? 'N/D' : `${metrics.cpuPercent.toFixed(1)}%`}</strong><small>Ultimo campione ricevuto</small>{metrics.cpuPercent !== null && <div className="meter"><i style={{ width: `${Math.min(100, Math.max(0, metrics.cpuPercent))}%` }}/></div>}</article>
        <article><span><MemoryStick size={16}/>Memoria disponibile</span><strong>{byteCount(metrics.availableMemoryBytes)}</strong><small>su {byteCount(metrics.totalMemoryBytes)} totali</small><div className="meter"><i style={{ width: `${100 - (metrics.availableMemoryBytes / metrics.totalMemoryBytes * 100)}%` }}/></div></article>
        <article><span><Timer size={16}/>Uptime</span><strong>{Math.floor(metrics.uptimeSeconds / 3600)} <small>ore</small></strong><small>Dall’ultimo avvio del sistema</small></article>
      </div> : <div className="remote-empty-state"><Activity size={20}/>Metriche non ancora ricevute dall’agente.</div>}
      {inventory && <div className="device-summary-grid">
        <article className="device-info-card"><h3><Server size={16}/>Sistema</h3><p>{inventory.operatingSystem} · {inventory.architecture}</p><p>{inventory.manufacturer ?? 'Produttore non rilevato'} {inventory.model ?? ''}</p><small>{inventory.processor ?? 'CPU non rilevata'} · {inventory.logicalProcessors} processori logici</small></article>
        <article className="device-info-card"><h3><HardDrive size={16}/>Archiviazione</h3>{metrics?.disks.length ? metrics.disks.map(d => <div className="disk-row" key={d.name}><span>{d.name}</span><strong>{byteCount(d.freeBytes)} liberi</strong><small>di {byteCount(d.totalBytes)}</small><div className="meter"><i style={{ width: `${Math.min(100, Math.max(0, (d.totalBytes - d.freeBytes) / d.totalBytes * 100))}%` }}/></div></div>) : <p>Dati disco non disponibili.</p>}</article>
        <article className="device-info-card"><h3><Network size={16}/>Rete</h3>{metrics ? <><p>{byteCount(metrics.networkReceivedBytes)} ricevuti</p><p>{byteCount(metrics.networkSentBytes)} inviati</p><small>Contatori cumulativi dall’avvio dell’agente</small></> : <p>Dati rete non disponibili.</p>}</article>
      </div>}
      {!!inventory?.warnings.length && <div className="device-warning-list">{inventory.warnings.map((warning, index) => <p key={index}><AlertTriangle size={15}/>{warning}</p>)}</div>}
      {inventory && <div className="device-summary-footer"><span>Software rilevato</span><strong>{inventory.software.length}</strong><button className="secondary compact" onClick={() => setActiveTab('software')}>Apri inventario <Icon name="arrow" size={14}/></button></div>}
    </DevicePanel>}

    {activeTab === 'desktop' && <DevicePanel id="desktop"><div className="remote-workspace">
      <div className="remote-workspace-main">
        <RemoteSessions key={device.id} deviceId={device.id} admin={admin} onAvailability={setRemoteReady}/>
        <RemoteConsole {...remoteProps} capability="remote.control" allowed={count('remote.control')}/>
      </div>
      <aside className="remote-device-sidebar"><div className="device-sidebar-heading"><span className="eyebrow">DETTAGLI E DIAGNOSTICA</span><ShieldCheck size={18}/></div>
        <div className="sidebar-status"><span className={`connection-indicator ${device.online ? 'ready' : ''}`}/><strong>{device.online ? 'Agent online' : 'Agent offline'}</strong><small>{device.lastSeen ? `Ultimo heartbeat ${new Date(device.lastSeen).toLocaleTimeString('it-IT')}` : 'Nessun heartbeat ricevuto'}</small></div>
        <dl>{[['Sistema operativo', inventory?.operatingSystem], ['Modello', [inventory?.manufacturer, inventory?.model].filter(Boolean).join(' ')], ['CPU', inventory?.processor], ['Memoria', metrics ? `${byteCount(metrics.availableMemoryBytes)} liberi` : undefined]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value || 'Dato non disponibile'}</dd></div>)}</dl>
        <p className="sidebar-note">Lo stato online dell’Agent non equivale allo stato della connessione desktop MeshCentral.</p>
      </aside>
    </div></DevicePanel>}

    {activeTab === 'terminal' && <DevicePanel id="terminal"><RemoteConsole {...remoteProps} capability="terminal" allowed={count('terminal')}/></DevicePanel>}
    {activeTab === 'files' && <DevicePanel id="files"><RemoteConsole {...remoteProps} capability="files" allowed={count('files')}/></DevicePanel>}

    {activeTab === 'software' && <DevicePanel id="software"><div className="section-heading"><div><span className="eyebrow">INVENTARIO</span><h3>Software installato</h3></div><span className="count">{inventory?.software.length ?? 0}</span></div>
      {!inventory ? <p>Inventario non ancora ricevuto dall’agente.</p> : <div className="table-wrap"><table><thead><tr><th>Software</th><th>Versione</th><th>Produttore</th></tr></thead><tbody>{inventory.software.map((app, index) => <tr key={`${app.name}-${index}`}><td>{app.name}</td><td>{app.version || '—'}</td><td>{app.publisher || '—'}</td></tr>)}</tbody></table></div>}
    </DevicePanel>}

    {activeTab === 'updates' && <DevicePanel id="updates"><div className="section-heading"><div><span className="eyebrow">GESTIONE</span><h3>Aggiornamenti Windows</h3></div><button className="secondary compact" disabled={busy || !count('updates')} onClick={() => void work(loadJobs)}><Icon name="refresh" size={15}/>Aggiorna</button></div>
      {!count('updates') ? <p>Non hai il permesso per gestire gli aggiornamenti di questo dispositivo.</p> : <>
        <article className="update-job-card"><div><strong>OmniDesk Agent</strong><span>Installato: {metrics?.agentVersion ?? 'versione non rilevata'}</span></div>{admin ? <><small>{!versionAtLeast(metrics?.agentVersion, '0.8.1') ? 'Questa versione non supporta l’aggiornamento remoto: installa manualmente l’MSI 0.8.1 una volta.' : windowsRelease?.available && windowsRelease.release?.msiSha256 ? `Release disponibile: ${windowsRelease.release.version}` : 'Release MSI verificata non disponibile sul server'}</small><button disabled={busy || device.revoked || !device.online || !versionAtLeast(metrics?.agentVersion, '0.8.1') || !windowsRelease?.available || !windowsRelease.release?.msiSha256 || !windowsRelease.release?.msiSize || !windowsRelease.release.version || versionAtLeast(metrics?.agentVersion, windowsRelease.release.version)} onClick={() => {
          const target = windowsRelease?.release?.version;
          if (!target || !window.confirm(`Aggiornare OmniDesk Agent a ${target}? Il servizio si arresterà e ripartirà durante l’installazione. Non è previsto un riavvio di Windows.`)) return;
          void work(async () => { await api(`/admin/devices/${device.id}/agent-update`, 'POST', { requestId: crypto.randomUUID() }); await loadJobs(); });
        }}>Aggiorna Agent</button></> : <small>Solo un amministratore globale può aggiornare il software Agent.</small>}</article>
        <div className="action-row"><button disabled={busy || device.revoked} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/scan`, 'POST', { requestId: crypto.randomUUID() }); await loadJobs(); })}>Avvia scansione</button><p>I lavori vengono eseguiti dall’agente. Nessun riavvio automatico.</p></div>
        {!jobs.length && <p>Nessun lavoro di aggiornamento registrato.</p>}
        {jobs.map(job => <article className="update-job-card" key={job.id}><div><strong>{job.kind === 'scan' ? 'Scansione' : job.kind === 'install' ? 'Installazione aggiornamenti' : 'Aggiornamento Agent'}</strong><span className={`status-pill ${job.state === 'succeeded' ? 'online' : job.state === 'failed' ? 'danger' : 'offline'}`}>{jobLabels[job.state] ?? job.state}</span></div><small>{new Date(job.createdAt).toLocaleString('it-IT')}</small>
          {job.state === 'queued' && <button className="secondary compact" disabled={busy} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/${job.id}`, 'DELETE'); await loadJobs(); })}>Annulla</button>}
          {job.result && (() => { try { const result = JSON.parse(job.result) as { Succeeded: boolean; RebootRequired: boolean; Error: string | null; Updates: UpdateItem[] }; return <div className="update-result"><p>{result.Succeeded ? 'Operazione completata' : 'Operazione non riuscita'}{result.RebootRequired ? ' · Riavvio richiesto' : ''}</p>{result.Error && <details><summary>Dettagli errore</summary><code>{result.Error}</code></details>}{job.kind === 'scan' && result.Succeeded && result.Updates.map(item => <label className="update-choice" key={`${item.UpdateId}-${item.Revision}`}><input type="checkbox" checked={selectedUpdates.some(x => x.UpdateId === item.UpdateId)} onChange={event => setSelectedUpdates(event.target.checked ? [...selectedUpdates.filter(x => x.UpdateId !== item.UpdateId), item] : selectedUpdates.filter(x => x.UpdateId !== item.UpdateId))}/>{item.Title} · {item.Kb.join(', ')}</label>)}</div>; } catch { return <p>Risultato non leggibile; richiedi di nuovo l’elenco lavori.</p>; } })()}
        </article>)}
        {!!selectedUpdates.length && <button disabled={busy || device.revoked} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/install`, 'POST', { requestId: crypto.randomUUID(), updates: selectedUpdates.map(item => ({ updateId: item.UpdateId, revision: item.Revision })) }); setSelectedUpdates([]); await loadJobs(); })}>Installa {selectedUpdates.length} aggiornamenti selezionati</button>}
      </>}
    </DevicePanel>}

    {activeTab === 'tickets' && <DevicePanel id="tickets">{count('tickets.view') ? <SupportDesk key={device.id} sites={sites} deviceId={device.id} visibleDeviceIds={[]} onOpenDevice={async () => undefined}/> : <p>Non hai il permesso tickets.view su questo dispositivo.</p>}</DevicePanel>}

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
              <div className="table-wrap"><table><thead><tr><th>PID</th><th>Processo</th><th>Avvio</th><th>Azioni</th></tr></thead><tbody>{(data?.Processes ?? []).map((item, i) => <tr key={`${item.ProcessId}-${i}`}><td>{item.ProcessId}</td><td>{item.Name}</td><td>{new Date(item.StartedAt).toLocaleString('it-IT')}</td><td>{count('processes.manage') && <button className="secondary compact" disabled={busy || device.revoked || !device.online} onClick={() => void requestProcessKill(item.ProcessId, item.Name, item.StartedAt)}>Termina…</button>}</td></tr>)}</tbody></table></div>
              {count('processes.manage') && <div className="service-action-history"><h4>Interventi sui processi</h4>{!processJobs.length && <p className="muted">Nessuna azione registrata.</p>}{processJobs.map(job => {
                let result: { Succeeded?: boolean; Error?: string | null } | null = null;
                try { result = job.result ? JSON.parse(job.result) as { Succeeded?: boolean; Error?: string | null } : null; } catch { result = null; }
                let target = 'Processo selezionato';
                try { const payload = JSON.parse(job.payload ?? '{}') as { ProcessId?: number; ProcessName?: string }; target = `${payload.ProcessName ?? 'Processo'} · PID ${payload.ProcessId ?? '?'}`; } catch { /* corrupted legacy result */ }
                return <article className="update-job-card" key={job.id}><div><strong>Terminazione · {job.id.slice(0, 8)}</strong><span className={`status-pill ${job.state === 'succeeded' ? 'online' : job.state === 'failed' ? 'danger' : 'offline'}`}>{jobLabels[job.state] ?? job.state}</span></div><small>{new Date(job.createdAt).toLocaleString('it-IT')}</small><small>{target}</small>{result?.Error && <small>{result.Error}</small>}</article>;
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
  </section>;
}

function UnavailablePanel({ title, detail, icon: Icon }: { title: string; detail: string; icon: typeof Activity }) {
  return <DevicePanel id={title === 'Processi' ? 'processes' : title === 'Servizi Windows' ? 'services' : 'events'}><div className="unavailable-panel"><span className="unavailable-icon"><Icon size={22}/></span><span className="eyebrow">IN SVILUPPO</span><h3>{title}</h3><p>{detail}</p><small>Prima di abilitarla servono endpoint Agent, permessi dedicati e audit lato server.</small></div></DevicePanel>;
}
