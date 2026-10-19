# Roadmap

Stato corrente sintetico: [consegna Windows/LAN](completion-status.md). Le sezioni successive conservano lo storico; gli stati iniziali progettato/non compilato sono superati solo dalle verifiche successive esplicitamente registrate.

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
dimostra la connessione video. Processi, servizi e log per dispositivo restano
da sviluppare lato Agent/API con permessi dedicati e audit; le metriche attuali
sono snapshot/counter e non generano grafici storici. Build e test sono registrati
in [validation.md](validation.md). Sessione Mesh reale e collaudo interattivo
Windows restano gate separati. Il deploy live è escluso per non interrompere
sessioni remote correnti.

- M1: infrastruttura Compose/Caddy/PostgreSQL, setup Ubuntu, schema e migrazioni Identity, bootstrap, autenticazione e CSRF, utenti, permessi globali/gruppo/device, audit, dashboard e branding. Consegna iniziale: vedi validation.md per prove effettive e limiti.
- M2 (implementazione iniziale compilata; MSI compilato in CI; gate installazione Windows/firma ancora aperto): servizio Windows .NET con enrollment monouso, chiavi dispositivo, revoca, retry/backoff, metriche CPU/RAM/disco/rete/uptime, inventario; MSI unico firmato e aggiornamento sicuro. Test Windows e reti NAT differenti obbligatori.
- M3 (adapter compilato e permessi testati con fake; gate motore/Windows e audit node-forge aperti): MeshCentral, adapter server con mapping identità e gruppi, sessioni autorizzate e revocabili per remote/terminal/files; MSI coordina i due servizi. Test anti bypass dei permessi e WebSocket/TLS. MeshCentral non viene esposto da M1.
- M4 (coda/API testate, worker cross-compilato; collaudo WUA Windows ancora aperto): Windows Update scan/install, job firmati/idempotenti, reboot policy, scheduling e report.
- M5 (console cross-compilata, TOTP e layout testati; flag TOTP inizialmente false): console Windows WebView2 della stessa UI, dashboard layout per utente, TOTP setup/challenge/recovery/cifratura implementati e testati; hardening, retention, restore, upgrade e release firmata ancora aperti.

Gate produzione: revisione minacce, pen test autorizzazione/CSRF/enrollment, build riproducibili e SBOM, backup ripristinato, certificati pubblici verificati, test MSI install/upgrade/uninstall e rotazione credenziali. Nessuna data di completamento promessa per moduli non implementati.

Incremento Agent LAN 0.5.2: client HTTP comune senza redirect, diagnostica readiness prima dell'enrollment, verifica identità protetta e script installazione con CA LocalMachine/impronta SHA256 e MSI elevato. Binario win-x64 cross-compilato, contratti/test TLS sul server LAN verificati. MSI 0.5.2 e lifecycle Windows ancora da compilare/collaudare su Windows; kit locale diagnostico senza MSI. [Procedura LAN](windows-lan-install.md). Nessun desktop remoto o Tray dichiarato operativo.

Avanzamento hardening M5: rotazione recovery code con rinnovo security stamp, invalidazione delle altre sessioni entro il controllo Identity e conservazione della sessione corrente. Scenario PostgreSQL esteso e passato; gate Windows, firma e produzione restano aperti. Nessuna fase WT o SD avviata.

Istanza di collaudo avviata su HTTPS 192.168.1.233:8085: build container, migrazioni, readiness e login verificati dal server. Istruzioni in [accesso LAN](lan-nginx.md); verifica da client LAN e gate di produzione ancora aperti.

Accesso da client LAN confermato dall'utente. Incremento console M5: elenco utenti/ruoli globali, creazione gruppi e assegnazione/revoca concessioni per gruppo o dispositivo dalla dashboard. Lifecycle completo account/ruoli, reset assistito MFA/password e gate Windows restano aperti; percorso WT/SD non avviato.

Incremento lifecycle M5: sospensione/riattivazione account, sostituzione ruoli globali e revoca sessioni web, ultimo amministratore protetto e controllo cookie/stamp a ogni richiesta. Compilato e verificato con PostgreSQL/Chromium. Guard sessioni remote con revoca persistente/retry dopo perdita permessi: test con fake, chiusura tunnel reale ancora aperta. Migrazione UserLifecycle versionata. Backup locale dell'istanza e restore del DB su PostgreSQL temporaneo verificati; restore completo applicazione/chiavi, off-site e retention non chiusi. Prossimi gate: lifecycle ruoli/gruppi avanzato, reset assistito e collaudi Windows/MSI, firma e motore remoto. Le fasi WT/SD rimangono separate dal completamento della baseline.

Incremento gestione dispositivi: etichetta/gruppo e revoca identità dalla console, conflitti di modifica rilevati, rivalidazione job queued al claim e dopo cambio gruppo, revoca idempotente con cancellazione queued e richiesta chiusura sessioni remote. Verificati PostgreSQL (anche concorrenza), Chromium e build. Nessuna nuova migrazione o modifica all'Agent; MSI/Windows, remoto reale, Tray, lifecycle ruoli/gruppi avanzato e Service Desk restano aperti.

## Percorso futuro OmniDesk Service Desk

**Tutte le fasi SD sono proposte, non implementate.** Percorso aggiuntivo: non sostituisce M1–M5 né chiude i gate di produzione. Specifiche in [Service Desk](service-desk.md) e [schema dati](service-desk-data-model.md). Prima di sviluppare saranno stabiliti contratti, matrice permessi e criteri di accettazione per ciascuna fase.

| Fase | Risultato previsto | Dipendenze e gate di ingresso/uscita |
| --- | --- | --- |
| SD0 — basi comuni e progetto esecutivo | Registro sedi, associazione dispositivi e backfill; concessioni per sede; contratti eventi/outbox e audit; decisioni storage, antivirus, retention e identità portale | Inventario e Identity esistenti; nessun gruppo trasformato implicitamente in sede. Verificare migrazione dati e isolamento autorizzativo prima del primo ticket |
| SD1 — ticket e console IT | Modulo separato, migrazioni proprie, ticket manuali, stati/priorità/categorie, code/assegnazioni, uno o più dispositivi e una sede, cronologia/audit; link contestuali alle azioni RMM | SD0; transazioni/ETag/idempotenza e permessi granulari testati. Le azioni RMM richiedono anche capacità correnti e collaudo del relativo motore; non aggirano gate M3/M4 |
| SD2 — portale e collaborazione | Richieste proprie, commenti pubblici/note interne, allegati in quarantena, notifiche e KB iniziale con pubblicazione controllata | SD1; verifica identità utente/associazione dispositivi, storage privato, antivirus e worker. Gate contro fughe via email, ricerca, allegati e conteggi |
| SD3 — ingressi agente/email e storico interventi | Intake macchina autenticati, mailbox/thread verificati, deduplicazione e qualificazione; correlazione sessioni/job e riepilogo interventi | SD1–SD2; protocollo agente esistente, account verificati, gestione segreti mailbox e test spoof/replay/loop. Nessuna identità umana derivata dal solo mittente email |
| SD4 — allarmi, SLA ed escalation | Motore allarmi core persistente, ticket da occorrenze correlate, SLA/calendari versionati, timer durevoli e automazioni allowlist | SD0–SD3 e sviluppo/validazione del motore allarmi, oggi assente. Gate DST/pausa/riapertura/crash, deduplica escalation e prevenzione loop. Nessuna shell automatica |
| SD5 — conoscenza avanzata e AI opzionale | Ricerca autorizzata, KB e storico avanzati; riepiloghi e bozze AI opt-in, eventuali approvazioni umane specifiche per future azioni | SD1–SD4 stabilizzati, policy dati/provider/budget e valutazioni prompt injection. AI senza credenziali o accesso diretto ai comandi; produzione subordinata a restore, carico e sicurezza |

Le fasi non hanno date promesse. KB di base e storico non dipendono dall'AI. L'attivazione email, antivirus o provider AI richiederà configurazione operativa separata: nessun servizio esterno è stato collegato da questa documentazione. I test indicati nelle specifiche sono criteri futuri, non risultati già ottenuti.

## Percorso futuro Windows Agent e Tray

**Implementazione parziale avviata su richiesta successiva dell'utente.** La baseline verificata resta quella M1–M5; Service Desk non avviato. [Specifica tecnica](windows-agent-tray.md), [stato sorgenti 0.6.0](windows-agent-tray-implementation.md). Il MSI già compilato resta Agent-only; il sorgente MSI 0.6.0 include Agent+Tray, non compilato o installato. Tray distinta dalla console IT WebView2.

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

Pilot remoto: dopo autorizzazione esplicita Defender dell'operatore, risolto nei sorgenti il rifiuto mapping 409 dovuto alla risposta upstream nodes che omette meshid dentro il nodo. Preservati gruppo esatto/nodo unico/online/credenziale; deploy e prova runtime da registrare. Autorizzazione laboratorio non costituisce verdetto Microsoft o chiusura del gate produzione.

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
della 0.8.1. È in preparazione la 0.8.2, build di validazione senza modifiche
funzionali, per rendere disponibile una release successiva all’Agent 0.8.1 e
provare l’aggiornamento remoto sul PC pilota. Il publish resta subordinato al
workflow Windows. Firma Authenticode e invio Microsoft restano fuori da questa
implementazione; i checksum del server non attestano l’editore.
