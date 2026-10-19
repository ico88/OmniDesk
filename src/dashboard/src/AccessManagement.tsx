import { useEffect, useState } from 'react';
import { api } from './api';

type Account = { id: string; email: string; roles: string[]; isDisabled: boolean };
type Role = { id: string; name: string; permissions: string[] };
type Target = { id: string; name: string };
type Site = Target & { isArchived: boolean; deviceCount: number; ticketCount: number };
type Grant = { id: string; userId: string; roleId: string; groupId: string | null; deviceId: string | null; siteId: string | null };
type SupportQueue = { id: string; name: string; siteId: string | null; siteName: string | null; isActive: boolean; version: number; ticketCount: number };
type QueueMember = { id: string; email: string; isDisabled: boolean };
type QueueRoutingRule = { id: string; siteId: string | null; siteName: string | null; category: string; queueId: string; queueName: string | null; version: number };
const ticketCategories = [['general','Generale'],['hardware','Hardware'],['software','Software'],['network','Rete'],['account','Account'],['other','Altro']];

export function AccessManagement({ currentUserId }: { currentUserId: string }) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [groups, setGroups] = useState<Target[]>([]);
  const [devices, setDevices] = useState<Target[]>([]);
  const [sites, setSites] = useState<Site[]>([]);
  const [grants, setGrants] = useState<Grant[]>([]);
  const [supportQueues, setSupportQueues] = useState<SupportQueue[]>([]);
  const [routingRules, setRoutingRules] = useState<QueueRoutingRule[]>([]);
  const [routingSite, setRoutingSite] = useState('');
  const [selectedQueueId, setSelectedQueueId] = useState('');
  const [selectedQueueVersion, setSelectedQueueVersion] = useState(0);
  const [queueMemberIds, setQueueMemberIds] = useState<string[]>([]);
  const [scope, setScope] = useState('group');
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [selectedId, setSelectedId] = useState('');
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  async function load() {
    const [a, r, g, d, s, locations, queues, routing] = await Promise.all([
      api<Account[]>('/admin/users'), api<Role[]>('/admin/roles'), api<Target[]>('/admin/groups'),
      api<Target[]>('/devices'), api<Grant[]>('/admin/grants'), api<Site[]>('/admin/sites'), api<SupportQueue[]>('/admin/support-queues'), api<QueueRoutingRule[]>('/admin/support-routing'),
    ]);
    setAccounts(a); setRoles(r); setGroups(g); setDevices(d); setGrants(s); setSites(locations); setSupportQueues(queues);
    setRoutingRules(routing);
  }
  async function run(work: () => Promise<void>) {
    setBusy(true); setError(''); setMessage('');
    try { await work(); } catch (e) { setError(e instanceof Error ? e.message : 'Errore inatteso'); }
    finally { setBusy(false); }
  }
  useEffect(() => { void run(load); }, []);
  const targets = scope === 'group' ? groups : scope === 'device' ? devices : sites.filter(s => !s.isArchived);
  const selected = accounts.find(a => a.id === selectedId);
  return <>
    {error && <p role="alert" className="error">{error}</p>}
    {message && <p role="status">{message}</p>}
    <section className="panel"><h2>Account e ruoli globali</h2>
      <button disabled={busy} onClick={() => void run(load)}>Aggiorna utenti e concessioni</button>
      <div className="table-wrap"><table><thead><tr><th>Email</th><th>Ruoli globali</th><th>Stato</th><th>Azioni</th></tr></thead>
        <tbody>{accounts.map(a => <tr key={a.id}><td>{a.email}</td><td>{a.roles.join(', ') || 'Nessuno'}</td><td>{a.isDisabled ? 'Sospeso' : 'Attivo'}</td><td><button disabled={busy} onClick={() => { setSelectedId(a.id); setSelectedRoles([...a.roles]); }}>Gestisci account</button></td></tr>)}</tbody></table></div>
    </section>
    {selected && <section className="panel settings"><h2>Gestisci {selected.email}</h2>
      <p>La sospensione blocca accesso e nuove operazioni, invalida le sessioni web e richiede la chiusura delle sessioni remote. La riattivazione richiede un nuovo login. Deve rimanere almeno un amministratore attivo.</p>
      <button disabled={busy || selected.id === currentUserId} onClick={() => {
        if (window.confirm(`${selected.isDisabled ? 'Riattivare' : 'Sospendere'} ${selected.email}?`)) void run(async () => {
          await api(`/admin/users/${selected.id}/status`, 'PUT', { isDisabled: !selected.isDisabled });
          await load(); setMessage(selected.isDisabled ? 'Account riattivato' : 'Account sospeso');
        });
      }}>{selected.isDisabled ? 'Riattiva account' : 'Sospendi account'}</button>
      <button disabled={busy} onClick={() => {
        if (window.confirm(`Revocare tutte le sessioni web di ${selected.email}? Le sessioni remote sono gestite separatamente.`)) void run(async () => {
          await api(`/admin/users/${selected.id}/revoke-sessions`, 'POST');
          if (selected.id === currentUserId) window.location.reload(); else setMessage('Sessioni web revocate');
        });
      }}>Revoca sessioni web</button>
      <form onSubmit={e => { e.preventDefault(); if (window.confirm(`Salvare i ruoli globali di ${selected.email}? Le sessioni web saranno invalidate.`)) void run(async () => {
        await api(`/admin/users/${selected.id}/roles`, 'PUT', { roles: selectedRoles });
        if (selected.id === currentUserId) window.location.reload(); else { await load(); setMessage('Ruoli globali aggiornati'); }
      }); }}><h3>Ruoli globali dell’account</h3>
        {roles.map(r => <label key={r.id}><input type="checkbox" style={{width:'auto'}} disabled={busy} checked={selectedRoles.includes(r.name)} onChange={e => setSelectedRoles(e.target.checked ? [...selectedRoles,r.name] : selectedRoles.filter(n => n !== r.name))}/>{r.name}</label>)}
        <p>Le concessioni locali restano attive. I permessi globali si applicano a tutti i dispositivi.</p><button disabled={busy}>Salva ruoli globali</button>
      </form>
    </section>}
    <form className="panel settings" onSubmit={e => {
      e.preventDefault(); const form = e.currentTarget; const values = new FormData(form);
      void run(async () => { await api('/admin/groups', 'POST', { name: values.get('name') }); form.reset(); await load(); setMessage('Gruppo creato'); });
    }}><h2>Crea un gruppo dispositivi</h2><label>Nome gruppo<input name="name" required maxLength={200}/></label><button disabled={busy}>Crea gruppo</button></form>
    <section className="panel"><h2>Sedi operative</h2><p>Le sedi sono un ambito distinto dai gruppi; i ticket conservano la sede storica del dispositivo quando vengono aperti.</p>
      <form className="settings" onSubmit={e => { e.preventDefault(); const form = e.currentTarget; const values = new FormData(form); void run(async () => { await api('/admin/sites', 'POST', { name: values.get('name') }); form.reset(); await load(); setMessage('Sede creata'); }); }}><label>Nuova sede<input name="name" required maxLength={200}/></label><button disabled={busy}>Crea sede</button></form>
      <div className="table-wrap"><table><thead><tr><th>Sede</th><th>Dispositivi</th><th>Ticket storici</th><th>Stato</th><th></th></tr></thead><tbody>{sites.map(site => <tr key={site.id}><td>{site.name}</td><td>{site.deviceCount}</td><td>{site.ticketCount}</td><td>{site.isArchived ? 'Archiviata' : 'Attiva'}</td><td>{!site.isArchived && <button className="secondary compact" disabled={busy || site.deviceCount > 0} title={site.deviceCount > 0 ? 'Sposta prima tutti i dispositivi' : undefined} onClick={() => void run(async () => { await api(`/admin/sites/${site.id}`, 'PUT', { name: site.name, expectedName: site.name, isArchived: true }); await load(); setMessage('Sede archiviata'); })}>Archivia</button>}</td></tr>)}</tbody></table></div>
    </section>
    <section className="panel"><h2>Code di assistenza</h2><p>Le code organizzano il lavoro senza cambiare i permessi di visibilità del ticket. Disattivare una coda non rimuove le assegnazioni storiche.</p>
      <form className="settings" onSubmit={e => { e.preventDefault(); const form = e.currentTarget; const values = new FormData(form); void run(async () => { await api('/admin/support-queues', 'POST', { name: values.get('name'), siteId: values.get('site') || null }); form.reset(); await load(); setMessage('Coda creata'); }); }}><label>Nuova coda<input name="name" required maxLength={100}/></label><label>Sede<select name="site" defaultValue=""><option value="">Globale</option>{sites.filter(s=>!s.isArchived).map(s=><option key={s.id} value={s.id}>{s.name}</option>)}</select></label><button disabled={busy}>Crea coda</button></form>
      <div className="table-wrap"><table><thead><tr><th>Coda</th><th>Ambito</th><th>Ticket</th><th>Stato</th><th>Azioni</th></tr></thead><tbody>{supportQueues.map(queue => <tr key={queue.id}><td>{queue.name}</td><td>{queue.siteName||'Globale'}</td><td>{queue.ticketCount}</td><td>{queue.isActive ? 'Attiva' : 'Disattivata'}</td><td><button className="secondary compact" disabled={busy||!queue.isActive} onClick={() => void run(async () => { const data=await api<{version:number;members:QueueMember[]}>(`/admin/support-queues/${queue.id}/members`);setQueueMemberIds(data.members.filter(m=>!m.isDisabled).map(m=>m.id));setSelectedQueueVersion(data.version);setSelectedQueueId(queue.id); })}>Gestisci membri</button> <button className="secondary compact" disabled={busy} onClick={() => void run(async () => { await api(`/admin/support-queues/${queue.id}`, 'PUT', { name: queue.name, expectedName: queue.name, expectedVersion:queue.version, isActive: !queue.isActive }); await load(); setMessage(queue.isActive ? 'Coda disattivata' : 'Coda riattivata'); })}>{queue.isActive ? 'Disattiva' : 'Riattiva'}</button></td></tr>)}</tbody></table></div>
      {selectedQueueId&&<form className="settings" onSubmit={e=>{e.preventDefault();const queue=supportQueues.find(x=>x.id===selectedQueueId);if(!queue)return;void run(async()=>{await api(`/admin/support-queues/${queue.id}/members`,'PUT',{expectedVersion:selectedQueueVersion,userIds:queueMemberIds});await load();setSelectedQueueId('');setQueueMemberIds([]);setMessage('Membri della coda aggiornati');});}}><h3>Membri della coda {supportQueues.find(x=>x.id===selectedQueueId)?.name}</h3><p>L’appartenenza organizza il lavoro e limita gli assegnatari proposti; non concede accesso ai ticket o ai dispositivi.</p>{accounts.filter(a=>!a.isDisabled).map(a=><label key={a.id}><input type="checkbox" style={{width:'auto'}} disabled={busy} checked={queueMemberIds.includes(a.id)} onChange={e=>setQueueMemberIds(ids=>e.target.checked?[...ids,a.id]:ids.filter(id=>id!==a.id))}/>{a.email}</label>)}<button disabled={busy}>Salva membri</button><button className="secondary" type="button" disabled={busy} onClick={()=>{setSelectedQueueId('');setQueueMemberIds([]);}}>Annulla</button></form>}
    </section>
    <section className="panel"><h2>Routing automatico ticket</h2><p>Le nuove richieste ricevono una coda in base alla categoria. La regola specifica per sede prevale su quella globale; il routing non assegna operatori e non modifica i permessi.</p>
      <form className="settings" onSubmit={e=>{e.preventDefault();const form=e.currentTarget;const values=new FormData(form);void run(async()=>{await api('/admin/support-routing','POST',{siteId:values.get('site')||null,category:values.get('category'),queueId:values.get('queue')});form.reset();setRoutingSite('');await load();setMessage('Regola di routing creata');});}}>
        <label>Sede<select name="site" value={routingSite} onChange={e=>setRoutingSite(e.target.value)}><option value="">Tutte le sedi (regola globale)</option>{sites.filter(s=>!s.isArchived).map(s=><option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <label>Categoria<select name="category">{ticketCategories.map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label>
        <label>Coda<select name="queue" required>{supportQueues.filter(q=>q.isActive&&(q.siteId===null||q.siteId===routingSite||!routingSite&&q.siteId===null)).map(q=><option key={q.id} value={q.id}>{q.name}{q.siteName?` · ${q.siteName}`:''}</option>)}</select></label>
        <button disabled={busy||!supportQueues.some(q=>q.isActive&&(q.siteId===null||q.siteId===routingSite||!routingSite&&q.siteId===null))}>Aggiungi regola</button>
      </form>
      <div className="table-wrap"><table><thead><tr><th>Sede</th><th>Categoria</th><th>Coda di destinazione</th><th></th></tr></thead><tbody>{routingRules.map(rule=><tr key={rule.id}><td>{rule.siteName||'Tutte le sedi'}</td><td>{ticketCategories.find(([value])=>value===rule.category)?.[1]||rule.category}</td><td>{rule.queueName||'Coda rimossa'}</td><td><button className="secondary compact" disabled={busy} onClick={()=>void run(async()=>{await api(`/admin/support-routing/${rule.id}`,'DELETE',{expectedVersion:rule.version});await load();setMessage('Regola rimossa');})}>Rimuovi</button></td></tr>)}</tbody></table></div>
    </section>
    <form className="panel settings" onSubmit={e => {
      e.preventDefault(); const form = e.currentTarget; const values = new FormData(form);
      void run(async () => {
        await api('/admin/users', 'POST', { email: values.get('email'), password: values.get('password'), role: values.get('role') || null });
        form.reset(); await load(); setMessage('Utente creato');
      });
    }}><h2>Crea un account</h2>
      <label>Email<input type="email" name="email" required maxLength={254}/></label>
      <label>Password iniziale<input name="password" type="password" required minLength={14} maxLength={1024} autoComplete="new-password"/></label>
      <p>Minimo 14 caratteri, maiuscole, minuscole, numero e simbolo. Consegnare tramite un canale riservato.</p>
      <label>Ruolo globale<select aria-label="Ruolo globale" name="role"><option value="">Nessuno (solo concessioni locali)</option>{roles.map(r => <option key={r.id}>{r.name}</option>)}</select></label>
      <button disabled={busy}>Crea utente</button>
    </form>
    <form className="panel settings" onSubmit={e => {
      e.preventDefault(); const values = new FormData(e.currentTarget);
      void run(async () => {
        await api('/admin/grants', 'POST', { userId: values.get('user'), role: values.get('role'), groupId: scope === 'group' ? values.get('target') : null, deviceId: scope === 'device' ? values.get('target') : null, siteId: scope === 'site' ? values.get('target') : null });
        await load(); setMessage('Concessione assegnata');
      });
    }}><h2>Assegna una concessione</h2><p>Le concessioni si aggiungono ai ruoli globali. La revoca di una concessione non rimuove gli altri accessi.</p>
      <label>Utente destinatario<select aria-label="Utente destinatario" name="user" required>{accounts.map(a => <option key={a.id} value={a.id}>{a.email}</option>)}</select></label>
      <label>Ruolo della concessione<select aria-label="Ruolo della concessione" name="role" required>{roles.map(r => <option key={r.id} value={r.name}>{r.name} ({r.permissions.join(', ')})</option>)}</select></label>
      <label>Ambito<select aria-label="Ambito" value={scope} onChange={e => setScope(e.target.value)}><option value="group">Gruppo</option><option value="device">Dispositivo</option><option value="site">Sede</option></select></label>
      <label>Destinazione<select aria-label="Destinazione" key={scope} name="target" required>{targets.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
      {targets.length === 0 && <p>{scope === 'group' ? 'Nessun gruppo disponibile.' : scope === 'device' ? 'Nessun dispositivo visibile disponibile.' : 'Nessuna sede attiva disponibile.'}</p>}
      <button disabled={busy || !targets.length || !accounts.length || !roles.length}>Assegna concessione</button>
    </form>
    <section className="panel"><h2>Concessioni attive</h2>{grants.length === 0 ? <p>Nessuna concessione locale.</p> : <div className="table-wrap"><table><thead><tr><th>Utente</th><th>Ruolo</th><th>Ambito</th><th>Azioni</th></tr></thead><tbody>{grants.map(g => <tr key={g.id}>
      <td>{accounts.find(a => a.id === g.userId)?.email ?? g.userId}</td><td>{roles.find(r => r.id === g.roleId)?.name ?? g.roleId}</td>
      <td>{g.groupId ? `Gruppo: ${groups.find(t => t.id === g.groupId)?.name ?? g.groupId}` : g.deviceId ? `Dispositivo: ${devices.find(t => t.id === g.deviceId)?.name ?? g.deviceId}` : `Sede: ${sites.find(t => t.id === g.siteId)?.name ?? g.siteId}`}</td>
      <td><button disabled={busy} onClick={() => { if (window.confirm('Revocare questa concessione? Gli altri accessi resteranno validi.')) void run(async () => { await api(`/admin/grants/${g.id}`, 'DELETE'); await load(); setMessage('Concessione revocata'); }); }}>Revoca concessione</button></td>
    </tr>)}</tbody></table></div>}</section>
  </>;
}
