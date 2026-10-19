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
  id: string; name: string; groupId: string | null; capabilities: string[]; online: boolean; revoked: boolean; lastSeen: string | null;
  metrics: { cpuPercent: number | null; totalMemoryBytes: number; availableMemoryBytes: number; networkReceivedBytes: number; networkSentBytes: number; uptimeSeconds: number; disks: MetricDisk[] } | null;
  inventory: { operatingSystem: string; architecture: string; manufacturer: string | null; model: string | null; processor: string | null; logicalProcessors: number; software: { name: string; version: string | null; publisher: string | null }[]; warnings: string[] } | null;
};
type Group = { id: string; name: string };
type UpdateItem = { UpdateId: string; Revision: number; Title: string; Kb: string[]; ResultCode: number | null };
type UpdateJob = { id: string; kind: string; state: string; createdAt: string; result: string | null };

const jobLabels: Record<string, string> = { queued: 'In coda', leased: 'In esecuzione', succeeded: 'Completato', failed: 'Non riuscito', cancelled: 'Annullato' };
const byteCount = (bytes: number) => `${(bytes / 1073741824).toFixed(1)} GiB`;

export function DeviceWorkspace({ device, admin, groups, onDeviceChanged, onMessage }: {
  device: DeviceStatus;
  admin: boolean;
  groups: Group[];
  onDeviceChanged: (message: string) => Promise<void>;
  onMessage: (message: string) => void;
}) {
  const [activeTab, setActiveTab] = useState<DeviceTabId>('overview');
  const [remoteReady, setRemoteReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [jobs, setJobs] = useState<UpdateJob[]>([]);
  const [selectedUpdates, setSelectedUpdates] = useState<UpdateItem[]>([]);

  async function work(action: () => Promise<void>) {
    setBusy(true);
    setError('');
    try { await action(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Operazione non riuscita.'); }
    finally { setBusy(false); }
  }

  async function loadJobs() { setJobs(await api<UpdateJob[]>(`/devices/${device.id}/updates`)); setSelectedUpdates([]); }

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
    if (activeTab === 'updates' && device.capabilities.includes('updates')) void work(loadJobs);
  }, [activeTab, device.id]);

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
    {admin && <details className="advanced device-management"><summary><Icon name="settings"/>Gestione dispositivo</summary><DeviceManagement key={`${device.id}:${device.name}:${device.groupId}:${device.revoked}`} device={device} groups={groups} onChanged={onDeviceChanged}/></details>}
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
        <div className="action-row"><button disabled={busy || device.revoked} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/scan`, 'POST', { requestId: crypto.randomUUID() }); await loadJobs(); })}>Avvia scansione</button><p>I lavori vengono eseguiti dall’agente. Nessun riavvio automatico.</p></div>
        {!jobs.length && <p>Nessun lavoro di aggiornamento registrato.</p>}
        {jobs.map(job => <article className="update-job-card" key={job.id}><div><strong>{job.kind === 'scan' ? 'Scansione' : 'Installazione'}</strong><span className={`status-pill ${job.state === 'succeeded' ? 'online' : job.state === 'failed' ? 'danger' : 'offline'}`}>{jobLabels[job.state] ?? job.state}</span></div><small>{new Date(job.createdAt).toLocaleString('it-IT')}</small>
          {job.state === 'queued' && <button className="secondary compact" disabled={busy} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/${job.id}`, 'DELETE'); await loadJobs(); })}>Annulla</button>}
          {job.result && (() => { try { const result = JSON.parse(job.result) as { Succeeded: boolean; RebootRequired: boolean; Error: string | null; Updates: UpdateItem[] }; return <div className="update-result"><p>{result.Succeeded ? 'Operazione completata' : 'Operazione non riuscita'}{result.RebootRequired ? ' · Riavvio richiesto' : ''}</p>{result.Error && <details><summary>Dettagli errore</summary><code>{result.Error}</code></details>}{job.kind === 'scan' && result.Succeeded && result.Updates.map(item => <label className="update-choice" key={`${item.UpdateId}-${item.Revision}`}><input type="checkbox" checked={selectedUpdates.some(x => x.UpdateId === item.UpdateId)} onChange={event => setSelectedUpdates(event.target.checked ? [...selectedUpdates.filter(x => x.UpdateId !== item.UpdateId), item] : selectedUpdates.filter(x => x.UpdateId !== item.UpdateId))}/>{item.Title} · {item.Kb.join(', ')}</label>)}</div>; } catch { return <p>Risultato non leggibile; richiedi di nuovo l’elenco lavori.</p>; } })()}
        </article>)}
        {!!selectedUpdates.length && <button disabled={busy || device.revoked} onClick={() => void work(async () => { await api(`/devices/${device.id}/updates/install`, 'POST', { requestId: crypto.randomUUID(), updates: selectedUpdates.map(item => ({ updateId: item.UpdateId, revision: item.Revision })) }); setSelectedUpdates([]); await loadJobs(); })}>Installa {selectedUpdates.length} aggiornamenti selezionati</button>}
      </>}
    </DevicePanel>}

    {activeTab === 'tickets' && <DevicePanel id="tickets">{count('tickets.view') ? <SupportDesk key={device.id} deviceId={device.id} visibleDeviceIds={[]} onOpenDevice={async () => undefined}/> : <p>Non hai il permesso tickets.view su questo dispositivo.</p>}</DevicePanel>}

    {activeTab === 'processes' && <UnavailablePanel title="Processi" icon={Activity} detail="L’Agent non espone ancora un endpoint autorizzato per elencare processi o terminare processi. Nessun dato viene simulato."/>}
    {activeTab === 'services' && <UnavailablePanel title="Servizi Windows" icon={Wrench} detail="L’Agent non espone ancora un endpoint autorizzato per leggere o modificare i servizi Windows."/>}
    {activeTab === 'events' && <UnavailablePanel title="Eventi e log" icon={Activity} detail="La console mostra audit amministrativo globale; non esiste ancora un flusso dispositivo di eventi Windows autorizzato e auditato."/>}
  </section>;
}

function UnavailablePanel({ title, detail, icon: Icon }: { title: string; detail: string; icon: typeof Activity }) {
  return <DevicePanel id={title === 'Processi' ? 'processes' : title === 'Servizi Windows' ? 'services' : 'events'}><div className="unavailable-panel"><span className="unavailable-icon"><Icon size={22}/></span><span className="eyebrow">IN SVILUPPO</span><h3>{title}</h3><p>{detail}</p><small>Prima di abilitarla servono endpoint Agent, permessi dedicati e audit lato server.</small></div></DevicePanel>;
}
