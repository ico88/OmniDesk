import { useState } from 'react';
import { api } from './api';

type Device = { id: string; name: string; groupId: string | null; revoked: boolean };
export function DeviceManagement({ device, groups, onChanged }: { device: Device; groups: { id: string; name: string }[]; onChanged: (message: string) => Promise<void> }) {
  const [name, setName] = useState(device.name);
  const [groupId, setGroupId] = useState(device.groupId ?? '');
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
      if ((groupId || null) !== device.groupId && !window.confirm('Spostare il dispositivo? Le concessioni del nuovo gruppo saranno applicate, sarà richiesta la revoca delle sessioni remote attive e i job in coda non più autorizzati saranno annullati. Le concessioni dirette al dispositivo restano valide.')) return;
      void run(async () => {
        await api(`/admin/devices/${device.id}`, 'PUT', { name, groupId: groupId || null, expectedName: device.name, expectedGroupId: device.groupId });
        await onChanged('Dispositivo aggiornato');
      });
    }}><label>Nome dispositivo<input value={name} onChange={e => setName(e.target.value)} required maxLength={200} disabled={busy}/></label>
      <label>Gruppo dispositivo<select aria-label="Gruppo dispositivo" value={groupId} onChange={e => setGroupId(e.target.value)} disabled={busy}><option value="">Senza gruppo</option>{groups.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label>
      <p>Il nome è l’etichetta nella console e non cambia il nome del computer Windows. Il gruppo determina le concessioni ereditate.</p>
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
