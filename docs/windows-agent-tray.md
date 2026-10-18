# Componenti Windows: OmniDesk Agent e OmniDesk Tray

**Progetto futuro, esclusivamente documentale.** La baseline attuale contiene il servizio .NET e un MSI Agent unsigned compilato in CI; collector, DPAPI e ACL su directory temporanea sono stati verificati, non installazione o lifecycle del servizio. Non esistono ancora Tray, named pipe, MSI combinato, Service Recovery configurato o workflow ticket locale. Questa specifica non modifica M1–M5. La console IT WebView2 già compilata resta distinta dalla Tray per utenti finali.

## Componenti e privilegi

| Componente | Identità e responsabilità | Confine |
| --- | --- | --- |
| OmniDesk Agent | Servizio Windows .NET, installazione per-machine elevata, avvio automatico SCM; monitoraggio, inventario, heartbeat e operazioni RMM già autorizzate dal server | Indipendente da logon/logoff; nessuna UI nella sessione 0. LocalSystem è la baseline attuale; riduzione privilegi e service SID da valutare senza presumere compatibilità con WUA |
| OmniDesk Tray | Processo non elevato per sessione interattiva, icona/stato, richiesta assistenza, ticket propri e screenshot consensuali | Nessuna chiave macchina, accesso SCM privilegiato, arresto/configurazione/disinstallazione del servizio o esecuzione comandi |

La chiusura o il crash della Tray interrompe solo l'interfaccia di quella sessione. Il servizio continua senza utenti connessi. RDP e fast user switching richiedono istanze Tray separate per logon, senza scambio di ticket, screenshot o consenso fra utenti. L'avvio al logon viene registrato dal MSI con una modalità non elevata da scegliere e collaudare in WT2; niente password salvate o task interattivi SYSTEM.

## Servizio, recovery e credenziali

L'MSI dovrà impostare e verificare DACL del servizio, directory binari/configurazione, registry e chiavi. Utenti standard non ricevono SERVICE_STOP, SERVICE_CHANGE_CONFIG, DELETE, WRITE_DAC o WRITE_OWNER né scrittura sui binari. Non possono sostituire eseguibili, modificare recovery, alterare enrollment o disinstallare il prodotto per-machine senza elevazione. Query di stato minima può essere consentita. Non si concede un ACE deny generico a Users che blocchi anche amministratori; si applicano grant minimi espliciti. L'effettivo impedimento va provato con account standard su Windows, non dedotto dalla sola configurazione MSI. Riferimento: [diritti sui servizi Windows](https://learn.microsoft.com/en-us/windows/win32/services/service-security-and-access-rights).

SCM Service Recovery proposto: riavvio dopo 5, 30 e 120 secondi, reset del conteggio dopo 24 ore; niente reboot macchina o comando arbitrario come recovery. Distinguere arresto amministrativo intenzionale, crash e uscita con errore; configurare esplicitamente le failure actions per gli errori non-crash ammessi. Errori irreversibili di identità/revoca devono fallire in sicurezza senza loop di enrollment. Recovery non rileva da solo deadlock o rete assente: health locale e rilevamento server restano separati. Installazione/upgrade devono verificare e ripristinare la policy prevista. Riferimento: [failure actions MSI](https://learn.microsoft.com/en-us/windows/win32/msi/msiserviceconfigfailureactions-table).

Baseline segreti: DPAPI LocalMachine più ACL SYSTEM/Administrators, owner e controllo reparse point già presenti. DPAPI macchina da sola non sostituisce ACL. Alternativa futura: chiave privata non esportabile in Windows Certificate Store LocalMachine, ACL al servizio e rotazione definita. Nessuna credenziale dispositivo in Tray, HKCU, pipe, log, proprietà MSI o riga di comando; token enrollment temporaneo da file protetto. Dati UI per utente separati; token umani eventualmente persistiti con protezione CurrentUser e logout/revoca, non sotto identità SYSTEM.

## Heartbeat, offline e anomalie

La baseline invia metriche circa ogni 60 secondi con jitter/backoff e considera online l'identità non revocata con ricezione negli ultimi tre minuti. L'estensione separerà heartbeat di salute da telemetria pesante: versione servizio, boot/istanza, ultimo esito, recovery e anomalie minimizzate. Ultima ricezione calcolata dal server, clock monotono per intervalli locali, clock server per scadenze. Distinguere servizio locale attivo, connessione server, enrollment revocato e stato remoto non aggiornato; la Tray non mostra online basandosi soltanto sull'esistenza della pipe.

Worker server futuro per transizioni online/offline con soglia configurabile, deduplica e isteresi. Offline non prova manomissione: può essere assenza rete, sospensione o arresto host. Audit di cambi stato, crash/recovery noti, ACL inattese, autenticazione pipe negata, identità revocata e tentativi anomali; buffer locale protetto e limitato, inoltro al ritorno rete, nessuno screenshot o segreto nei log. Un servizio terminato non può comunicare in quel momento: l'assenza viene rilevata dal server e gli eventi disponibili riconciliati al riavvio. Motore allarmi persistente ancora da implementare, come indicato in SD0/SD4.

## Named pipe locale: contratto e autorizzazione

Il servizio è server IPC. Pipe solo locale, con rifiuto client remoti e DACL esplicita SYSTEM/service SID e logon SID autorizzato per sessione. Nome sessione non costituisce un'autenticazione. Evitare Everyone/Anonymous e diritti generici che includano FILE_CREATE_PIPE_INSTANCE; concedere ai client soltanto i diritti necessari. Riservare la prima istanza e fallire chiuso se il nome è già occupato; prevenire server contraffatti e pipe squatting. La Tray verifica PID del server, corrispondenza al servizio registrato e identità/percorso protetto; nessun dato sensibile prima della verifica. Riferimento: [sicurezza named pipe](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights).

Il servizio ricava SID e sessione dal token del client tramite API Windows, non dal payload. Impersonazione solo per identificazione/access check, ripristinata in finally prima di qualsiasi lavoro privilegiato; nessuna esecuzione asincrona conservando impersonazione. Il client limita il livello di impersonazione richiesto. ACL non autentica il singolo eseguibile: un altro processo dello stesso utente può tentare richieste. Perciò ogni metodo è sicuro anche con client ostile; firma/percorso processo sono difesa aggiuntiva, non fonte di privilegi.

Protocollo versionato con framing a lunghezza, schema tipizzato, request ID, timeout e cancellazione, limiti dimensione/concorrenza/rate per SID/sessione, idempotenza delle mutazioni e rifiuto campi/metodi sconosciuti. Ricontrollo dell'identità per connessione/richiesta; invalidazione al logoff. Nessuna deserializzazione polimorfica arbitraria, path/URL da aprire come SYSTEM, script o handle ricevuti senza verifica. Screenshot trasferiti come byte limitati, mai come percorso utente da leggere con privilegi elevati.

| Metodo IPC proposto | Controlli |
| --- | --- |
| GetConnectionStatus | Stato minimizzato, nessuna credenziale o inventario riservato |
| CreateSupportIntake | SID/sessione reali, schema/quote/idempotenza, DeviceId derivato dal servizio; sede risolta dal server |
| ListOwnRequests / GetOwnRequest | Identità umana verificata e policy portale; non tutti i ticket del dispositivo condiviso |
| UploadConsentedScreenshot | Consenso esplicito della sessione corrente, correlazione richiesta, dimensioni/tipo/hash, quarantena e autorizzazione server |
| ReplyRemoteConsent | Sessione/grant/nonce specifici, logon destinatario, scadenza e uso singolo; nessuna modifica ai privilegi dell'operatore |

Non esistono metodi StopService, ChangeConfig, Uninstall, RunCommand o ReadSecret. Il servizio non è un proxy HTTP generico autenticato con la chiave macchina.

## Ticket e screenshot

“Richiedi assistenza” apre un modulo con oggetto, descrizione e dispositivo corrente non alterabile. Il servizio associa la propria identità macchina; il server risolve sede e permessi, senza fidarsi di DeviceId/SiteId ricevuti dalla UI. Se manca una sede valida o un utente verificato si crea intake da qualificare secondo [Service Desk](service-desk.md), non un ticket fuori dai vincoli.

L'account Windows locale non equivale automaticamente all'account OmniDesk. Occorre associazione verificata tramite login portale o flusso di pairing breve legato a account, dispositivo e logon, con revoca/scadenza; SID/nome dichiarati non bastano. Il servizio inoltra contesto utente limitato e verificabile insieme all'identità macchina: la chiave dispositivo non concede lettura dei ticket di altri utenti. Il backend applica policy portale a elenco, dettaglio e download. Senza identità verificata si espone solo la ricevuta minimizzata del proprio intake tramite capability limitata, mai lo storico completo del dispositivo.

Screenshot acquisito **dalla Tray non elevata**, dopo gesto esplicito, selezione schermo/finestra, anteprima e conferma di invio; possibilità di annullare/rimuovere e oscurare dati. Nessuna cattura periodica, background, di altre sessioni o del secure desktop. Consenso separato per ogni acquisizione; il consenso al controllo remoto non autorizza automaticamente allegati. Memoria/file temporanei per utente con durata minima, pulizia e retry limitati; niente coda persistente di immagini senza informazione e scelta dell'utente. Si applicano quote, quarantena e permessi allegati Service Desk. La prova del click non costituisce protezione assoluta da malware nella stessa sessione.

## Sessioni di assistenza remota

1. Operatore IT autenticato richiede capacità specifica sul device dalla console o dal ticket; server verifica stato dispositivo, ambito corrente e remote.control, terminal o files separatamente. Accesso al ticket e apertura richiesta non concedono tali capacità.
2. Il server crea richiesta breve con session ID, operatore, capacità, device, logon target, scadenza e nonce; audit senza URL bearer. Consent state separato dall'autorizzazione IT, grant legato a una sola sessione/capacità.
3. Per assistenza interattiva la Tray della sessione corretta mostra operatore, scopo e capacità e richiede accettazione esplicita. Rifiuto, timeout, Tray assente o sessione cambiata negano il percorso assistito. La risposta IPC non può scegliere un altro device/logon o ampliare capacità. Consenso revocabile; indicatore visibile durante la sessione.
4. Il backend ricontrolla permessi e validità prima di chiedere il link al motore. Link breve consegnato solo all'operatore, mai persistito nei ticket/log. Revoca, logout policy, scadenza o ritiro consenso devono chiudere realmente il tunnel tramite il motore; non basta aggiornare uno stato DB.
5. Accesso non presidiato, se introdotto, richiede policy esplicita per dispositivo/gruppo, capacità dedicata aggiuntiva, motivo e audit; default disabilitato, non fallback automatico se manca la Tray. Accesso al secure desktop/UAC è una capacità/policy distinta da collaudare, senza trasferire privilegi alla Tray.

La baseline adapter MeshCentral non dimostra questo workflow end-to-end. Il mapping sessioni Windows, i prompt/consensi nativi del motore e la revoca devono essere integrati evitando doppie conferme incoerenti o bypass: il motore deve applicare il grant autorizzato. Test su sessioni multiple e reti NAT obbligatori prima di dichiarare il controllo operativo. La revoca locale del consenso può essere consentita all'utente senza conferirgli diritto di fermare il servizio.

## MSI unico e disinstallazione autorizzata

Un solo MSI per-machine futuro installa Agent e Tray, binari in Program Files non scrivibili dagli utenti, servizio automatico, recovery, ACL e avvio Tray al logon. Il MSI non avvia UI elevata nella sessione dell'installatore; entrambi i binari e il pacchetto saranno firmati. Compatibilità Windows/.NET, upgrade code, versioni IPC compatibili, rollback e migrazione credenziali vanno collaudati. L'eventuale Mesh agent rimane un motore distinto da coordinare nello stesso pacchetto dopo i gate M3, non parte della Tray.

Procedura ordinaria proposta: amministratore OmniDesk con permesso dedicato richiede dismissione del device, chiusura sessioni e cancellazione job queued; server revoca identità e credenziali/link remoti applicabili, registra ricevuta audit; amministratore Windows elevato rimuove MSI e componenti di startup, poi elimina identità locale con scelta esplicita e verificata. Preservare l'identità durante upgrade non equivale a conservarla dopo dismissione. Il pacchetto attuale la preserva: nessun comportamento è stato cambiato qui.

Endpoint offline: registrare revoca sul server prima della rimozione quando possibile; altrimenti procedura break-glass elevata, ricevuta locale minimizzata e riconciliazione amministrativa obbligatoria sul server. Non considerare il device revocato perché è sparita la Tray o perché un uninstall locale è riuscito. Nessun segreto statico di disinstallazione nell'MSI e nessun meccanismo che impedisca in assoluto a un amministratore locale di rimuovere il software. Recovery viene sospeso soltanto durante manutenzione elevata autorizzata e ripristinato dopo upgrade/rollback.

**Limite esplicito:** queste misure separano privilegi e proteggono dagli utenti standard nel modello Windows ordinario; non garantiscono resistenza assoluta ad amministratori locali, compromissione kernel, policy Windows insicure o accesso fisico privilegiato.

## Verifiche future, non eseguite

Installazione/repair/upgrade/rollback/uninstall su VM Windows; account standard incapace di stop/config/change ACL/sostituzione binari/disinstallazione silenziosa; avvio senza login e crash recovery; Tray chiusa con heartbeat attivo; ACL/DPAPI e rotazione; pipe squatting/client remoto/SID contraffatto/fuzz/oversize/timeout/rate limit; isolamento RDP e logoff; ticket-only senza accesso RMM; screenshot senza consenso e di altra sessione rifiutati; ticket propri su device condiviso; revoca prima e durante la sessione, timeout consenso e unattended negato; dismissione offline e server revoke; assenza credenziali nei log/MSI. Le prove CI pregresse non sostituiscono questi collaudi.
