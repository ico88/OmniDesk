import { useEffect, useMemo, useState } from 'react';
import { Expand, ExternalLink, LoaderCircle, MonitorPlay, RotateCcw, ShieldCheck, Square, UserRoundX, Wifi, X } from 'lucide-react';
import { api } from './api';

type RemoteSession = { id: string; url: string; expiresAt: string };
type ViewState = 'idle' | 'creating' | 'renewing' | 'disconnecting' | 'loading' | 'page-loaded' | 'disconnected' | 'error' | 'expired';

function remaining(expiresAt: string, now: number) {
  const seconds = Math.max(0, Math.floor((new Date(expiresAt).getTime() - now) / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

function secondsRemaining(expiresAt: string, now: number) {
  return Math.floor((new Date(expiresAt).getTime() - now) / 1000);
}

export function RemoteConsole({ deviceId, deviceName, capability, allowed, ready, revoked, onMessage }: {
  deviceId: string;
  deviceName: string;
  capability: 'remote.control' | 'terminal' | 'files';
  allowed: boolean;
  ready: boolean;
  revoked: boolean;
  onMessage: (message: string) => void;
}) {
  const [session, setSession] = useState<RemoteSession | null>(null);
  const [viewState, setViewState] = useState<ViewState>('idle');
  const [error, setError] = useState('');
  const [now, setNow] = useState(Date.now());
  const [frameElement, setFrameElement] = useState<HTMLIFrameElement | null>(null);
  const [frameKey, setFrameKey] = useState(0);
  const isDesktop = capability === 'remote.control';
  const title = isDesktop ? 'Desktop remoto' : capability === 'terminal' ? 'Terminale e PowerShell' : 'Trasferimento file';
  const timeLeft = useMemo(() => session ? remaining(session.expiresAt, now) : null, [session, now]);
  const expired = !!session && new Date(session.expiresAt).getTime() <= now;

  useEffect(() => {
    if (!session) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [session]);

  useEffect(() => {
    if (!session || viewState !== 'loading') return;
    const timer = window.setTimeout(() => {
      setViewState(current => current === 'loading' ? 'error' : current);
      setError('La sessione è autorizzata, ma MeshCentral non ha completato il caricamento. Verifica il motore remoto sul dispositivo o apri la sessione in una nuova scheda.');
    }, 20000);
    return () => window.clearTimeout(timer);
  }, [session, viewState]);

  useEffect(() => {
    if (expired) setViewState('expired');
  }, [expired]);

  async function start() {
    const previous = session;
    const renewing = !!previous && new Date(previous.expiresAt).getTime() > Date.now();
    setError('');
    setViewState(renewing ? 'renewing' : 'creating');
    try {
      const created = await api<RemoteSession>(`/devices/${deviceId}/remote-sessions`, 'POST', { capability });
      let valid = false;
      try {
        const target = new URL(created.url);
        // The API creates scoped MeshCentral guest links on /sharing; reject other destinations.
        valid = target.protocol === 'https:' && target.pathname === '/sharing' && !target.username && !target.password &&
          Number.isFinite(Date.parse(created.expiresAt)) && Date.parse(created.expiresAt) > Date.now();
      } catch { /* Invalid URL. */ }
      if (!valid) {
        // Creation already happened server-side; revoke the short-lived share instead of leaking it.
        try { await api(`/devices/${deviceId}/remote-sessions/${created.id}`, 'DELETE'); } catch { /* expires server-side */ }
        throw new Error('Il server ha restituito un collegamento remoto non valido.');
      }
      // Keep the operator in this console. The API rechecks the same capability,
      // device policy and attended consent before issuing the replacement grant.
      // Revoke the old grant before switching the iframe; if revocation is pending,
      // the server guard continues retrying it and its original expiry remains bounded.
      let revokePending = false;
      if (previous) {
        try {
          const revoked = await api<{ state?: string }>(`/devices/${deviceId}/remote-sessions/${previous.id}`, 'DELETE');
          revokePending = revoked?.state === 'revocation_pending';
        } catch (cause) {
          // Do not leave an unused replacement share active when the old grant
          // could not be closed and the UI therefore cannot switch to it safely.
          try { await api(`/devices/${deviceId}/remote-sessions/${created.id}`, 'DELETE'); } catch { /* server expiry/guard remains the fallback */ }
          throw cause;
        }
      }
      setSession(created);
      setNow(Date.now());
      setViewState('loading');
      if (renewing) onMessage(revokePending
        ? 'Sessione rinnovata. La revoca del collegamento precedente è in verifica.'
        : 'Sessione rinnovata. Il desktop remoto si sta ricollegando.');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Non è stato possibile creare la sessione.');
      setViewState(renewing ? 'page-loaded' : 'error');
    }
  }

  async function stop() {
    if (!session) return;
    setViewState('creating');
    try {
      const result = await api<{ state: string } | undefined>(`/devices/${deviceId}/remote-sessions/${session.id}`, 'DELETE');
      onMessage(result?.state === 'revocation_pending'
        ? 'Chiusura richiesta; il server continuerà a verificare la revoca.'
        : 'Sessione remota revocata.');
      setSession(null);
      setViewState('idle');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Non è stato possibile revocare la sessione.');
      setViewState('error');
    }
  }

  async function disconnectOperator() {
    if (!session || !window.confirm('Disconnettere l’operatore dal desktop? Il collegamento resterà valido fino alla scadenza e potrà essere riutilizzato.')) return;
    setError('');
    setViewState('disconnecting');
    try {
      await api(`/devices/${deviceId}/remote-sessions/${session.id}/disconnect`, 'POST');
      setViewState('disconnected');
      onMessage('Disconnessione richiesta. Il collegamento resta valido fino alla scadenza e può essere riutilizzato.');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Non è stato possibile disconnettere l’operatore.');
      setViewState('page-loaded');
    }
  }

  function reconnect() {
    if (!session || expired) return;
    setError('');
    setViewState('loading');
    setFrameKey(value => value + 1);
  }

  async function fullscreen() {
    if (!frameElement) return;
    try {
      await frameElement.requestFullscreen();
    } catch {
      setError('Il browser non ha consentito la modalità a schermo intero.');
    }
  }

  const active = !!session && !expired;
  return <section className="remote-console" aria-label={title}>
    <div className="remote-console-toolbar">
      <div className="remote-console-title">
        <span className={`connection-indicator ${active ? 'ready' : ''}`} aria-hidden="true" />
        <div><strong>{deviceName}</strong><small>{title} · {viewState === 'page-loaded' ? 'Interfaccia remota caricata' : active ? 'Sessione autorizzata' : 'Non connesso'}</small></div>
      </div>
      <div className="remote-console-actions">
        {active && <span className="session-countdown"><Wifi size={15}/><span>Scade tra {timeLeft}</span></span>}
        {active && session && secondsRemaining(session.expiresAt, now) <= 300 && <button type="button" className="secondary compact" disabled={viewState === 'renewing'} onClick={() => void start()} title="Rinnova il collegamento mantenendo aperta questa console">
          {viewState === 'renewing' ? <LoaderCircle className="spin" size={15}/> : <RotateCcw size={15}/>}Rinnova sessione
        </button>}
        {active && <button type="button" className="secondary compact" onClick={() => window.open(session.url, '_blank', 'noopener,noreferrer')} title="Apri la sessione in una nuova scheda"><ExternalLink size={16}/>Apri scheda</button>}
        {active && <button type="button" className="secondary compact" onClick={() => void fullscreen()} title="Schermo intero"><Expand size={16}/><span className="sr-only">Schermo intero</span></button>}
        {active && isDesktop && <button type="button" className="secondary compact" disabled={viewState === 'disconnecting'} onClick={() => void disconnectOperator()} title="Disconnette gli operatori attivi ma mantiene valido il collegamento"><UserRoundX size={15}/>{viewState === 'disconnecting' ? 'Disconnessione…' : 'Disconnetti operatore'}</button>}
        {active && <button type="button" className="danger-button compact" onClick={() => void stop()}><Square size={15}/>Termina</button>}
        {!active && <button type="button" disabled={!allowed || !ready || revoked || viewState === 'creating'} onClick={() => void start()}>
          {viewState === 'creating' ? <LoaderCircle className="spin" size={16}/> : viewState === 'error' || viewState === 'expired' ? <RotateCcw size={16}/> : <MonitorPlay size={16}/>}
          {viewState === 'creating' ? 'Attendere…' : viewState === 'error' || viewState === 'expired' ? 'Riprova' : 'Avvia sessione'}
        </button>}
      </div>
    </div>
    {error && <p className="remote-inline-error" role="alert"><X size={16}/>{error}</p>}
    {revoked && <p className="remote-empty-state"><ShieldCheck size={20}/>Dispositivo revocato: l’apertura di sessioni remote è disabilitata.</p>}
    {!revoked && !allowed && <p className="remote-empty-state"><ShieldCheck size={20}/>Il tuo account non ha il permesso necessario per questa sessione.</p>}
    {!revoked && allowed && !ready && <p className="remote-empty-state"><ShieldCheck size={20}/>Motore remoto disattivato o non ancora associato. In Desktop remoto un amministratore può verificare la configurazione.</p>}
    {active && session && viewState !== 'disconnected' ? <div className="remote-frame-shell">
      {viewState === 'loading' && <div className="frame-loading"><LoaderCircle className="spin" size={20}/>Apertura della sessione autorizzata…</div>}
      <iframe
        key={frameKey}
        ref={setFrameElement}
        src={session.url}
        title={`${title}: ${deviceName}`}
        referrerPolicy="no-referrer"
        allow="clipboard-read; clipboard-write; fullscreen; display-capture"
        sandbox="allow-forms allow-scripts allow-same-origin allow-downloads allow-popups allow-popups-to-escape-sandbox allow-modals allow-pointer-lock"
        onLoad={() => { setError(''); setViewState(current => current === 'loading' || current === 'error' ? 'page-loaded' : current); }}
      />
      <div className="remote-frame-footnote">La pagina MeshCentral è incorporata in modo isolato. Lo stato di caricamento non conferma da solo che il flusso video sia attivo.</div>
    </div> : active && viewState === 'disconnected' ? <div className="remote-empty-state">
      <UserRoundX size={28}/><strong>Operatore disconnesso</strong>
      <span>Il link resta utilizzabile fino alle {new Date(session!.expiresAt).toLocaleTimeString('it-IT')}. Puoi ricollegarti oppure terminare la sessione per impedirne il riuso.</span>
      <button type="button" className="secondary compact" onClick={reconnect}><RotateCcw size={15}/>Ricollega</button>
    </div> : !revoked && allowed && ready && <div className="remote-empty-state">
      <MonitorPlay size={28}/>
      <strong>{viewState === 'expired' ? 'Il collegamento è scaduto' : viewState === 'error' ? 'Sessione non disponibile' : 'Pronto per collegarsi'}</strong>
      <span>{isDesktop ? 'La sessione può includere più monitor. Monitor, qualità, scaling, tastiera e clipboard sono gestiti dai controlli MeshCentral disponibili nel desktop remoto.' : capability === 'terminal' ? 'Il terminale viene servito da MeshCentral in base alla capability autorizzata.' : 'La gestione file viene servita da MeshCentral in base alla capability autorizzata.'}</span>
    </div>}
    {active && isDesktop && <p className="remote-capability-note">Selezione monitor, scaling, qualità, tastiera e clipboard dipendono dai controlli esposti dalla versione MeshCentral installata.</p>}
  </section>;
}
