# Roadmap

Stato corrente sintetico: [consegna Windows/LAN](completion-status.md). Le sezioni successive conservano lo storico; gli stati iniziali progettato/non compilato sono superati solo dalle verifiche successive esplicitamente registrate.

### Incremento SD1 — Allegati ticket con quarantena (implementato nel branch, non pubblicato)

API, dashboard IT e portale supportano allegati fino a 10 MiB, quote per ticket, consenso esplicito per la condivisione pubblica, visibilità pubblica/interna, storage privato con hash e download negato finché ClamAV non registra una scansione pulita. Il worker usa claim PostgreSQL con lease, retry durevoli e audit; il servizio antivirus è fissato a ClamAV 1.4.5 via digest e non pubblica porte. Gli operatori con `tickets.manage` possono riaccodare una scansione fallita, senza poter sbloccare file infetti. La scansione pulita produce una notifica outbox a coda/assegnatario e collaboratori; quelle interne richiedono anche `tickets.note.internal`. Test API 82/82, build dashboard e 6/6 Vitest, configurazioni Compose e smoke isolato con scansione reale ClamAV pulita/EICAR sono passati localmente; lo smoke Chromium include upload self-service, consenso, scansione, download, revoca e conservazione dell'accesso al team IT. In più, `./scripts/smoke.sh --browser` crea un backup effimero con allegato, controlla il manifest e verifica il restore runtime di DB/blob/chiavi Identity/CA via HTTPS. CI Linux `39333890788` e Windows `39333890830` sono verdi sul commit `95fc6ef` per i gate eseguiti prima dell'estensione del restore nello smoke; la nuova revisione attende CI. Restano restore snapshot LAN/off-site, retention e prova portale con account reale. L'incremento non è distribuito sul server LAN.

### Incremento console RMM React e MeshCentral — sorgenti implementati, collaudo live aperto

Interfaccia dedicata nella scheda dispositivo con Panoramica, Desktop remoto,
Terminale/PowerShell, File, Processi, Servizi Windows, Software, Aggiornamenti,
Eventi e Ticket. Icone Lucide, layout responsive e tema OmniDesk; build production
riuscita. La sessione usa
il grant breve creato dall'API per la capability richiesta; il browser riceve
soltanto URL HTTPS `/sharing` a scadenza breve, con `no-referrer`, controlli
origine e revoca tramite API. Il backend rimane autoritativo su RBAC e device;
MeshCentral è limitato a origini di framing configurate e non usa login token.

Monitor multipli, qualità, scaling, input, clipboard e fullscreen sono affidati
all'interfaccia nativa MeshCentral effettivamente incorporata; React non tenta
di controllare il DOM cross-origin. La UI indica che il caricamento iframe non
dimostra la connessione video. Processi, servizi ed eventi hanno ora raccolta
su richiesta in sola lettura dall'Agent, autorizzazione device/group scoped
`diagnostics.view`, coda, rivalidazione al claim, limiti di output e audit.
Terminazione processi e controllo stop/start/restart servizi sono operazioni
separate con permessi `processes.manage` e `services.manage`, conferma e audit.
Le API restituiscono un 409 strutturato se l'identità Agent manca o è revocata,
così la dashboard può spiegare perché una raccolta o un controllo è negato.
Le metriche
attuali sono snapshot/counter e non generano grafici storici. Build e test sono registrati
in [validation.md](validation.md). Sessione Mesh reale e collaudo interattivo
Windows restano gate separati. Deploy live consentito solo dopo backup e
verifica che non vi siano sessioni remote o job mutanti attivi.

- M1: infrastruttura Compose/Caddy/PostgreSQL, setup Ubuntu, schema e migrazioni Identity, bootstrap, autenticazione e CSRF, utenti, permessi globali/gruppo/device, audit, dashboard e branding. Consegna iniziale: vedi validation.md per prove effettive e limiti.
- M2 (implementazione iniziale compilata; MSI compilato in CI; gate installazione Windows/firma ancora aperto): servizio Windows .NET con enrollment monouso, chiavi dispositivo, revoca, retry/backoff, metriche CPU/RAM/disco/rete/uptime, inventario; MSI unico firmato e aggiornamento sicuro. Test Windows e reti NAT differenti obbligatori.
- M3 (adapter compilato e permessi testati con fake; gate motore/Windows e audit node-forge aperti): MeshCentral, adapter server con mapping identità e gruppi, sessioni autorizzate e revocabili per remote/terminal/files; MSI coordina i due servizi. Test anti bypass dei permessi e WebSocket/TLS. MeshCentral non viene esposto da M1.
- M4 (coda/API testate, worker cross-compilato; collaudo WUA Windows ancora aperto): Windows Update scan/install, job firmati/idempotenti, reboot policy, scheduling e report.
- M5 (console cross-compilata, TOTP e layout testati; flag TOTP inizialmente false): console Windows WebView2 della stessa UI, dashboard layout per utente, TOTP setup/challenge/recovery/cifratura implementati e testati; hardening, retention, restore, upgrade e release firmata ancora aperti.

Release candidata 0.8.3: raccolta on-demand processi/servizi/eventi con permesso
`diagnostics.view`; versione MSI e workflow Windows aggiornati. Build Linux e test locali passati; workflow Windows run 39242100048 riuscito. Il kit 0.8.3 ha superato CI Windows/Linux; nessun deploy di quella release sul server LAN registrato qui.

Gate produzione: revisione minacce, pen test autorizzazione/CSRF/enrollment, build riproducibili e SBOM, snapshot LAN/off-site ripristinato, cifratura/retention/RPO-RTO, certificati pubblici verificati, test MSI install/upgrade/uninstall e rotazione credenziali. Il restore full-stack con allegato di test passa nello smoke isolato, ma non equivale a un disaster recovery periodico su host separato. Nessuna data di completamento promessa per moduli non implementati.

Incremento Agent LAN 0.5.2: client HTTP comune senza redirect, diagnostica readiness prima dell'enrollment, verifica identità protetta e script installazione con CA LocalMachine/impronta SHA256 e MSI elevato. Binario win-x64 cross-compilato, contratti/test TLS sul server LAN verificati. MSI 0.5.2 e lifecycle Windows ancora da compilare/collaudare su Windows; kit locale diagnostico senza MSI. [Procedura LAN](windows-lan-install.md). Nessun desktop remoto o Tray dichiarato operativo.

Avanzamento hardening M5: rotazione recovery code con rinnovo security stamp, invalidazione delle altre sessioni entro il controllo Identity e conservazione della sessione corrente. Scenario PostgreSQL esteso e passato; gate Windows, firma e produzione restano aperti. Nessuna fase WT o SD avviata.

Istanza di collaudo avviata su HTTPS 192.168.1.233:8085: build container, migrazioni, readiness e login verificati dal server. Istruzioni in [accesso LAN](lan-nginx.md); verifica da client LAN e gate di produzione ancora aperti.

Accesso da client LAN confermato dall'utente. Incremento console M5: elenco utenti/ruoli globali, creazione gruppi e assegnazione/revoca concessioni per gruppo o dispositivo dalla dashboard. Lifecycle completo account/ruoli, reset assistito MFA/password e gate Windows restano aperti; percorso WT/SD non avviato.

Incremento lifecycle M5: sospensione/riattivazione account, sostituzione ruoli globali e revoca sessioni web, ultimo amministratore protetto e controllo cookie/stamp a ogni richiesta. Compilato e verificato con PostgreSQL/Chromium. Guard sessioni remote con revoca persistente/retry dopo perdita permessi: test con fake, chiusura tunnel reale ancora aperta. Migrazione UserLifecycle versionata. Backup locale, restore DB e restore runtime isolato di un backup storico con chiavi Identity/CA verificati; backup recente con allegati, off-site e retention non chiusi. Prossimi gate: lifecycle ruoli/gruppi avanzato, reset assistito e collaudi Windows/MSI, firma e motore remoto. Le fasi WT/SD rimangono separate dal completamento della baseline.

Incremento gestione dispositivi: etichetta/gruppo e revoca identità dalla console, conflitti di modifica rilevati, rivalidazione job queued al claim e dopo cambio gruppo, revoca idempotente con cancellazione queued e richiesta chiusura sessioni remote. Verificati PostgreSQL (anche concorrenza), Chromium e build. Nessuna nuova migrazione o modifica all'Agent; MSI/Windows, remoto reale, Tray, lifecycle ruoli/gruppi avanzato e Service Desk restano aperti.

## Percorso futuro OmniDesk Service Desk

**SD0–SD2 hanno incrementi parziali implementati e verificati; SD3 ha intake macchina e correlazione RMM parziali, ma manca l'ingresso email.** Le colonne distinguono ciò che esiste dai risultati ancora da sviluppare. Il percorso non sostituisce M1–M5 né chiude i gate di produzione. Specifiche in [Service Desk](service-desk.md) e [schema dati](service-desk-data-model.md).

| Fase | Risultato previsto | Dipendenze e gate di ingresso/uscita |
| --- | --- | --- |
| SD0 — basi comuni e progetto esecutivo | **Implementato:** registro sedi e Device.SiteId, sede backfill “Sede da assegnare”, concessioni site-scoped, snapshot sede sui ticket, outbox transazionale, worker durevole per proiezione notifiche ticket con retry, dead letter e lock concorrente; storage privato allegati e scansione ClamAV sono implementati. **Da completare:** copertura/consumer per tutti gli eventi di dominio e policy di retention esplicita | Migrazioni versionate; nessun gruppo trasformato implicitamente in sede. Il ticket conserva la sede storica quando il dispositivo si sposta. Quarantena, pulizia/infezione, retry autorizzato e backup DB/blob sono testati nel branch |
| SD1 — ticket e console IT | **Parziale implementato:** intake Agent/Tray mono-dispositivo, stati open/in_progress/resolved/closed, transizioni server-side, priorità P1–P4 canoniche, categorie, code/site routing/membership, assegnazione singola/bulk, chat pubblica, audit, note interne e link multi-dispositivo nella stessa sede. Workflow globale/per sede con bozze concorrenti, pubblicazione immutabile, prevalenza sede/fallback globale e binding della versione a ogni nuovo ticket; collegamento a sessioni/job RMM con snapshot append-only; riepiloghi manuali strutturati e collaboratori ticket scoped. Allegati generici con quarantena, antivirus, retry autorizzato, notifica dopo scansione pulita e backup blob sono implementati e testati nel branch. | Test PostgreSQL, Vitest e smoke Chromium; il nuovo smoke portale verifica upload/consenso/scansione/download/revoca. Il branch non è ancora distribuito sulla LAN; restano restore completo e retention. Nessuna azione RMM è attivata dagli allegati |
| SD2 — portale e collaborazione | **Parziale implementato:** Tray locale mostra ticket e risposte pubbliche; dashboard IT separa conversazione e note interne. Portale web con associazione amministrativa, ticket/chat, screenshot PNG con consenso e allegati generici pubblici con consenso, quarantena e scansione antivirus. KB interna con bozze, pubblicazione, grant `knowledge.view` globale/sede/gruppo, revisioni scoped e ricerca autorizzata. | Suite API e smoke browser isolati verificano accesso, consenso e visibilità; prova sul portale con account reale resta aperta. La KB resta interna senza HTML/Markdown o allegati; restore completo e retention sono gate separati |
| SD3 — ingressi agente/email e storico interventi | **Parziale implementato:** intake macchina autenticato, limitato e idempotente; collegamento ticket-sessione/job RMM con snapshot e timestamp terminale server-side quando disponibile; riepilogo operatore append-only strutturato per attività già collegate. **Da completare:** mailbox/thread verificati e qualificazione email | Identità macchina separata da quella umana, rate limit e test replay esistenti; segreti mailbox e test spoof/replay/loop obbligatori prima di collegare email. Nessuna identità umana deriva dal solo mittente |
| SD4 — allarmi, SLA ed escalation | Motore allarmi core persistente, ticket da occorrenze correlate, SLA/calendari versionati, timer durevoli e automazioni allowlist | SD0–SD3 e sviluppo/validazione del motore allarmi, oggi assente. Gate DST/pausa/riapertura/crash, deduplica escalation e prevenzione loop. Nessuna shell automatica |
| SD5 — conoscenza avanzata e AI opzionale | Ricerca autorizzata, KB e storico avanzati; riepiloghi e bozze AI opt-in, eventuali approvazioni umane specifiche per future azioni | SD1–SD4 stabilizzati, policy dati/provider/budget e valutazioni prompt injection. AI senza credenziali o accesso diretto ai comandi; produzione subordinata a restore, carico e sicurezza |

Incremento KB — ricerca autorizzata: API e dashboard cercano in titolo, descrizione e corpo, applicano grant globali/sede/gruppo prima del match e restituiscono al massimo 50 risultati senza corpo articolo. Draft consultabili solo da `knowledge.manage` globale. La ricerca PostgreSQL full-text/GIN, Markdown e il portale esterno non sono inclusi. Test PostgreSQL coprono contenuti e isolamento tra scope; smoke Chromium verifica la ricerca tramite UI. Nessun deploy LAN in questo incremento.

Le fasi non hanno date promesse. KB di base e storico non dipendono dall'AI. L'attivazione email, antivirus o provider AI richiederà configurazione operativa separata: nessun servizio esterno è stato collegato da questa documentazione. I test indicati nelle specifiche sono criteri futuri, non risultati già ottenuti.

## Percorso futuro Windows Agent e Tray

**Implementazione parziale avviata su richiesta successiva dell'utente.** La baseline verificata resta quella M1–M5; il Service Desk è avviato e il suo stato è nella sezione dedicata sopra. [Specifica tecnica](windows-agent-tray.md), [stato sorgenti 0.6.0](windows-agent-tray-implementation.md). Il MSI già compilato resta Agent-only; il sorgente MSI 0.6.0 include Agent+Tray, non compilato o installato. Tray distinta dalla console IT WebView2.

| Fase | Consegna proposta | Dipendenze e criteri di accettazione |
| --- | --- | --- |
| WT0 — contratti e autorizzazione | Threat model IPC, matrice privilegi, identità utente/device, flusso consenso remoto e dismissione | Baseline Agent/Identity/RMM; stabilire versioni Windows supportate, service SID, payload e migrazioni. Nessuna fiducia nel solo SID dichiarato o ticket visibile |
| WT1 — lifecycle servizio | ACL servizio/binari/credenziali, Service Recovery, heartbeat di salute e audit anomalie; revoca e procedura uninstall elevata | WT0 e gate install/upgrade M2. Test account standard, crash/riavvio, no login, offline e manutenzione. Allarmi server durevoli dipendono dal motore futuro SD4 |
| WT2 — Tray e pipe | UI non elevata per logon, stato reale connessione, named pipe locale autenticata e metodi limitati; MSI unico Agent+Tray | WT0–WT1; test pipe squatting, ACL, input ostile, sessioni RDP, logoff e Tray chiusa con servizio attivo; firma MSI/binari e install/repair/upgrade/rollback/uninstall |
| WT3 — assistenza e screenshot | Apertura ticket dal device, elenco ticket propri, pairing/login utente e allegati con anteprima/consenso | WT2 e SD0–SD3 per sedi, ticket, portale, intake macchina e allegati. Prima di tali dipendenze non dichiarare operativo il pulsante. Nessuna cattura background o cross-sessione |
| WT4 — consenso remoto end-to-end | Grant breve per device/capacità/logon, indicatore/ritiro consenso, chiusura tunnel e policy unattended separata disabilitata di default | WT2 e gate motore reale M3; WT3 solo per ingresso dal ticket. Test revoca live, permesso perso, timeout/Tray assente, multi-sessione e reti NAT; nessuna escalation privilegi tramite IPC |

WT3 e WT4 hanno dipendenze diverse: assistenza remota dalla console non deve attendere l'intero Service Desk; ticket e screenshot richiedono invece i servizi pertinenti. Nessuna nuova attestazione di protezione anti-admin o collaudo Windows deriva dall'aggiornamento documentale.

Avanzamento sorgenti WT0/WT1/WT2: contratto IPC v1 read-only, stato heartbeat reale del servizio, WTS/logon/ACL/pipe locale, NotifyIcon non elevata e MSI unico 0.6.0 con HKLM Run e recovery/DACL servizio. Implementato nel codice, non compilato: revisione automatica Docker indisponibile per capacità del modello, comandi non eseguiti. Nessuna fase WT conclusa; collaudi completi Windows rinviati come richiesto, compilazione ancora bloccata. WT3/WT4 e fasi SD restano da sviluppare. Istanza LAN non aggiornata con questi sorgenti.

Gate aggiuntivi WT0/WT4: definire le capacità dedicate unattended/elevazione/dismissione e l'intervallo massimo di rivalidazione dei permessi; verificare separatamente desktop, terminale e file, consumo singolo del grant e chiusura reale del motore dopo revoca. Gate WT2: unica transazione MSI Agent+Tray, ripristino ACL/recovery durante repair, rollback senza residui privilegiati e rimozione startup per tutti gli utenti. Il lavoro corrente e i gate M1–M5 mantengono priorità; le fasi WT saranno avviate separatamente, senza includerle implicitamente nella milestone in corso.

Priorità aggiornata su richiesta: completare remoto, poi ticket/screenshot, infine generare il singolo MSI per collaudo LAN. Incremento sorgenti: revoca esplicita durevole con risposta pending distinta dalla conferma e script unico di build Windows con checksum; nessun nuovo risultato compilato/testato. Motore remoto e Windows sono gate aperti, ticket/screenshot non avviati. Il blocco della revisione automatica Docker per capacità permane; richiesto ambiente Windows/runner per generazione reale MSI.

Aggiornamento Ubuntu f1: l'operatore conferma build API/dashboard riuscita e ripetuta con cache; il precedente impedimento alla compilazione Docker non è più un blocco del server. Configurazioni Compose validate; container esistenti running con DB healthy, ma nessuna nuova attestazione runtime o distribuzione delle immagini ricompilate. Test applicativi e sonda LAN in questa sessione impediti dal sandbox, senza ripetere l'escalation automatica. Preparata procedura `scripts/validate-release.sh`. Installer: GitHub Actions Windows è il percorso concordato, workflow locale aggiornato con test contratto Tray e packaging; esecuzione/artifact ancora pendenti, accesso GitHub non verificabile dalla sessione. Priorità invariata: remoto completo → ticket/screenshot → MSI e prova LAN Windows.

Incremento 0.6.0 in lavorazione: ticket macchina associati all'utente Windows autenticato della pipe e al dispositivo firmatario, elenco ticket propri e risposte, console IT con tickets.view/manage e versione attesa, screenshot con anteprima/consenso e validazione PNG limitata. Specifica effettiva in [support-implementation](support-implementation.md). È un percorso minimo aggiuntivo: non completa le fasi SD per sedi/email/SLA/KB o gli allegati generici. Agent e Tray cross-compilati; test server PostgreSQL aggiornati passati. Packaging Windows e prove reali ancora da eseguire. Per il remoto aggiunti stato configurazione, mapping amministrativo e storico/retry revoche nella console; motore non dichiarato operativo. L'advisory node-forge GHSA-86w9-cpqp-85rv risulta ancora senza versione corretta al controllo odierno; nessun rilascio pubblico del motore remoto.

Packaging 0.6.0: primo GitHub Actions Windows riuscito con MSI unico e artifact, oltre a contratti IPC/self-test Agent. Supporto server pubblicato sul LAN con migrazione e backup; probe HTTPS reale passata dopo avvio. Smoke browser esteso alla risoluzione ticket passato; suite finale 23 xUnit/3 Vitest. In aggiunta è stato preparato il test MSI installazione/crash recovery/uninstall su runner Windows, con esito da registrare. Restano reali gate interattivi Windows e motore remoto: un MSI compilato non attesta desktop remoto funzionante.

Consegna verificata del kit LAN Agent/Tray 0.6.0: Windows run 39204693297 e Linux run 39204693314 riusciti, incluso install/crash recovery/uninstall MSI sul runner. Artifact e ZIP con CA pubblica/checksum verificati; istruzioni operative aggiornate. Test interattivi Windows/LAN e desktop remoto completo restano aperti, non inclusi nel risultato dei run. Vedere completion-status.md e validation.md per il confine esatto della consegna.

Incremento 0.7.0 in sviluppo per chiudere il provisioning remoto: MSI con motore vendor verificato, servizio demand-start, configurazione per dispositivo approvata e cifrata sul server, rinnovo dell'autorizzazione dal servizio, mapping verificato contro MeshCentral e overlay LAN. Specifica in remote-provisioning.md. Aggiunto download del kit dalla sezione Dispositivi della UI, con manifest/hash verificato e autorizzazione amministrativa. Finche non registrate build/test/runtime successivi, questi sorgenti non sono una consegna remota verificata.

Progresso 0.7: pulsante download autenticato disponibile su LAN con kit 0.6 verificato; smoke browser download e permessi passato. Motore reale LAN :8086 avviato; sonda .NET TLS/control, provisioning/retry e rimozione gruppo diagnostico passata. Aggiunti revoca persistente gruppo, conversione node hash nativo e prova Windows startup del motore/fail closed. Nuovo MSI da compilare e promuovere dopo questi controlli; desktop e consenso su Windows 10 192.168.1.160 ancora da eseguire.

Kit 0.7 promosso dopo Windows run 39207328177: motore nativo realmente avviato sul runner, generazione NodeID e fail closed senza enrollment verificati. Download UI disponibile su LAN. Aggiunte prove upgrade baseline 0.6, repair ACL e rollback, in attesa del run successivo. Attesa installazione sul client Windows 10 192.168.1.160 per chiudere i test interattivi: non simulati sul server.

Installazione manuale semplificata: launcher LAN con parametri verificati e richiesta interattiva del token mascherata, senza creazione manuale del file. Modalita file preservata per automazioni. Parsing e test prompt Windows nel run 39209165420; pubblicazione del kit solo dopo esito verificato. Resta distinto dal futuro wizard grafico integrato nell'MSI.

Distribuzione del launcher interattivo separata dal binario MSI: mantenuto l'MSI 0.7 verificato (run 39207328177), aggiornato solo lo script con prompt gia testato Windows. Provenienza dei due componenti esplicita nel manifest/LEGGIMI. Il collaudo esteso upgrade/repair/rollback rimane distinto e non viene dichiarato superato.

Hotfix 0.7.1 Tray: lettura SID di logon dal token nativo per correggere mancata creazione pipe e autenticazione client. Build locale passata; self-test Windows esteso, aggiornamento del client e prova Tray reali da eseguire. Non richiede un nuovo enrollment dello stesso server.

0.7.1: Windows run 39211896966 e Linux 39211897037 riusciti. Confermata lettura SID logon nativa e lifecycle MSI; chiusi i controlli CI di upgrade baseline 0.6, repair ACL e rollback. Kit in pubblicazione per collaudo Tray sul PC reale; consenso e sessione remota restano da verificare separatamente.

Gate remoto pilota: Defender ha quarantinato il binario e rimosso il servizio, rilevamento HackTool:Win32/Meshagent.MBZ!MTB confermato dagli eventi. Dossier di revisione pronto; nessun invio o verdetto di falso positivo. Collaudo desktop sospeso in attesa di risoluzione della valutazione del motore. Agent/Tray LAN restano verificati; UI e ticket possono proseguire indipendentemente. Correzione conservazione sorgente MSI e test estesi passati nel run 39215503901.

Pilot remoto: dopo autorizzazione esplicita Defender dell'operatore, risolto nei sorgenti il rifiuto mapping 409 dovuto alla risposta upstream nodes che omette meshid dentro il nodo. Preservati gruppo esatto/nodo unico/online/credenziale; deploy e prova runtime da registrare. L'ispezione live ha rilevato una discrepanza di hash certificato riportata da MeshCentral per l'Agent Windows: aggiunto un endpoint amministrativo auditato per rigenerare la configurazione, ruotare l'ID di provisioning e forzare il riavvio del motore conservando la policy. API e UI sono ora pubblicate sull'istanza LAN dopo backup e migrazioni versionate; il percorso di rigenerazione è disponibile, ma non ancora eseguito sul dispositivo pilota. Verifica del video Windows ancora aperta. Autorizzazione laboratorio Defender non costituisce verdetto Microsoft o chiusura del gate produzione.

Incremento accesso non presidiato: policy per dispositivo con consenso predefinito,
modifiche solo amministratore globale e versione attesa, snapshot nella sessione,
revoca persistente al cambio e ricontrollo dopo creazione link. Notifiche Mesh
obbligatorie nel gruppo, richiesta di consenso decisa sul server per ciascuna
sessione e protocollo. UI con controllo esplicito, conferma e storico modalita.
Migrazione RemoteAccessPolicy generata; test iniziali passati, verifiche finali e
runtime ancora da completare. Nessuna attivazione globale sui dispositivi.

Riordino web UI: icone SVG locali, ricerca dispositivi, stati reali leggibili,
metriche compatte, dettagli avanzati espandibili, onboarding/download affiancati,
avvio remoto con apertura scheda e link di riserva. Supporto responsive e temi.
Scansione Windows Update segnalata fallita sul pilota: correzione avvio processo
PowerShell con RemoteSigned solo nel processo, precedenza Group Policy preservata,
nessun riavvio automatico; nuovo Agent/MSI 0.7.2 da compilare e collaudare.

Consegna corrente: UI ridisegnata e policy non presidiata pubblicate su LAN dopo 43 xUnit, 3 Vitest, smoke Chromium desktop/mobile e sonda motore reale. Policy pilota disattivata per default, attivabile dall'amministratore dalla scheda dispositivo. Windows kit 0.7.2 pubblicato dopo run Windows 39219223914 riuscito (incluse entrambe le baseline upgrade, repair e rollback). Scansione WUA reale e prove non presidiate restano collaudi client separati; revisione Microsoft del rilevamento ancora non inviata.

Incremento assistenza corrente: nome computer nella colonna Dispositivo e nel
dettaglio ticket (stesso nome registrato nella console, inizializzato dal nome
Windows all'enrollment), ricerca per oggetto/nome senza distinzione maiuscole,
filtro stato e apertura contestuale della scheda RMM solo per dispositivi
visibili all'operatore. UUID resta interno per correlazione e autorizzazione.
Contratti macchina/Tray invariati; nessun nuovo enrollment o MSI richiesto.
Si tratta del miglioramento del modulo ticket esistente: sedi, assegnazioni,
SLA, email e cronologia collaborativa delle fasi SD restano incrementi distinti.
Test PostgreSQL iniziali passati; build/browser/deploy da registrare sotto.

Incremento Assistenza consegnato su LAN: nome PC al posto dell'UUID, ricerca,
filtro e navigazione contestuale verificati con PostgreSQL/Vitest/Chromium;
aggiornamento automatico elenco autorizzato dopo nuovi enrollment. Non completa
le fasi SD0/SD1 estese. Restano separati i collaudi client Update/non presidiato,
la revisione Microsoft del motore e i gate produzione gia elencati.

Incremento conversazione ticket: cronologia persistente delle nuove risposte IT
 e dei cambi di stato, screenshot nel messaggio iniziale con ingrandimento,
validazione esplicita della risposta per risolvere/chiudere. Migrazione
SupportConversation importa solo l'ultima risposta preesistente, indicata come
precedente e datata con l'ultima modifica disponibile. Nessuna cronologia fittizia.
La Tray esistente mostra stato e ultima risposta con «Aggiorna ticket» in
«Richiedi assistenza / I miei ticket»; thread completo, risposte dell'utente e
notifiche in tempo reale restano sviluppo successivo. Nessun nuovo MSI richiesto.
Conversazione compilata e verificata con 43 xUnit, 4 Vitest e Chromium isolato;
collaudo su ticket reale a cura dell'operatore dopo pubblicazione LAN.
Conversazione pubblicata su LAN con migrazione applicata e verifica HTTPS/runtime
riuscita; utente finale ancora su stato/ultima risposta nella Tray esistente.

Correzione Windows 0.7.3: Tray attende la disponibilita della pipe condivisa con
il polling invece di fallire immediatamente per ERROR_PIPE_BUSY/FILE_NOT_FOUND.
Errori identita/ACL restano bloccanti; timeout, formato e disconnessione hanno
messaggi distinti. La risposta HTTP 200 osservata sul server non dimostra la
consegna IPC sul PC: causa specifica e collaudo interattivo ancora da confermare.
Le notifiche remote sono richieste anche in non presidiato (flag 1/2/4): da
verificare visivamente sul pilota, separatamente dalla richiesta di consenso.
Avviso remoto: aggiunta richiesta barra desktop persistente (flag64) alle nuove
sessioni, indipendente dal consenso. Toast iniziale gia richiesto. Verifica
interattiva di apparizione/rimozione sul PC pilota ancora necessaria.
Kit 0.7.3 compilato/testato su Windows (run39223715334 riuscita) e pubblicato nel
download autenticato della console. Restano prove client: consultazione ticket
ripetuta durante polling e avviso/barra durante nuova sessione remota.

0.8.0 build-only riuscita su GitHub Actions run 39229033734: Setup WinForms nativo,
Agent, Tray e MSI compilati; i job di test sono stati esplicitamente saltati.
Artifact LAN confezionato con CA pubblica, checksum e istruzioni native; API con
endpoint revoca pubblicata sul server dopo backup locale. La disinstallazione MSI revoca prima
identità e accessi e fallisce chiusa se il server non risponde. Installazione,
enrollment e rimozione interattivi non sono collaudati.

### Aggiornamento remoto Agent Windows

Implementazione sorgente 0.8.1: heartbeat include versione Agent; il server
accetta un job solo da amministratore globale, su dispositivo online, con
versione rilevata e release MSI pubblicata. L’Agent scarica solo l’MSI legato a
job/lease validi, controlla dimensione e SHA-256 e invoca Windows Installer
senza riavvio di Windows. Un updater separato e una ricevuta ACL protetta
permettono al servizio di riavviarsi e riportare il risultato. Build MSI,
installazione/recovery/disinstallazione e upgrade/repair/rollback da baseline
0.6.0 e 0.7.0 sono passati sul runner Windows GitHub Actions
([run 39236894238](https://github.com/ico88/OmniDesk/actions/runs/39236894238)).
Il kit LAN 0.8.1 è pubblicato e disponibile dalla console agli amministratori;
il kit 0.8.0 non contiene updater e richiede una singola installazione manuale
della 0.8.1. La 0.8.2, build di validazione senza modifiche funzionali, ha
superato il workflow Windows e ora è il target pubblicato per testare
l’aggiornamento remoto dal client 0.8.1. Il collaudo di installazione e ricevuta
sul PC pilota resta aperto. Firma Authenticode e invio Microsoft restano fuori
da questa implementazione; i checksum del server non attestano l’editore.


### SD0 — sedi e ambiti autorizzativi core — implementazione verificata

Aggiunta la migrazione versionata `CoreSites`: crea il catalogo, assegna i record
esistenti alla sede esplicita “Sede da assegnare”, trasferisce la sede corrente
del dispositivo sui ticket esistenti e rimuove i default dopo il backfill. I gruppi
non vengono convertiti in sedi e i grant esistenti restano limitati al loro
ambito. La API consente amministrazione/archiviazione delle sedi, selezione sede
per enrollment, assegnazione dispositivo, grant site-scoped e filtro ticket;
la Dashboard espone questi flussi. Ticket già aperti mantengono il proprio
SiteId storico al successivo spostamento del device.

`./scripts/test.sh` passato con 47 test xUnit/PostgreSQL e 4 Vitest, build
TypeScript/Vite inclusa. Lo scenario auth dimostra visibilità dei dispositivi
della sede, negazione separata di `remote.control` e revoca della visibilità;
`git diff --check` passato. L’upgrade con dati M1–M5 e il deploy live non sono
ancora verificati. Il consumer outbox, gli allarmi persistenti e il modulo Service Desk SD1
restano sviluppo successivo.


### SD0 — eventi outbox core — sorgenti e migrazione verificate

Aggiunta `CoreOutboxEvents` con versione schema, aggregate/event type, payload
JSONB minimizzato, correlation ID, contatore retry, lease e timestamp
di pubblicazione. Enrollment device, create/update sede, metadata device e
create/update ticket scrivono l’evento nella stessa unità transazionale della
modifica. Gli eventi ticket escludono descrizione, screenshot e testo risposta;
sono verificati con test API e PostgreSQL. Nessun dispatcher/consumer è ancora
attivo: gli eventi restano persistenti nel DB finché la fase worker non ne
implementa consegna idempotente, backoff, dead-letter e retention. Non dichiarare
notifiche o integrazioni asincrone operative.


### RMM — controllo servizi Windows autorizzato — sorgenti e test Linux verificati

Aggiunta la capability indipendente `services.manage`, concessa globalmente solo
al ruolo Administrator predefinito; Operator non la riceve, mentre ruoli custom
possono averla assegnata esplicitamente. API e claim Agent verificano il grant
per dispositivo, una sola operazione può essere in coda, richieste duplicate sono
idempotenti, il risultato resta consultabile e auditato. L’interfaccia richiede
conferma per ogni avvio/arresto/riavvio. Agent usa ServiceController con timeout
e ricevuta ACL protetta, non esegue shell e non ripete automaticamente operazioni
interrotte. Policy condivisa rifiuta nomi non validi e servizi OmniDesk/Windows
critici. Release 0.8.4 in CI per Agent/Tray/Setup/MSI. Nessun servizio è stato
modificato sul dispositivo pilota; test interattivo Windows, pubblicazione LAN
e firma restano aperti.


### RMM — terminazione processi autorizzata — sorgenti e test in corso

La terminazione usa una capability nuova `processes.manage`, assegnata al solo
ruolo Administrator predefinito e concedibile esplicitamente tramite ruolo scoped.
API serializza l’azione nella coda device e rivalida il permesso al claim; la UI
richiede conferma. Agent ricontrolla PID, nome e timestamp d’avvio, rifiuta nomi
di processi critici e OmniDesk e conserva una ricevuta che impedisce il replay
automatico dopo crash. Self-test Windows crea e termina esclusivamente un
processo PowerShell temporaneo. È consentito annullare solo lavori ancora in coda.
Release MSI candidata 0.8.5. Il test su un processo applicativo reale del PC pilota
e la verifica CI Windows sono ancora aperti.

La console ticket aggiunge filtri server-side per priorità e assegnatario (email),
senza allargare l’insieme dei ticket restituiti oltre gli ambiti `tickets.view`.
Test API verificano filtri e rifiuto di priorità/categorie non valide. Le categorie
core sono general, hardware, software, network, account e other; la Tray le
propone in apertura e la console le filtra. Le code globali e per sede,
membership operatori versionata, routing per categoria con override di sede,
assegnazione per ticket e in massa e filtro per coda sono implementati con
permessi e migrazioni verificate. Restano SLA/calendari.

Il numero ticket è ora generato da Identity PostgreSQL e condiviso dalla Tray e
dalla console nel formato `OD-000123`; non deriva dall’UUID e non promette una
sequenza senza buchi. La migrazione conserva le richieste già presenti e assegna
numeri distinti. La candidata Windows passa da 0.8.5 a 0.8.6 per includere il
numero nella Tray; il workflow precedente non valida questo incremento. La tranche
categorie aggiorna la candidata a 0.8.7 e richiede il proprio workflow Windows.

### SD2 — conversazione ticket dalla Tray — sorgenti e test Linux verificati

La Tray può selezionare uno dei ticket del SID Windows corrente, leggere la
conversazione pubblica e inviare una risposta. Il server associa il ticket sia
al dispositivo firmatario sia alla chiave SID derivata dall’Agent; applica
versione attesa, limite anti-spam e rifiuto delle risposte a ticket risolti o
chiusi. Le note interne non vengono restituite alla Tray. IPC v1 ha nuovi metodi
tipizzati e rifiuta payload non validi. Suite Linux API/PostgreSQL, IPC, build
dashboard e Vitest passati; CI Windows 39265279323 ha compilato e collaudato
MSI 0.8.8 (install/recovery/uninstall e upgrade/repair/rollback). Il kit è
pubblicato nel volume LAN della UI. La prova della conversazione sulla Tray
installata resta aperta.


### SD1 — priorità e assegnazione ticket — sorgenti e test verificati

Il ticket core supporta priorità canoniche `P1` critica, `P2` alta, `P3` normale e
`P4` bassa; l'API continua ad accettare temporaneamente `urgent/high/normal/low`
per i client precedenti. La migrazione `TicketPrioritiesP1P4` converte i valori
persistiti senza perdita di ticket. Restano assegnatario facoltativo,
code, membership, routing, assegnazioni in massa e riepiloghi server-side dei
conteggi. Le viste richiedono i grant ticket correnti; i conteggi non superano il
limite di 100 righe della tabella. L’incremento riepilogo è testato con
PostgreSQL/Chromium; deploy LAN completato dopo CI Linux. Contatori completi
rispettano `tickets.view` e i filtri opzionali device/sede. Restano SLA,
viste operative più ampie e automazioni Service Desk.

### Identità leggibile dell’operatore nelle sessioni remote — implementata su LAN, prova PC aperta

MeshCentral riceveva l’ID interno dell’account come guest name, visibile nel banner
sul PC assistito. La console ora consente di impostare il nome in Account e sicurezza;
la sessione passa quel nome a MeshCentral e usa l’email se il profilo è vuoto. La
migrazione `OperatorDisplayName` è applicata su LAN. Test API/PostgreSQL e browser
smoke verificano il forwarding al motore fake e la persistenza del profilo. Resta
la conferma visiva sul PC Windows pilota; nessuna sessione remota viene avviata
automaticamente.

### Durata sessioni remote — 2026-10-19

Durata predefinita del link temporaneo MeshCentral estesa da 5 a 30 minuti per
ridurre le riaperture durante interventi ordinari. Restano conto alla rovescia,
scadenza server-side e revoca esplicita; il server continua a controllare permessi
e policy tramite il guard di sessione.

La console ora offre «Rinnova sessione» negli ultimi cinque minuti del grant:
crea un nuovo collegamento nella stessa pagina, ripete i controlli server e
revoca quello precedente prima di sostituire l'iframe. Se la revoca resta in
verifica, il guard server continua i tentativi. Per le sessioni presidiate
MeshCentral può richiedere di nuovo il consenso locale; la funzione non aggira
questa policy e non rinnova in background. Build dashboard e test Vitest passati;
test API/PostgreSQL 64/64. Il bundle LAN pubblicato contiene il rinnovo, verificato
da smoke HTTPS/CA. Restano aperti il test di rinnovo/riconnessione su sessione
MeshCentral reale e la ripetizione del consenso sul PC pilota.

### Diagnostica Windows — code indipendenti — sorgenti e test verificati

Le raccolte Processi/Servizi/Eventi possono accodarsi mentre un’altra raccolta
diagnostica è pendente; una nuova richiesta per la stessa categoria riutilizza
il job attivo. La migrazione rimuove l’indice DB che impediva più job pendenti
per dispositivo. Gli interventi che cambiano lo stato (update, processi e
servizi) mantengono la serializzazione reciproca. Test API/PostgreSQL 64/64,
build API e dashboard e Vitest passati. Deploy API LAN effettuato dopo backup
verificato e migrazione applicata; readiness HTTPS/CA e rifiuto anonimo passati.
Prova interattiva Processi/Servizi dal browser ancora da fare.

### Sessione desktop — disconnessione riutilizzabile

La barra distingue «Disconnetti operatore» da «Termina»: il primo invia a
MeshCentral la chiusura degli stream desktop attivi per quel link e mantiene il
grant temporaneo valido fino alla scadenza, quindi consente una riconnessione
manuale. «Termina» revoca il link e impedisce nuovi accessi. L'endpoint server
verifica l'utente proprietario o administration, `remote.control`, dispositivo,
stato e policy prima di inviare l'azione. Il browser non riceve identità Mesh o
credenziali. Build dashboard, Vitest 5/5 e API/PostgreSQL 64/64 passati. Resta
da verificare il comando contro una sessione MeshCentral interattiva. Il deploy
LAN e la migrazione sono stati completati dopo backup e verifica di zero job e
zero sessioni; smoke HTTPS/CA e bundle aggiornato passano.

### Revoca del collegamento remoto e blocco durante la conferma

La barra desktop ora usa un'unica azione «Disconnetti e revoca»: rimuove il link
condiviso, invece di chiudere soltanto lo stream lasciando valido lo stesso grant.
Se la disconnessione o la revoca resta in verifica, il server mantiene lo stato
`revocation_pending`, impedisce nuove sessioni per quel dispositivo e continua i
tentativi; la UI attende la conclusione server-side prima di consentire un nuovo collegamento.
Una volta chiusa la revoca, ogni nuova connessione passa di nuovo da RBAC e dalla
policy di consenso/unattended configurata. API/PostgreSQL coprono il blocco e il
riavvio dopo il retry; Chromium isolato verifica la barra e il flusso UI con URL
Mesh simulato. Il comportamento del motore e della sessione Windows reale resta
da verificare prima del deploy LAN.

Correzione della disconnessione attiva: la revoca di un grant `remote.control`
invia prima `endDesktopMultiplex` tramite MeshCentral e poi rimuove il device
share. `RemoteSessionGuard` ritenta entrambe le operazioni mentre lo stato resta
`revocation_pending`, che continua a bloccare nuove sessioni. L'adapter invia il
comando di chiusura senza una conferma del viewer: la risposta API prova quindi
l'invio del comando e la revoca del link, non che lo stream sul PC sia già
terminato. Test API/PostgreSQL verificano ordine, fallimenti separati, retry e
blocco/riapertura. Prova interattiva contro MeshCentral e Windows resta aperta.

### SD1 — vincolo sede per ticket multi-dispositivo — sorgenti e test verificati

Il collegamento multi-dispositivo esiste già con snapshot, permessi, limite dieci,
lock, versione, audit/outbox e migrazione backfill del primario. Questa correzione
applica la specifica: il dispositivo candidato deve condividere la sede del
ticket e la UI non propone sedi diverse. Il server ricontrolla la sede dopo il
lock dispositivo per evitare link durante uno spostamento concorrente. Test API/
PostgreSQL 64/64, build dashboard, Docker API/dashboard e Vitest 5/5 passati.
API aggiornata su LAN dopo backup e verifica zero sessioni/job; readiness HTTPS/
CA e rifiuto anonimo passano. Test browser autenticato ticket ancora da fare.


### SD1 — creazione manuale ticket dalla console — sorgenti e test verificati

La console IT crea un ticket associato a un singolo dispositivo selezionato, con
categoria, priorità, routing per sede/categoria e snapshot della sede corrente.
Il server applica `tickets.manage` sul dispositivo, registra audit/outbox senza
contenuto descrittivo e rende l'operazione idempotente per utente/dispositivo
tramite RequestId. `Origin` distingue ticket Agent da quelli inseriti
dall'operatore. Test API/PostgreSQL verificano grant scoped, deny fuori ambito,
retry e conflitto per payload diverso. Build API/dashboard e Vitest passati. Deploy su LAN completato dopo backup
verificato e migrazione applicata; readiness e bundle aggiornato verificati. La UI
riusa RequestId per retry senza modifiche e ne genera una nuova se i campi cambiano.
L’affinamento retry UI è pubblicato dopo CI Linux; prova interattiva autenticata ancora da completare.

### Dashboard — badge online/offline dispositivi

L’elenco Dispositivi mostra lo stato effettivo dell’Agent dal heartbeat: online
se ricevuto negli ultimi tre minuti, offline in caso contrario, e Revocato per
identità revocate. Il criterio coincide con la scheda dispositivo; tooltip e
`lastSeen` identificano l’ultimo contatto. L’elenco si aggiorna automaticamente
ogni 30 secondi mentre la sezione è aperta. Test PostgreSQL 64/64, Vitest 5/5,
build Docker e configurazione Compose passati. Pubblicato sull'istanza LAN dopo
backup e verifica di zero sessioni/job; readiness, CA, bundle e rifiuto anonimo
verificati. Lo smoke Chromium isolato verifica il badge Online con account
autenticato e heartbeat firmato; resta la verifica manuale sull'istanza LAN.

### Inbox notifiche ticket — sorgenti e test verificati

La dashboard include un inbox aggiornato ogni 30 secondi per nuovi ticket
instradati a una coda dell'operatore, risposte dei richiedenti e nuove
assegnazioni. Selezionare una notifica la marca letta e apre il ticket; l'API
rivalida `tickets.view` sia quando materializza il destinatario sia quando
restituisce o marca letta una notifica. Non vengono copiati testo della
conversazione o screenshot nella notifica. Il consumer usa ricevute persistenti
per evento/consumer, retry progressivo e dead-letter dopo dieci errori;
l'elaborazione non modifica il checkpoint globale dell'outbox. Test API/PostgreSQL
verificano destinatari, lettura, isolamento tra utenti, idempotenza, retry e
dead-letter. Build API/dashboard, Vitest e smoke Chromium isolato passano.
Questa milestone non invia email/push e non è stata pubblicata sull'istanza LAN;
il comportamento runtime live resta da provare dopo la finestra di test attiva.

### Portale personale ticket — implementazione iniziale nel ramo

Identity web è collegata al dispositivo tramite `PortalDeviceAccesses`, assegnata
e revocata da un amministratore con audit. Il portale React permette a qualunque
account attivo autenticato di vedere i dispositivi esplicitamente associati,
aprire ticket, leggere la propria conversazione e inviare risposte pubbliche.
Le API applicano ownership `RequesterUserId`, link ancora attivo, sede e
credenziale valide, antiforgery sulle mutazioni, idempotenza del RequestId,
versione attesa e rate limit; le note interne non entrano nei DTO. La relazione
non concede permessi RMM e la revoca chiude la visibilità e le nuove risposte.
Migrazione e test PostgreSQL passano; dashboard TypeScript/Vite e Vitest passano.
La migrazione è stata verificata soltanto sul database temporaneo. Rimangono
smoke browser dedicato, pubblicazione LAN e prova autenticata; screenshot,
allegati, email e notifiche in tempo reale non sono inclusi in questa fase.

### Marchio condiviso Tray, installer e favicon

Creato un asset OmniDesk multi-risoluzione con monitor bianco su fondo blu. La
Tray lo usa stabilmente anche quando mostra un errore: la connessione resta
visibile nel tooltip/menu, mentre l’icona identifica sempre il prodotto. Lo
stesso `.ico` è incorporato negli eseguibili Tray e Setup e definisce la loro
icona Windows; il SVG condiviso è usato nella favicon, nella schermata login e
nel marchio predefinito della dashboard. Pacchetto aggiornato a 0.8.9. Build
dashboard, smoke browser e cross-build dei progetti Tray/Setup sono passati.
CI Linux 39290031242 e Windows 39290031180 hanno superato test, MSI, estrazione
icona, installazione/recovery/uninstall e upgrade/repair/rollback. Nessun kit è
stato pubblicato né installato sulla LAN in questo incremento.

### SD1 — collaborazione ticket con partecipanti autorizzati

`SupportTicketParticipants` registra account attivi già autorizzati alla sola
lettura del ticket. La console con `tickets.manage` li aggiunge/rimuove con
versione attesa, lock dispositivo, audit e outbox. La partecipazione non concede
permessi; lettura e notifiche rivalidano account e `tickets.view`, e il worker
esclude eventi anteriori all'aggiunta. La dashboard include l'elenco e i controlli
di gestione. Migrazione, suite PostgreSQL, build API/dashboard e smoke Chromium
sono passati e registrati in [validation.md](validation.md); CI Linux
`39326865633` passata. Non distribuito sull'istanza LAN; workflow Windows MSI
`39326865702` in coda e run precedente `39324222524` ancora attivo.
Allegati generici/quarantena, email, allarmi, SLA e automazioni restano aperti.
