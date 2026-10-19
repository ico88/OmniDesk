import { useState } from 'react';
import { api } from './api';

type Device = { id: string; name: string; groupId: string | null; siteId: string | null; revoked: boolean };
export function DeviceManagement({ device, groups, sites, onChanged }: { device: Device; groups: { id: string; name: string }[]; sites: { id: string; name: string; isArchived?: boolean }[]; onChanged: (message: string) => Promise<void> }) {
  const [name, setName] = useState(device.name);
  const [groupId, setGroupId] = useState(device.groupId ?? '');
  const [siteId, setSiteId] = useState(device.siteId ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function run(work: () => Promise<void>) {
    setBusy(true); setError('');
    try { await work(); } catch (e) { setError(e instanceof Error ? e.message : 'Errore inatteso'); }
    finally { setBusy(false); }
  }
  return <section className="panel settings"><h2>Gestione dispositivo</h2>
    {error && <p role="alert" className="error">{error}</p>}
    <form onSubmit={e => {
      e.preventDefault();
      if (((groupId || null) !== device.groupId || siteId !== device.siteId) && !window.confirm('Spostare il dispositivo? Le concessioni del nuovo ambito saranno applicate, le sessioni remote saranno revocate e i job in coda non più autorizzati saranno annullati. I ticket conservano la sede storica.')) return;
      void run(async () => {
        await api(`/admin/devices/${device.id}`, 'PUT', { name, groupId: groupId || null, expectedName: device.name, expectedGroupId: device.groupId, siteId, expectedSiteId: device.siteId });
        await onChanged('Dispositivo aggiornato');
      });
    }}><label>Nome dispositivo<input value={name} onChange={e => setName(e.target.value)} required maxLength={200} disabled={busy}/></label>
      <label>Gruppo dispositivo<select aria-label="Gruppo dispositivo" value={groupId} onChange={e => setGroupId(e.target.value)} disabled={busy}><option value="">Senza gruppo</option>{groups.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label>
      <label>Sede<select aria-label="Sede dispositivo" value={siteId} onChange={e => setSiteId(e.target.value)} disabled={busy} required>{sites.filter(s => !s.isArchived || s.id === device.siteId).map(s => <option key={s.id} value={s.id}>{s.name}{s.isArchived ? ' (archiviata)' : ''}</option>)}</select></label>
      <p>Il nome è l’etichetta nella console e non cambia il nome del computer Windows. Gruppo e sede determinano concessioni distinte.</p>
      <button disabled={busy}>Salva dispositivo</button>
    </form>
    <p>La revoca blocca le credenziali dell’agente, annulla i job in coda e richiede la chiusura delle sessioni remote. Non disinstalla il servizio e non interrompe aggiornamenti già avviati.</p>
    <button disabled={busy || device.revoked} onClick={() => {
      if (window.confirm(`Revocare definitivamente l’identità di ${device.name}? Per registrarlo nuovamente servirà un nuovo enrollment.`)) void run(async () => {
        await api(`/admin/devices/${device.id}/revoke`, 'POST');
        await onChanged('Identità dispositivo revocata');
      });
    }}>{device.revoked ? 'Identità già revocata' : 'Revoca identità dispositivo'}</button>
  </section>;
}
