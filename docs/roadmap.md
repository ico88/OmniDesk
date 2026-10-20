# Roadmap

### CI Windows — timeout del test MSI

Le verifiche MSI avevano timeout per installazione e SCM, ma l'interrogazione
`MeshService64.exe -nodeid` poteva bloccare il job senza limite. Il test ora
termina quella chiamata dopo 15 secondi. La prima sonda in CI è fallita perché
`Start-Process` non ha restituito un codice uscita leggibile; ora usa
`System.Diagnostics.Process` con stream asincroni e diagnostica d'errore limitata.
La prima run con questa implementazione è rimasta oltre il tempo atteso nello
step MSI; ora anche la lettura di ciascuno stream ha un limite di 5 secondi.
La run live non viene interrotta e non include quest'ultima correzione. Vedi
[validation.md](validation.md) per gli ID e il limite delle prove.

Stato verificato il 2026-10-20 alle 05:53 UTC: la CI Linux `39375025444` è
verde. La run Windows `39372277004` risulta cancellata prima del completamento
della prova install/recovery/uninstall; non è stata interrotta da questo lavoro.
La run `39375025464` resta attiva nella matrice upgrade/repair/rollback, dopo
parse, contratti Tray, build MSI e install/recovery/uninstall riusciti. La
matrice Windows non è ancora dichiarata superata. Dettagli in
[validation.md](validation.md).

### Backup — convalida automatica prima del marker READY

`scripts/backup.sh` esegue ora `verify-backup.sh --staging`: checksum, riferimenti
DB/blob e restore applicativo isolato devono riuscire prima che l'archivio riceva
`READY`. Gli archivi staged vengono rifiutati dal verificatore in modalità
normale; il test shell controlla entrambe le condizioni. Smoke Chromium completo
passato con restore fixture attraverso la modalità staging.
Il backup riconosce anche installazioni legacy senza tabella allegati né volume:
in quel caso salva e verifica un payload vuoto; se lo schema allegati esiste ma
il mount persistente manca, interrompe il backup. Questa compatibilità è stata
verificata con backup e restore isolato del DB LAN prima dell'upgrade.
Questo riduce il rischio di archivi incoerenti quando la retention elimina
allegati durante la raccolta, ma non sostituisce backup off-site, cifratura,
retention dei backup o prova di disaster recovery su host separato.

### Privacy — retention metriche e inventario Agent

Implementate due policy indipendenti, `OMNIDESK_RETENTION_DEVICE_METRICS_DAYS`
e `OMNIDESK_RETENTION_DEVICE_INVENTORY_DAYS`, entrambe disattivate con default
`0`. Gli snapshot scaduti sono eliminati in batch limitati con audit del solo
conteggio; le metriche vengono rimosse solo quando il dispositivo è già
offline, mentre l'inventario segue la propria soglia. I record dispositivo non
vengono eliminati. Suite PostgreSQL 95/95, build dashboard/Vitest, configurazioni
Compose e build API sono passati. Anche lo smoke Chromium/restore isolato è
passato, includendo probe browser, ClamAV ed esercizio del ripristino. Non
attivato sulla LAN (default `0`); retention backup/off-site ancora da progettare
e validare.

### Service Desk — notifiche email escalation SLA

La proiezione outbox per breach ed escalation crea consegne email solo per destinatari con `tickets.view` e consenso email già attivo; l'EmailNotificationWorker rivalida account, scope e consenso prima di inviare tramite SMTP configurato. Test PostgreSQL aggiornato dimostra la coda per escalation duplicate-deduplicate, destinatario scoped e consenso esplicito. L'invio SMTP reale, deliverability e test operativi del provider restano da verificare; nessuna credenziale SMTP è stata aggiunta.

### Privacy — retention configurabile eventi di audit

Implementata una retention audit opt-in `OMNIDESK_RETENTION_AUDIT_EVENTS_DAYS` (default `0`, disattivata): oltre la soglia elimina in transazioni batch limitate solo eventi più vecchi, preserva gli eventi di purge e registra il conteggio senza dettagli o PII. Usa l'indice `AuditEvents.At` esistente, non richiede migrazioni né modifica i dati live finché non viene configurata una soglia. Test PostgreSQL dedicato e suite 94/94, build dashboard e 6/6 Vitest passati localmente. Restano da definire e verificare separatamente retention telemetria/backup, snapshot off-site e requisiti normativi prima di abilitare una policy in produzione.

### Privacy — retention del contenuto dei ticket risolti

Implementata nel branch una policy separata opt-in, `OMNIDESK_RETENTION_RESOLVED_TICKET_CONTENT_DAYS` (default `0`, disattivata). Dopo la soglia, ticket `resolved`/`closed` perdono oggetto libero, descrizione, risoluzione e corpi di messaggi pubblici/interni; numero, stato, date, metadati, eventi di workflow e interventi RMM restano consultabili. Flag persistenti evitano ripetizioni, il worker opera in batch transazionali e l'audit registra solo conteggi. La migrazione è versionata. Suite PostgreSQL 93/93, build dashboard e 6/6 Vitest, build Docker e smoke runtime Chromium/restore sono passati localmente. Non pubblicato sulla LAN. Retention allegati, intake, contenuti ticket, audit, telemetria e backup restano policy indipendenti.

### Telemetria — transizioni di presenza Agent

Lo stato operativo Online/Offline continua a derivare dall'ultimo heartbeat firmato; un worker persistente ora inizializza la baseline senza creare eventi retroattivi e registra una sola voce audit al cambio, con target device e senza dati telemetrici. Dispositivi revocati sono esclusi. La soglia (180 secondi) e la frequenza (30 secondi) sono configurabili e allineate alle API di stato e aggiornamento. La campanella mostra i dispositivi offline e collega alla scheda dispositivo; la query è filtrata in database per i dispositivi visibili all'utente e l'allarme sparisce al ripristino. Gli eventi offline sono persistiti tramite outbox; gli utenti autorizzati ricevono una notifica web e, solo con preferenza email attiva, un messaggio SMTP che rivalida visibilità e consenso. Suite PostgreSQL 93/93, dashboard 6/6, smoke Chromium/restore e build Docker sono passati localmente. Restano altri canali esterni, retention della telemetria e pubblicazione LAN.

### Allarmi email dispositivi offline

Le transizioni offline inseriscono un evento outbox nella stessa transazione dell'audit. Il consumer crea notifiche solo per utenti con `view` sul dispositivo; l'invio email usa il consenso già esplicito dell'utente, rivalida al momento della consegna permesso, account, credential attiva e configurazione SMTP. Le notifiche web possono essere lette dalla campanella e l'amministratore può riprovare gli invii dead-letter senza vedere indirizzi. Il messaggio non include payload telemetrico. Test integrato PostgreSQL verifica filtro di visibilità e consegna con SMTP fake; 93/93 suite, build dashboard 6/6, Docker e smoke runtime Chromium/restore passati localmente. Il codice non è ancora pubblicato sulla LAN; SMTP reale e canali non-email restano da provare/implementare.

### Privacy — retention dei record intake email

Worker retention configurabile: i messaggi ancora pending vengono eliminati dopo 30 giorni; quelli scartati o promossi dopo 180, con riferimento intake rimosso senza cancellare il ticket. La data è quella server-side di ricezione; stati sconosciuti restano preservati. Batch transazionali limitati e auditano solo il conteggio, senza PII o payload. Test PostgreSQL 91/91, build API, configurazioni Compose e smoke runtime Chromium/restore sono passati localmente. La retention di ticket, allegati e backup rimane separata; nessun servizio LAN è stato toccato.

### Privacy — retention degli allegati e screenshot ticket

Implementata nel branch una policy opt-in `OMNIDESK_RETENTION_RESOLVED_TICKET_ATTACHMENTS_DAYS` (default `0`, disattivata): solo allegati e screenshot di ticket `resolved`/`closed` più vecchi della soglia vengono rimossi; metadati ticket e conversazioni restano. Gli allegati passano da uno stato persistente `retention_pending`, sono nascosti dalle liste e non scaricabili durante il purge; errori filesystem vengono ritentati e audit/log contengono solo conteggi o tipo errore. Migrazione versionata per il vincolo di stato. Suite API/PostgreSQL 92/92, build dashboard e 6/6 Vitest passati; nessun deploy LAN. Restano da definire la retention di conversazioni, audit, telemetria e backup e le verifiche runtime sul server.

### SD3 — promozione controllata dell’intake email a ticket

La dashboard consente a un operatore con `tickets.manage` di selezionare il dispositivo, rivedere/modificare oggetto e descrizione, impostare categoria/priorità e confermare esplicitamente che il mittente non è verificato. L’API crea un ticket `email` associato al dispositivo selezionato, senza collegare l’email a un account; la promozione è idempotente e transazionale, con routing, workflow, SLA, outbox e audit. Verifiche API/PostgreSQL 90/90, dashboard 6/6, build dei container e smoke Chromium isolato registrati in `validation.md`. Provider reale, binding verificato dei thread e invio/ricezione reply restano aperti; nessuna pubblicazione LAN in questo incremento.

Stato corrente sintetico: [consegna Windows/LAN](completion-status.md). Le sezioni successive conservano lo storico; gli stati iniziali progettato/non compilato sono superati solo dalle verifiche successive esplicitamente registrate.

### Incremento SD1 — Allegati ticket con quarantena (implementato nel branch, non pubblicato)

API, dashboard IT e portale supportano allegati fino a 10 MiB, quote per ticket, consenso esplicito per la condivisione pubblica, visibilità pubblica/interna, storage privato con hash e download negato finché ClamAV non registra una scansione pulita. Il worker usa claim PostgreSQL con lease, retry durevoli e audit; il servizio antivirus è fissato a ClamAV 1.4.5 via digest e non pubblica porte. Gli operatori con `tickets.manage` possono riaccodare una scansione fallita, senza poter sbloccare file infetti. La scansione pulita produce una notifica outbox a coda/assegnatario e collaboratori; quelle interne richiedono anche `tickets.note.internal`. Test API 83/83, build dashboard e 6/6 Vitest, configurazioni Compose e smoke isolato con scansione reale ClamAV pulita/EICAR sono passati localmente; lo smoke Chromium include upload self-service, consenso, scansione, download, revoca e conservazione dell'accesso al team IT. In più, `./scripts/smoke.sh --browser` crea un backup effimero con allegato, controlla il manifest e verifica il restore runtime di DB/blob/chiavi Identity/CA via HTTPS. È attiva la retention solo per eventi outbox consegnati da 180 giorni, con audit e salvaguardia retry/dead-letter/no-consumer; contenuti ticket e blob restano fuori. CI Linux `39336034038` ha rilevato la mancanza di pull dell'immagine Alpine nel restore; la correzione è stata validata nello smoke locale completo. CI Windows `39336034027` è verde, inclusi upgrade/repair/rollback MSI. La CI Linux della revisione corretta attende il push. Restano restore snapshot LAN/off-site, policy retention per ticket/allegati e prova portale con account reale. L'incremento non è distribuito sul server LAN.

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

Controllo sessioni: lo storico dispositivo mostra il nome visualizzato
dell'operatore. Un amministratore può richiedere la disconnessione temporanea
di una sessione desktop; il lease resta valido fino alla scadenza e l'operatore
può riconnettersi. La revoca definitiva rimane un comando distinto e auditato.
Test API/PostgreSQL e smoke Chromium verificano stato attivo, identità mostrata,
autorizzazione e messaggio di riconnessione; una sessione Mesh reale resta da
provare sul client Windows.

- M1: infrastruttura Compose/Caddy/PostgreSQL, setup Ubuntu, schema e migrazioni Identity, bootstrap, autenticazione e CSRF, utenti, permessi globali/gruppo/device, audit, dashboard e branding. Consegna iniziale: vedi validation.md per prove effettive e limiti.
- M2 (implementazione iniziale compilata; MSI compilato in CI; gate installazione Windows/firma ancora aperto): servizio Windows .NET con enrollment monouso, chiavi dispositivo, revoca, retry/backoff, metriche CPU/RAM/disco/rete/uptime, inventario; MSI unico firmato e aggiornamento sicuro. Test Windows e reti NAT differenti obbligatori.
- M3 (adapter compilato e permessi testati con fake; gate motore/Windows e audit node-forge aperti): MeshCentral, adapter server con mapping identità e gruppi, sessioni autorizzate e revocabili per remote/terminal/files; MSI coordina i due servizi. Test anti bypass dei permessi e WebSocket/TLS. MeshCentral non viene esposto da M1.
- M4 (coda/API testate, worker cross-compilato; collaudo WUA Windows ancora aperto): Windows Update scan/install, job firmati/idempotenti, reboot policy, scheduling e report.
- M5 (console cross-compilata, TOTP e layout testati; flag TOTP inizialmente false): console Windows WebView2 della stessa UI, dashboard layout per utente, TOTP setup/challenge/recovery/cifratura implementati e testati; hardening, retention, restore, upgrade e release firmata ancora aperti.

Gate dipendenza desktop remoto, verificato il 2026-10-20: `modules/remote-meshcentral/package-lock.json` risolve `meshcentral` 1.2.5 e `node-forge` 1.4.0. `npm audit --omit=dev` segnala GHSA-86w9-cpqp-85rv come high e `fixAvailable: false`; il registro npm non pubblica una versione MeshCentral successiva né node-forge oltre 1.4.0. Il [pull request upstream per il fix](https://github.com/digitalbazaar/forge/pull/1152) risulta aperto e l'[advisory GitHub](https://github.com/advisories/GHSA-86w9-cpqp-85rv) non elenca versioni corrette. Il gate release del motore rimane bloccante; non viene introdotta una patch crittografica locale.

Verifica release: `scripts/validate-release.sh` ricostruisce le immagini API e dashboard dal checkout prima dello smoke runtime, evitando di attribuire alla sorgente corrente risultati ottenuti da immagini Docker obsolete. La validazione resta isolata e la sonda LAN è read-only.

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

**SD0–SD4 hanno incrementi parziali implementati e verificati nel branch; nessuno chiude da solo i gate di produzione.** L'intake email è un webhook normalizzato e firmato, con coda inbox, scarto o promozione manuale a ticket senza associare il mittente a un account. La coda applica retention a 30 giorni per i pending e 180 giorni per i record terminali; ticket e backup hanno policy distinte ancora aperte. Il percorso non sostituisce M1–M5 né attesta il deploy LAN della revisione corrente. Specifiche in [Service Desk](service-desk.md) e [schema dati](service-desk-data-model.md).

| Fase | Risultato previsto | Dipendenze e gate di ingresso/uscita |
| --- | --- | --- |
| SD0 — basi comuni e progetto esecutivo | **Implementato nel branch:** registro sedi e Device.SiteId, concessioni site-scoped, snapshot sede ticket, outbox transazionale, worker notifiche durevole con retry/dead-letter/lock, purge auditato outbox e inbox email, storage allegati e scansione ClamAV. **Da completare:** consumer per tutti gli eventi, retention ticket/conversazioni/telemetria e lifecycle backup | Migrazioni versionate; gruppi non sono sedi. Il ticket conserva la sede storica. I purge preservano retry, dead-letter e stati intake sconosciuti. Backup DB/blob e restore runtime isolato sono testati nel branch |
| SD1 — ticket e console IT | **Parziale implementato:** intake Agent/Tray, stati e transizioni server-side, priorità P1–P4, categorie, code/site routing/membership, assegnazione, chat pubblica, note interne e link multi-device same-site. Workflow globali/per sede versionati; link RMM append-only; riepiloghi e collaboratori scoped. Allegati con quarantena ClamAV, retry autorizzato, notifica post-scan, backup blob e purge opt-in per contenuti di ticket risolti/chiusi | Suite PostgreSQL, Vitest e smoke Chromium; upload/consenso/scansione/download/revoca e restore isolato passano. La revisione corrente non è distribuita sulla LAN; restano restore snapshot LAN/off-site e policy per conversazioni/audit/telemetria/backup. Allegati non avviano azioni RMM |
| SD2 — portale e collaborazione | **Parziale implementato:** Tray locale mostra ticket e risposte pubbliche; dashboard IT separa conversazione e note interne. Portale web con associazione amministrativa, ticket/chat, screenshot PNG con consenso e allegati generici pubblici con consenso, quarantena e scansione antivirus. KB interna con bozze, pubblicazione, grant `knowledge.view` globale/sede/gruppo, revisioni scoped e ricerca autorizzata. | Suite API e smoke browser isolati verificano accesso, consenso e visibilità; prova sul portale con account reale resta aperta. La KB resta interna senza HTML/Markdown o allegati; restore completo e retention sono gate separati |
| SD3 — ingressi agente/email e storico interventi | **Parziale implementato:** intake macchina autenticato/idempotente; link session/job RMM e riepiloghi append-only; webhook HMAC-SHA256 anti-replay, mailbox allowlist e deduplica; inbox dashboard con scarto auditato e promozione manuale a ticket dopo scelta device e conferma mittente non verificato. Ticket di origine email senza `RequesterUserId`; retention intake server-side: 30 giorni pending, 180 terminale. **Da completare:** adapter provider, binding verificato di thread/reply e destinatari, risposta email con token monouso e protezione loop | Identità macchina separata da quella umana; mittente email non autentica il richiedente. Segreto webhook almeno 32 byte e allowlist obbligatoria. Payload ≤64 KiB e corpo ≤20.000 caratteri. I ticket promossi e i backup seguono retention distinta; provider reale non collegato |
| SD4 — allarmi, SLA ed escalation | Policy SLA globali/per sede, versioni immutabili, permesso dedicato, target iniziali con snapshot sui tre ingressi, completamento idempotente, pannello autorizzato, pause con recupero della scadenza, nuovi cicli resolution alla riapertura, breach persistente, notifiche scoped e soglie di escalation configurabili e deduplicate su outbox; calcolatore IANA/DST testato | PostgreSQL/API verificano scope, versionamento, pause, cicli, deduplica e destinatari. Build Docker e smoke Chromium/runtime isolato passano, compresi worker reali per breach, escalation e notifica. Non eseguito su LAN. Restano allarmi persistenti/escalation esterna e workflow di recovery; nessuna shell automatica |
| SD5 — conoscenza avanzata e AI opzionale | Ricerca autorizzata, KB e storico avanzati; riepiloghi e bozze AI opt-in, eventuali approvazioni umane specifiche per future azioni | SD1–SD4 stabilizzati, policy dati/provider/budget e valutazioni prompt injection. AI senza credenziali o accesso diretto ai comandi; produzione subordinata a restore, carico e sicurezza |

Incremento KB — ricerca autorizzata: API e dashboard cercano in titolo, descrizione e corpo, applicano grant globali/sede/gruppo prima del match e restituiscono al massimo 50 risultati senza corpo articolo. Draft consultabili solo da `knowledge.manage` globale. La ricerca PostgreSQL full-text/GIN, Markdown e il portale esterno non sono inclusi. Test PostgreSQL coprono contenuti e isolamento tra scope; smoke Chromium verifica la ricerca tramite UI. Nessun deploy LAN in questo incremento.

Le fasi non hanno date promesse. KB di base e storico non dipendono dall'AI. Provider email e AI richiedono configurazione operativa separata. Gli esiti effettivi sono registrati in [validation.md](validation.md); criteri non eseguiti restano indicati come gate e non sono risultati.

## Percorso futuro Windows Agent e Tray

**Implementazione nel branch: Agent e Tray distinti, pacchetto unico 0.8.9; collaudo client e gate di rilascio ancora aperti.** CI Windows verifica build, MSI, installazione, recovery, uninstall e upgrade/repair/rollback; le prove interattive su PC pilota, firma Authenticode e revisione Microsoft Defender non sono chiuse. Agent gira come servizio per-machine con ACL/DPAPI e recovery; Tray non elevata per-sessione usa named pipe ACL e protocollo tipizzato. Nessuna promessa di protezione assoluta contro amministratori locali o accesso fisico privilegiato. La Tray resta distinta dalla console IT WebView2. Stato corrente e gate in [completion-status.md](completion-status.md); le note storiche seguenti non sostituiscono tale stato.

| Fase | Consegna proposta | Dipendenze e criteri di accettazione |
| --- | --- | --- |
| WT0 — contratti e autorizzazione | Implementati nel branch: confini di privilegio, protocollo IPC tipizzato, identità pipe/utente e permessi server-side separati | Restano audit di sicurezza e verifica interattiva delle policy su Windows; il SID non sostituisce l'autorizzazione server |
| WT1 — lifecycle servizio | Implementati nel MSI unico: ACL servizio/binari/configurazione, DPAPI, auto-start, Service Recovery, heartbeat e uninstall elevato con revoca server | CI Windows copre install/recovery/uninstall/upgrade/repair/rollback. Restano prova account standard sul PC pilota e firma Authenticode; allarmi server dipendono da SD4 |
| WT2 — Tray e pipe | Implementati: Tray non elevata per logon, stato IPC, named pipe ACL, protocollo con metodi limitati e MSI Agent+Tray | Restano prove client di logoff/RDP, pipe e Tray chiusa mentre il servizio continua; CI package non sostituisce il test su PC reale |
| WT3 — assistenza e screenshot | Implementati nel branch: apertura e consultazione ticket propri, risposta, screenshot con consenso e identità dispositivo automatica | API/UI e CI package passano; restano prova interattiva Tray/pipe/screenshot sul PC pilota e gate di conservazione contenuti |
| WT4 — consenso remoto end-to-end | Policy unattended per-device separata e disabilitata per default, autorizzazione per capability, lease breve e revoca persistente | Restano prova non presidiata senza logon, revoca durante sessione, multi-sessione e reti NAT sul client; nessuna elevazione tramite IPC. Il gate Defender/Microsoft del motore resta aperto |

WT3 e WT4 hanno dipendenze diverse: assistenza remota dalla console non deve attendere l'intero Service Desk; ticket e screenshot richiedono invece i servizi pertinenti. Nessuna nuova attestazione di protezione anti-admin o collaudo Windows deriva dall'aggiornamento documentale.

### Registro storico degli incrementi Windows

Le note 0.6.0–0.8.x riportate sotto documentano decisioni e passaggi storici. Per lo stato corrente valgono la tabella WT aggiornata qui sopra e [completion-status.md](completion-status.md); le frasi storiche “non compilato”, “non implementato” o “in corso” non descrivono la revisione attuale.

Avanzamento sorgenti WT0/WT1/WT2 del 0.6.0: contratto IPC v1 read-only, stato heartbeat reale del servizio, WTS/logon/ACL/pipe locale, NotifyIcon non elevata e MSI unico con HKLM Run e recovery/DACL. In quell'incremento le verifiche non erano state eseguite; i successivi risultati sono documentati in [validation.md](validation.md) e nella sezione corrente sopra.

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
modifica. Il consumer `ticket-notifications.v1` proietta in modo idempotente le
notifiche ticket con retry, backoff e dead-letter. Una policy auditata rimuove
dopo 180 giorni solo eventi con almeno una consegna e tutte completate; eventi
senza consumer, retry e dead-letter sono preservati. Restano consumer per altri
eventi di dominio e policy di retention per contenuti. La prova locale è passata;
la CI del commit `da91205` è ancora in corso.


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
permessi e migrazioni verificate. Le policy SLA globali/per sede sono versionate
e i nuovi ticket ricevono snapshot dei target iniziali per primo riscontro e
risoluzione, calcolati sul calendario lavorativo con fallback globale. Le
risposte pubbliche dell'operatore e la risoluzione/chiusura completano i target
in modo idempotente e sono consultabili nel dettaglio ticket tramite il permesso
`tickets.view`. Il worker registra i target scaduti una sola volta, produce
audit e invia eventi outbox per notifiche autorizzate. L'amministrazione web
gestisce policy globali/per sede, versioni immutabili, calendari, festività e
stati di pausa. La pausa si applica solo prima della scadenza e sposta il
deadline alla ripresa; la riapertura crea un nuovo ciclo resolution mantenendo
la versione policy originaria. Test PostgreSQL, dashboard e smoke Chromium
isolato passano. Escalation e-mail e allarmi esterni restano aperti.

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
Il portale aggiorna ora elenco dispositivi, stato dei ticket e conversazione
aperta ogni 30 secondi, conservando l'ultima vista riuscita durante errori di
rete temporanei; resta disponibile l'aggiornamento manuale. Build TypeScript e
suite dashboard passano. Lo smoke Chromium ha verificato lo stato e una nuova
risposta pubblica arrivati senza ricaricare la pagina, oltre all'esclusione
delle note interne. Prova isolata, non ancora eseguita con un account reale.

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

### SD4 — notifiche email outbound (validato localmente; CI e provider reali restano aperti)

Preferenza individuale esplicita, SMTP MailKit solo TLS su 465/587 con password in secret file, coda lease/retry/dead-letter, autorizzazione rivalidata al momento dell'invio e messaggi generici senza conversazioni o note. Il link apre il ticket nel dashboard con autorizzazione server-side. Gli amministratori globali consultano gli stati senza destinatari/contenuti e rimettono in coda i dead letter con audit. Migrazione EF, 91 test API/PostgreSQL, build dashboard e 6 Vitest, build immagini, Compose e smoke Chromium/runtime isolato passano localmente; CI Linux `39360140407` e Windows `39360140450` passano per la versione outbound iniziale. Non inviato a provider SMTP reale e non distribuito sulla LAN; consegna at-least-once può duplicare una email se il processo si arresta dopo l'accettazione SMTP.

### Portale — avviso al richiedente quando risponde l’assistenza

Le risposte pubbliche degli operatori generano una notifica `ticket.staff.reply` per il richiedente, con autorizzazione rivalidata tramite ownership, account attivo, collegamento portale, credenziale dispositivo e sede attiva. L’email usa lo stesso consenso esplicito e contiene solo riferimenti generici al ticket, senza testo della risposta o note interne. Il centro notifiche distingue risposta dell’assistenza e risposta del richiedente; il click e il deep link `/?ticket=...` instradano l’utente al dettaglio nel portale se non ha accesso alla console operatori. Revocando il collegamento, la notifica scompare e la lettura viene negata. Suite API/PostgreSQL 95/95, build dashboard, Vitest 6/6 e smoke Chromium completo passano localmente; nessuna migrazione richiesta. Non distribuito sulla LAN; resta da provare con account cliente reale e configurazione SMTP reale.

### Panoramica rete dispositivo e caricamento MeshCentral incorporato

L’Agent invia gli adattatori di rete attivi insieme all’heartbeat: la dashboard
mostra nome, tipo, MAC e tutti gli IP unicast per ciascuna interfaccia, così
Wi-Fi ed Ethernet possono comparire contemporaneamente e i dati si aggiornano
con la telemetria. Il contratto è opzionale per mantenere compatibilità con
Agent precedenti e l’API applica limiti alla quantità e alla forma dei dati.
Il riquadro Rete compare appena arriva un heartbeat, anche prima del primo
inventario; Sistema e Archiviazione appaiono quando i rispettivi dati sono
disponibili.
L’avviso della console remota distingue il caricamento lento dell’iframe da un
errore del motore; la sessione incorporata resta attiva e l’avviso scompare al
caricamento. Suite API/PostgreSQL 95/95, Vitest 6/6, build API/dashboard,
publish self-contained dell’Agent `win-x64`, smoke Chromium con due interfacce e
frame ritardato, restore isolato completo, Compose e `git diff --check` passano.
Commit applicativo `160d1a7` distribuito su LAN per API/dashboard senza
migrazioni; probe HTTPS/CA, asset e rifiuto anonimo passano. Il kit Agent+Tray
0.8.9 è pubblicato nel download amministrativo, unsigned da laboratorio. CI
Linux `39378646434` e Windows `39378646489` passate. Restano il download
autenticato con account admin e il collaudo sul PC Windows pilota per confermare
heartbeat IP/MAC e caricamento remoto incorporato.
