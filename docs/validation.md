# Stato e verifiche — 18 ottobre 2026

Documento cronologico: le sezioni M1 descrivono la prima consegna; M2–M5 registrano gli avanzamenti successivi. Lo stato corrente è nel riepilogo finale.

## Ambiente effettivo

Repository iniziale: solo README, commit f1647fa. Nessuna implementazione preesistente. Host Ubuntu 26.04.1 x86_64, Docker Engine 29.9.1, Compose 5.6.0. SDK .NET/Node assenti sull'host: build e test eseguiti in container, dopo autorizzazione all'accesso Docker. Nessun push o installazione di pacchetti host, nessuna modifica diretta a firewall o servizi di sistema.

## Risultati

| Prova | Risultato effettivo |
| --- | --- |
| `dotnet build OmniDesk.sln -c Release` nel container SDK 10 | Riuscita, 0 errori e 0 warning del compilatore |
| Migrazione EF Core Initial | Generata con dotnet-ef 10.0.13, applicata su PostgreSQL reale dai test e dal comando migrate |
| `./scripts/test.sh` | Passato: 1 scenario xUnit di integrazione con molteplici assert, 2 test Vitest; build dashboard inclusa |
| Auth e permessi | 401 anonimi/password errata, CSRF mancante 400, cookie login/logout, lockout dopo 5 errori, TOTP-enabled rifiutato; admin vs viewer, scoped default deny, gruppo, device, revoca immediata; ruoli custom con files indipendente da terminal/remote |
| Branding e audit | Persistenza PostgreSQL, validazione URL/colore/tema, rifiuto mutazione viewer; audit login negato, branding e grant revoke verificati |
| Immagini Docker API e dashboard | Entrambe costruite con successo; API runtime non root |
| `./scripts/smoke.sh` | Passato su container runtime: migrate, bootstrap, secondo bootstrap rifiutato, health ready, pagina HTML, HTTPS Caddy locale, login e attributi cookie Secure/HttpOnly/SameSite, parco vuoto, branding e audit |
| Configurazione produzione | `docker compose config --quiet` e `caddy validate` riusciti con dominio/email di esempio; nessuna richiesta certificato pubblico |
| Installer | `bash -n` riuscito; preparazione configurazione in directory temporanea, input valido, secret casuale, directory 0700/file 0444, riesecuzione conserva secret DB; nessun container avviato da questa prova |
| Dipendenze | npm audit senza vulnerabilità dopo aggiornamento; restore NuGet con warning trattati come errori; `dotnet list OmniDesk.sln package --vulnerable --include-transitive` non segnala pacchetti vulnerabili per API o test |
| Git | Commit locali separati con autore ico88 <ico88@hotmail.com>; nessun push |

## Problemi emersi e corretti

- Sandbox: daemon Docker inizialmente non accessibile e .git read-only; usate autorizzazioni esplicite. Identità Git inizialmente assente: usata quella fornita dall'utente solo nei comandi commit.
- Generazione migrazione iniziale richiedeva prima restore: sequenza corretta.
- Dipendenze iniziali Vite/Vitest e Pkcs vulnerabili: aggiornate prima della consegna; versioni NuGet esplicite e npm lockfile versionato. Pkcs è un override transitivo esplicito per la versione corretta.
- Smoke test inizialmente fallito con 500 durante login: TLS termina su Caddy, API vedeva HTTP. Configurati ForwardedHeaders solo per X-Forwarded-Proto dalla subnet proxy dedicata, limite un hop; smoke ripetuto con successo. X-Forwarded-For non viene accettato.
- Docker Compose monta secret come file host: permessi 0600 non leggibili dal processo non root. File 0444 dentro directory host 0700; smoke conferma lettura runtime. Non esporre questi file fuori dalla directory privata.

## Limiti alla consegna M1 (superati dove indicato nelle sezioni successive)

Non provati: apt install Docker e avvio systemd su Ubuntu pulito; setup --start end-to-end di produzione; DNS, porte pubbliche, rinnovo Let's Encrypt; rendering/interazioni in browser reale; deploy GitHub Actions (workflow preparato ma mai eseguito su GitHub); upgrade, backup/restore e carico. Lo smoke HTTPS usa una CA interna e il probe disabilita la validazione del certificato solo nel test: non certifica TLS pubblico. L'HTML è servito, ma questo non equivale a un test browser.

Non implementati: enrollment, agente/MSI Windows, telemetria/inventario, Windows Update, integrazione MeshCentral e console Windows. TOTP ha solo predisposizione Identity e protezione contro bypass del challenge non ancora disponibile. Layout widget per utente, upload logo, cambio/reset password, disabilitazione account, modifica/revoca ruoli globali e gestione completa gruppi/device restano fuori da M1. Le concessioni locali hanno create/list/revoke; i ruoli custom hanno create/list, non modifica.

Hardening residuo: chiavi DataProtection non cifrate a riposo (volume protetto da ACL); account PostgreSQL unico iniziale, da separare per migrazioni/runtime; immagini con tag major e non digest; lockout account e rate limit login condiviso per proxy; retention audit e metriche future. Nessuna attestazione di sicurezza per produzione.

Log locali non versionati: `/tmp/omnidesk-test-validation.log`, `/tmp/omnidesk-smoke-validation.log`; runtime smoke in `/tmp/omnidesk-smoke.*/runtime.log`. Container e reti di test arrestati/rimossi; secret temporanei restano nelle directory private per ispezione. Il cleanup dei test riguarda esclusivamente risorse create dai rispettivi progetti Compose.

## Avanzamento M2

Implementati enrollment monouso atomico, token con scadenza/revoca, identità RSA per dispositivo, JWT RS256 brevi con jti anti replay in PostgreSQL e cleanup; API snapshot metriche/inventario, gruppi e dettaglio dashboard.

`dotnet build OmniDesk.sln -c Release` (API, contratti, agente Windows, test) riuscito con 0 warning/0 errori. `dotnet publish ... -r win-x64 --self-contained true -p:PublishSingleFile=true` ha prodotto un eseguibile Windows ~74 MiB in artifacts (ignorato da Git). Migrazione DeviceEnrollment generata/applicata. `./scripts/test.sh` passa con scenario M2 aggiunto: consumo concorrente token (1 successo/1 rifiuto), hash non in chiaro, scadenza/revoca, firma errata e durata eccessiva, JWT replay rifiutato, separazione cookie/device, validazione metriche, inventario, permessi scoped, device revocato. Dashboard build e due test Vitest passati.

Trovato e corretto nei test: la cache del provider crittografico manteneva riferimenti RSA già disposti tra richieste; disattivata la cache per le chiavi a vita breve sia nel verificatore sia nell'agente. Nessun algoritmo crittografico personalizzato.

MSI e pipeline Windows aggiunti, ma MSI non ancora compilato né installato; esecuzione servizio Windows, DPAPI, misure WMI/native e reti NAT diverse non verificate su questo host Linux. Firma e aggiornamento automatico non implementati; M2 non supera ancora il gate Windows/produzione.

Push richiesto dall'utente: tentato `git push origin main`, bloccato da autenticazione HTTPS Git assente sull'host (`could not read Username`). Nessuna credenziale richiesta in chat; in attesa configurazione Git dell'host. I commit locali restano intatti.

## Avanzamento M3

Adapter MeshCentral .NET e migrazione MeshSessions compilati. Test di creazione/revoca, protocollo selezionato e divieto viewer passati dentro la suite PostgreSQL con IMeshCentralClient sostituito da FakeMesh: non è un test con il motore reale. Caddy mesh.Caddyfile validato. Motore opzionale fissato a meshcentral 1.2.5. Audit iniziale 5 avvisi; aggiunti override compression/qs. Node-forge senza fix resta un blocco per release pubblica. Sessioni effimere massimo cinque minuti e consenso device; revoca tunnel già aperti non verificata. Provisioning Mesh agent nell'MSI e test Windows/NAT restano aperti.

## Deployment LAN richiesto

Default Compose aggiornato a 192.168.1.233:8085 -> HTTPS Caddy 443 interno; nessun bind host 80/443. Verificato sul JSON prodotto da Compose. Configurazioni Caddy LAN e Mesh validate con `caddy validate`; overlay diretto separato conserva l'opzione Let's Encrypt per server senza Nginx. CA interna da distribuire ai client LAN, esempio Nginx preparato con verifica TLS upstream attiva. Nessuna modifica/reload Nginx o firewall eseguita. Raggiungibilità dell'IP e installazione CA sul server reale non verificate.

Immagine motore MeshCentral costruita con successo, ma audit resta a 2 high (node-forge e dipendente MeshCentral). Non avviata pubblicamente.

## Avanzamento M4

Migrazione UpdateJobs generata/applicata; soluzione compilata senza errori/warning, agente win-x64 self-contained ripubblicato. Suite PostgreSQL passata con verifica autorizzazione updates, RequestId idempotente (anche JSONB install), esclusione di due job attivi, claim una sola volta, lease errata rifiutata, risultato idempotente, cancellazione solo queued. Dashboard TypeScript compilata. I risultati nei test sono fixture: WUA/COM, PowerShell e installazione aggiornamenti reali non eseguiti su Linux. Worker separato mantiene heartbeat mentre esegue job; ricevuta locale impedisce riesecuzione dopo interruzione. Nessun riavvio automatico o modifica policy WSUS.

## Avanzamento M5 e verifiche finali

Soluzione completa (6 progetti) compilata con 0 errori/0 warning. Console Windows e agente pubblicati per win-x64. Warning WebView2 WindowsBase risolto rimuovendo il riferimento WPF importato dal pacchetto, non usato dalla console WinForms; nessun warning nascosto. Audit NuGet diretto/transitivo non segnala pacchetti vulnerabili per i sei progetti; npm dashboard senza avvisi. Il motore opzionale MeshCentral conserva separatamente i due avvisi high noti.

TOTP opzionale con default false: setup, conferma, challenge login, recovery monouso, disable/rotate; token Identity cifrati con DataProtection. Test PostgreSQL passati con codici reali Otp.NET: password senza MFA rifiutata, codice errato, codice valido, recovery valido e riuso negato, chiavi/recovery non in chiaro nel DB, cambio password, isolamento layout utenti. Un secondo host con flag TOTP=false rifiuta account già abilitato anche con password/codice corretti: nessun bypass.

Migrazioni UserDashboardLayouts e BindRemoteSessionNode applicate nella suite. La sessione remota conserva il node originale, verificato cambiando mapping prima della revoca con adapter fake. PostgreSQL Docker usa esplicitamente password/GSS Encryption Mode=Disable: rimosso il tentativo di caricare librerie Kerberos opzionali assenti nell'immagine runtime.

Prova browser Chromium aggiunta a smoke e CI: durante lo sviluppo ha rilevato stato checkbox controllato aggiornato troppo tardi; corretto aggiornamento immediato con rollback su errore e blocco mentre salva. Etichette dei select rese esplicite. Il test attende il completamento di salvataggi e transizioni prima di proseguire. Risultato conclusivo riportato nel riepilogo.

Push Git riuscito dopo configurazione credenziali dell'host. Per e7c7fff sono già verdi [build-and-test](https://github.com/ico88/OmniDesk/actions/runs/39151906354) e [windows-agent-package](https://github.com/ico88/OmniDesk/actions/runs/39151906326): MSI unsigned realmente compilato su runner Windows, non installato. La pipeline finale aggiunge self-test Windows in sola lettura (collector, DPAPI, JWT consecutivi), parser PowerShell 5.1 senza eseguire WUA e pubblicazione console. Non avvia né installa servizi Windows nei test.

### Gate ancora aperti

- MSI install/upgrade/uninstall e servizio effettivamente avviato su endpoint Windows; firma Authenticode e aggiornamenti automatici firmati.
- Mesh agent integrato nell'MSI, provisioning/mapping automatico, desktop/terminale/file su due reti NAT, chiusura tunnel e soluzione advisory node-forge.
- Scansione/installazione Windows Update reale, timeout COM, reboot policy/scheduling; lo script non è stato eseguito su endpoint reali.
- Installazione Ubuntu pulito, LAN reale 192.168.1.233, CA distribuita, vhost Nginx e certificati pubblici, backup/restore/upgrade e carico.
- Gestione completa lifecycle utenti/ruoli/gruppi, reset assistito MFA/password, upload logo e storico/retention metriche.

Nessuna attestazione di release pronta per produzione. Nessun segreto o binario aggiunto al repository. Nessun firewall o servizio Nginx modificato dal lavoro di sviluppo.

## Riepilogo corrente

- Suite PostgreSQL: 1 scenario xUnit composto da Security/Remote/Agent/Update/Account, passato; 2 test Vitest passati. Non sono presentati come decine di test indipendenti.
- Smoke container HTTPS e **Chromium reale passati**: login/logout, branding, persistenza layout dopo reload, creazione account, viewer senza comandi amministrativi e layout isolato; nessuna eccezione JavaScript. Il solo probe browser ignora la CA di test, non i client di produzione.
- Tutte le migrazioni applicate, API/dashboard runtime costruiti; soluzione completa con zero warning/errori; pubblicazioni win-x64 eseguite.
- MSI unsigned compilato in GitHub Actions Windows per e7c7fff. M5 bc928ea confermata da [CI Linux/browser](https://github.com/ico88/OmniDesk/actions/runs/39152823194) e [CI Windows](https://github.com/ico88/OmniDesk/actions/runs/39152823179), entrambe success: collector/DPAPI/JWT eseguiti realmente su Windows, script WUA parsato senza eseguirlo, MSI e console prodotti.
- LAN 192.168.1.233:8085 configurata e validata staticamente; nessun servizio Nginx o firewall modificato.
- Blocchi alla produzione elencati sopra restano aperti, soprattutto MeshCentral/node-forge, installazione MSI e WUA reali, firma e reti distribuite.

## Hardening ACL agente successivo a M5

Caricamento configurazione vincolato a owner SYSTEM/Administrators, ACL senza altri SID autorizzati e assenza reparse point. Scritture atomiche con file CreateNew univoci e permessi espliciti anche per script e ricevute update. Build agente passata (0 warning/errori). Test Windows aggiunto: directory temporanea protetta, scrittura file fidato, aggiunta ACL Everyone/read rifiutata, ripristino e cleanup. Non viene eseguita installazione MSI o modifica servizi. Correzione d791e38 verificata da [CI Linux/browser](https://github.com/ico88/OmniDesk/actions/runs/39153100385) e [CI Windows](https://github.com/ico88/OmniDesk/actions/runs/39153100502), entrambe completate con successo; il test ACL temporaneo è passato su Windows. Restano esclusi installazione MSI, servizi e WUA reali.


## Progettazione Service Desk

Aggiornamento esclusivamente documentale: specifiche, confini architetturali, schema logico PostgreSQL e fasi SD0–SD5. Nessun codice, migrazione, endpoint, dipendenza software o configurazione operativa Service Desk introdotto. Controlli su diff, riferimenti locali e distinzione proposto/implementato; nessun test funzionale Service Desk eseguibile. Le verifiche CI del codice precedente restano quelle riportate sopra; non equivalgono a un collaudo del modulo futuro.

## Progettazione Agent/Tray Windows

Modifica esclusivamente documentale: servizio e UI separati, recovery/ACL/credenziali, heartbeat e anomalie, protocollo named pipe, ticket/screenshot, autorizzazione e consenso remoto, MSI unico futuro e dismissione autorizzata. Diff e link locali verificati. Nessun codice, installer o servizio modificato; nessuna nuova prova runtime, installazione MSI o attestazione di sicurezza Windows. I criteri WT sono test futuri e non risultati CI esistenti.

Precisazione documentale: aggiunta matrice delle autorizzazioni remote, capacità future dedicate e rivalidazione dei grant; gate MSI per transazione unica, repair, rollback e startup multiutente. Verifiche eseguite: `git diff --check` e script Python con `pathlib`/`re` sui link locali di architecture, windows-agent-tray e roadmap (tutti risolti). Build, test runtime, migrazioni e Compose non rieseguiti perché codice, schema e configurazione non cambiano. Milestone corrente e relativi gate invariati.

## Hardening M5 — rotazione recovery code

Implementato aggiornamento security stamp nella transazione di rotazione, con rinnovo del cookie corrente dopo commit. Scenario Account esteso: riautenticazione errata rifiutata, vecchio codice inutilizzato negato, nuovo codice accettato una sola volta, stamp cambiato, audit presente e sessione corrente accessibile. Host di test separato per i login aggiuntivi evita di esaurire il budget del rate limiter dello scenario principale; limiti di produzione invariati.

Eseguito `./scripts/test.sh > /tmp/omnidesk-mfa-rotation-tests.log 2>&1` tramite autorizzazione Docker: compilazione API/test, migrazioni su PostgreSQL effimero e scenario xUnit passati; build TypeScript/Vite e due test Vitest passati. Prima esecuzione fallita per rilettura dello stream JSON nel nuovo test, corretta conservando il risultato deserializzato. `git diff --check` passato. Configurazioni Compose principale e test valide con `docker compose ... config --quiet`, usando dominio/email `.invalid` solo per interpolazione statica; nessun deployment. Nessuna nuova migrazione o dipendenza. Non eseguiti browser smoke, build console/Agent o collaudi Windows: componenti invariati. Invalidazione delle altre sessioni affidata al validatore Identity esistente entro un minuto; il test verifica il cambio stamp, non attende una sessione reale per un minuto.

## Avvio istanza LAN richiesto dall'utente

Verificato con `ip -brief address` che enp3s0 possiede 192.168.1.233. Creati configurazione `.env` e segreti locali esclusi da Git. Eseguito `./setup.sh --start > /tmp/omnidesk-lan-start.log 2>&1`: build immagini API/dashboard, migrazioni versionate, bootstrap del DB vuoto, avvio container persistenti. `docker compose ps`: PostgreSQL healthy, API/dashboard/Caddy attivi, unica porta host OmniDesk 192.168.1.233:8085. Servizi precedenti su 80/443 invariati.

Primo probe IP fallito in handshake TLS: aggiunto `default_sni` IP LAN al Caddyfile; `caddy validate` passato e reload del solo container Caddy eseguito. Esportato solo root CA pubblico con `docker compose cp`. Probe Python `/tmp/omnidesk-lan-probe.py` con CA esplicita, verifica IP/certificato e cookie jar: `/`, `/api/health/live`, `/api/health/ready` 200; login con CSRF 204; `/api/auth/me` e `/api/devices` 200. Password letta da file senza stamparla. Nessun bypass TLS, modifica firewall o servizio host. Accesso da altro dispositivo LAN e browser interattivo ancora da verificare; non chiusi i gate Windows, firma, MeshCentral e WUA. Questa istanza è destinata al collaudo, non attestata pronta per produzione.

## Incremento M5 — gestione concessioni dalla dashboard

Accesso LAN confermato dall'utente. Implementato componente React AccessManagement: elenco account/ruoli globali, creazione account con ruoli dinamici, creazione gruppi e assegnazione/revoca concessioni. Riusa API amministrative esistenti; nessuna modifica backend, migrazione o dipendenza. Messaggio esplicito sui permessi additivi; conferma prima della revoca e gestione errori server.

Verifiche: build immagine `docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .` passata (TypeScript/Vite); `npm test` in container Node: due test Vitest passati. `./scripts/smoke.sh --browser > /tmp/omnidesk-browser-smoke.log 2>&1` passato: PostgreSQL effimero, migrazioni/bootstrap e probe HTTPS; Chromium verifica creazione account/gruppo, assegnazione Viewer su gruppo, persistenza dopo reload e revoca, UI amministrativa assente per viewer, login/logout/layout/branding e nessuna eccezione JavaScript. Prima esecuzione browser fallita sul nome accessibile del select ruolo globale; aggiunte etichette ARIA esplicite ai select e ripetuto con successo. Concessione su dispositivo e ruoli personalizzati compilati, non esercitati nel browser in questo incremento; autorizzazione API coperta dalla suite PostgreSQL precedente. Nessun dato fixture introdotto nell'istanza LAN. `git diff --check` passato.

Pubblicazione LAN: `docker compose up -d --no-deps dashboard`, solo container dashboard ricreato; API, DB e Caddy mantenuti attivi. Controllo finale `curl --cacert omnidesk-local-ca.crt` su IP: dashboard 200 e `/api/health/ready` con stato ready. Ricaricare il browser per il bundle aggiornato. Nessuna modifica a servizi host o firewall.

## Lifecycle account e revoca remota persistente

Implementati IsDisabled, migrazione UserLifecycle, API/UI di sospensione/riattivazione, sostituzione ruoli globali e revoca sessioni web. Il cookie verifica stato/stamp a ogni richiesta; non attende più il controllo periodico di un minuto. Ultimo account abilitato con administration globale protetto; advisory lock serializza modifiche stato/ruoli, con ricontrollo autorizzazione chiamante dopo il lock. Le concessioni locali restano separate. Guard remoto ogni dieci secondi fra cicli, revocation_pending durevole e retry, conferma del motore e audit transazionali; riesame autorizzazione prima di consegnare link appena creato. Nessuna attestazione su tunnel MeshCentral reali.

Migrazione generata con dotnet-ef 10.0.13 in container SDK, build passata. `./scripts/test.sh > /tmp/omnidesk-user-lifecycle-tests.log 2>&1` passato dopo l'ultima modifica: uno scenario xUnit PostgreSQL esteso (Security/Remote/Agent/UserLifecycle/Account/Update), migrazioni applicate, tre test Vitest e build dashboard passati. UserLifecycle verifica autosospensione/ultimo amministratore negati, viewer negato su tutte le nuove mutazioni, CSRF obbligatorio, login sospeso negato, cookie invalidato immediatamente, vecchio cookie negato dopo riattivazione, cambio ruoli, revoca sessioni web e audit. Adapter fake: revoca remota fallita resta pending, retry riesce sul node originale, sessione ancora autorizzata resta attiva, perdita capacità ne provoca revoca. Test concorrente su più istanze API non eseguito; advisory lock verificato tramite percorso PostgreSQL, non stress test.

`docker compose build api dashboard` e `./scripts/smoke.sh --browser > /tmp/omnidesk-user-lifecycle-browser.log 2>&1` passati: migrazioni/bootstrap, HTTPS/login, account/gruppi/concessioni, UI sospensione/riattivazione, modifica ruoli globali e revoca sessioni web, viewer isolato e nessuna eccezione JS. Modifica finale guard/audit transazionale verificata dalla suite PostgreSQL successiva; browser precedente esercita gli stessi contratti UI. Compose standard e compose.backup-check validati staticamente; sintassi shell e diff verificati. Nessuna dipendenza applicativa aggiornata.

## Backup locale e prova restore DB prima dell'upgrade

`bash scripts/backup.sh > /tmp/omnidesk-pre-lifecycle-backup.log 2>&1`: backup completato sotto `.backups/20261018T194213Z.27O40X`, directory 0700 e contenuti riservati, dump DB e copie chiavi Identity/CA/configurazione. `.backups` esclusa sia da Git sia dal contesto Docker; esclusi anche certificati/chiavi/dump nella root dal contesto. Nessun segreto stampato o aggiunto a Git.

`bash scripts/verify-backup.sh .backups/20261018T194213Z.27O40X > /tmp/omnidesk-pre-lifecycle-restore.log 2>&1`: checksum passato, pg_restore completato su PostgreSQL 17 tmpfs senza rete/porte, sei migrazioni/un account/zero dispositivi nella fotografia ripristinata, container temporaneo rimosso. Non è una prova di login/TOTP dopo ripristino delle chiavi; restore applicativo completo, Mesh, off-site, cifratura e retention restano gate aperti. La produzione non è stata modificata dalla prova restore. Procedura in backup-restore.md.

Pubblicazione lifecycle LAN: build API finale passata, `docker compose run --rm migrate` applica 20261018194022_UserLifecycle, quindi `docker compose up -d --no-deps api dashboard`. Solo API/dashboard ricreati; DB healthy e Caddy mantenuti. Probe `/tmp/omnidesk-lifecycle-live-probe.py` con CA/IP verificati: dashboard 200, bundle contenente nuova gestione account 200, readiness 200/ready, `/api/auth/me` anonimo 401. Nessun account live sospeso o modificato dai test, nessuna password dell'utente utilizzata. Verificati diff, sintassi shell e 37 riferimenti locali della documentazione. Windows/MSI e chiusura tunnel reali ancora da collaudare su infrastruttura dedicata.

## Agent LAN 0.5.2

Implementati client HTTP comune senza redirect per enrollment/metriche/update, `connect-test --server` senza credenziali o enrollment, readiness prima del consumo token e `verify-enrollment --server` con controllo ACL/DPAPI/URL senza stampare chiavi. Script PowerShell elevato: staging ACL SYSTEM/Administrators, verifica CA pubblica e SHA256 DER, import LocalMachine Root solo opt-in, firma MSI valida oppure lab unsigned esplicito con hash atteso, installazione MSI senza reboot automatico, enrollment da file e verifica locale. Server LAN predefinito 192.168.1.233:8085. Nessun bypass TLS, porta inbound client, firewall o servizio dell'host modificato.

`dotnet publish src/OmniDesk.Agent.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/agent-win-x64` in SDK Docker passato dopo l'ultima modifica; versione binario 0.5.2. `/tmp/omnidesk-agent-lan-build.log`. `./scripts/test.sh > /tmp/omnidesk-agent-lan-tests.log 2>&1` passato: 13 casi xUnit (scenario PostgreSQL precedente più 12 casi del client connessione) e tre Vitest; validazione origine LAN, rifiuto origini HTTP/credenziali/path/query/fragment, readiness errata/redirect/503, limite risposta e nessuna Authorization nella richiesta diagnostica. Fixture HTTP nei test unitari, non telemetria operativa.

Harness .NET Linux sotto `/tmp/omnidesk-agent-connection-probe` compila lo stesso AgentConnection.cs e raggiunge realmente HTTPS 192.168.1.233:8085 con CA esplicita via trust del processo di test `SSL_CERT_FILE`: readiness passata (`/tmp/omnidesk-agent-lan-live.log`). Test senza CA rifiutato con AuthenticationException/PartialChain (`/tmp/omnidesk-agent-lan-untrusted.log`, uscita non zero per eccezione non gestita nel harness). Nessun callback permissivo introdotto. È una prova del codice HTTP/TLS su Linux, non del servizio/store Windows.

Parser PowerShell 7.5 in container passato (`/tmp/omnidesk-agent-lan-powershell.log`), senza eseguire script Windows. Pipeline Windows aggiornata per parser 5.1, build MSI 0.5.2 e pubblicazione script; non è stata eseguita una nuova pipeline. Tentativo `dotnet build installer/OmniDesk.Agent -c Release` su Linux fallito: WiX avverte che supporta solo Windows e rifiuta i percorsi del pacchetto. Non prodotti MSI nuovi e non eseguiti installazione, import CA Windows o lifecycle servizio.

Preparato ZIP diagnostico locale `artifacts/agent-lan/OmniDesk.Agent.LAN-Diagnostics.zip`: exe self-contained, script, sola CA pubblica, impronta DER, checksum e istruzioni. ZIP integro, nessun MSI o token incluso; artifacts esclusi da Git/contesto Docker. Il kit non installa autonomamente il servizio. Nessun nuovo deployment API necessario; codice server invariato. Gate desktop remoto/Mesh agent e Tray ancora aperti. Istruzioni in windows-lan-install.md.

## Gestione dispositivi e autorizzazione job — 2026-10-19

Implementati API/UI di modifica etichetta/gruppo con valori attesi, revoca idempotente, cancellazione job queued e stato remote revocation_pending. Identità revocata senza riattivazione implicita, dati storici e concessioni dirette preservati. Lock transazionale PostgreSQL per dispositivo condiviso da edit/revoke/queue/claim/cancel; verifica administration/updates ripetuta dopo il lock. Il claim rivalida RequestedBy e credenziale device prima di emettere la lease; job non più autorizzato cancellato con audit. Scadenza lease rilevata al claim auditata; nessuna promessa di cancellazione delle operazioni WUA già avviate o di chiusura tunnel reale. Nessuna nuova migrazione o dipendenza.

`./scripts/test.sh > /tmp/omnidesk-device-management-tests.log 2>&1` passato dopo l'ultima modifica al lock cancel: 13 casi xUnit (12 contratti connessione Agent più uno scenario PostgreSQL composto, esteso con DeviceManagement) e tre Vitest; build API/test/dashboard passate. Verificati global admin vs scoped, CSRF, input/gruppo inesistente, perdita visibilità/capacità dopo spostamento, conservazione grant diretto, job queued non autorizzato cancellato senza claim, remote pending, modifica stale 409 e due edit concorrenti con un solo successo. Concorrenza queue/revoke: nuovo job rifiutato oppure creato e cancellato; nessun queued sopravvive su device revocato. Revoca ripetuta conserva timestamp e blocca JWT/claim. Audit controllato; fixture solo su DB effimero.

`docker compose build api dashboard` e `./scripts/smoke.sh --browser > /tmp/omnidesk-device-management-browser.log 2>&1` passati: container/migrazioni/bootstrap/HTTPS, account e concessioni, enrollment fixture con sola chiave pubblica nel DB isolato, rinomina e cambio gruppo dalla UI persistenti dopo reload, revoca e pulsanti remoto/scan disabilitati, gestione amministrativa assente per viewer, nessuna eccezione JS. L'ultima revisione del lock cancel è stata coperta dalla successiva suite PostgreSQL; successivo testo conferma UI non cambia il flusso. Nessun fixture, token o dispositivo di prova inserito nell'istanza LAN.

Prima dell'upgrade: `bash scripts/backup.sh > /tmp/omnidesk-pre-device-management-backup.log 2>&1` passato, backup protetto `.backups/20261019T003936Z.kTEAx2` con dump/chiavi/CA/configurazione. Compose statico e diff verificati. Windows/MSI, motore remoto reale e Tray restano gate aperti; nessun cambiamento di firewall o servizi host.

Build finale API/dashboard passata, pubblicazione con `docker compose up -d --no-deps api dashboard`; DB healthy e Caddy esistenti mantenuti, nessuna migrazione necessaria. Probe HTTPS `/tmp/omnidesk-device-management-live-probe.py` con CA/IP verificati: readiness 200/ready, dashboard 200, bundle con nuova gestione dispositivi 200, `/api/auth/me` anonimo 401. Nessuna modifica a dispositivi o credenziali live per il probe. Ricaricare il browser prima del collaudo utente.

## Sorgenti Agent/Tray 0.6.0 — compilazione bloccata

Su richiesta di proseguire l'implementazione prima del collaudo completo: aggiunti progetti Windows.Ipc e Tray.Windows, protocollo framing/status in Contracts, stato reale heartbeat in AgentWorker, worker pipe per logon WTS con ACL/reject-remote/first-instance e autenticazione SID, client con verifica SCM/PID/owner/ImagePath, Tray asInvoker e MSI combinato 0.6.0 con Run/recovery/PermissionEx. Pipeline Windows e soluzione aggiornate. Test protocollo predisposti ma non eseguiti. Nessuna funzione ticket/screenshot/consenso remoto simulata; assistenza mostrata come non disponibile.

Tre tentativi di compilazione Docker via `exec_command` con require_escalated non sono stati eseguiti: il sistema ha restituito “Automatic approval review failed: Selected model is at capacity”. È un guasto della revisione, non un giudizio di pericolosità. Nessuna elusione del controllo, nuova build, MSI o attestazione test. Rimangono verificabili soltanto sorgenti/static XML/link/diff; nessun aggiornamento dell'istanza LAN o del kit 0.5.2. Stato e verifiche future in windows-agent-tray-implementation.md.

### Completamento richiesto remoto → ticket/screenshot → MSI (2026-10-19)

- Nuovo tentativo autorizzato di `docker run ... dotnet build src/OmniDesk.Tray.Windows -c Release -r win-x64`: comando non eseguito. Revisione automatica indisponibile: `Selected model is at capacity`. Nessun aggiramento della revisione; nessuna compilazione o pubblicazione conseguente.
- Modifica sorgente revoca remota: intento e audit persistiti prima della chiamata al motore, risposta 202 se manca conferma, retry tramite guard esistente; UI distingue richiesta da conferma. Aggiunto scenario fake per errore motore, persistenza pending, retry e idempotenza. **Non eseguito**.
- Aggiunto `installer/Build-OmniDeskWindows.ps1`: publish Agent/Tray, self-test Agent, build MSI, verifica versioni concordi e manifest SHA256 dei file di consegna; workflow Windows riusa lo script ed è avviabile manualmente. **Script non eseguito, nessun MSI 0.6.0 generato**. Build Windows e prova installazione sono verifiche distinte.
- Controllo statico `git diff --check` passato; non sostituisce compilatore, parser PowerShell o test autorizzazione.
- Remoto completo non consegnato: installazione Mesh agent, configurazione motore, consenso e chiusura tunnel reali da integrare e verificare. Ticket/screenshot non implementati. Istanza LAN invariata.

### Build Ubuntu f1 confermata dall'operatore; verifiche successive (2026-10-19)

L'operatore riferisce riusciti `docker compose config`, `docker compose build`, generazione immagini `omnidesk-api:local` e `omnidesk-dashboard:local`, `dotnet publish`, `npm run build` e seconda build con cache. Il precedente blocco della compilazione Docker è quindi risolto per il server f1. Questo risultato riguarda API/dashboard e non attesta compilazione dei progetti Windows, generazione MSI, test o deployment delle nuove immagini.

Verifiche eseguite in questa sessione:

- Configurazione Compose principale, test, smoke e backup-check: passata con `config --quiet`. Per smoke/backup usati percorsi placeholder solo per validare la configurazione, non file di segreti reali.
- `docker compose ps --format json`: API/dashboard/Caddy/database risultano running, database healthy. API/dashboard già avviati prima della build riportata: non attestata corrispondenza fra immagini appena compilate e container esistenti.
- `bash scripts/test.sh`: non avviati i test, accesso negato al socket Docker dal sandbox; log `/tmp/omnidesk-current-tests.log`. Nessuna revisione automatica con escalation ripetuta, come richiesto dall'operatore.
- Sonda HTTPS LAN: bloccata dal sandbox prima di aprire la connessione (`Operation not permitted`); nessun nuovo PASS runtime.
- GitHub CLI presente, origin `https://github.com/ico88/OmniDesk.git`; interrogazione workflow non riuscita per accesso rete. `gh auth status` segnala token non valido, ma l'accesso GitHub deve essere riverificato da ambiente con rete prima di concludere sulla validità della credenziale. Nessun workflow avviato, nessun push effettuato. Modifiche Windows presenti solo nel checkout locale.
- YAML workflow/Compose, XML progetti, sintassi Bash e Python, `git diff --check`: passati. Non equivalgono all'esecuzione dei workflow o al parser PowerShell.

Nuovi strumenti: `scripts/validate-release.sh` esegue configurazione, suite applicativa PostgreSQL/dashboard, smoke Chromium isolato sulle immagini esistenti e infine sonda HTTPS LAN. Non ricompila, non distribuisce e non migra il database di collaudo LAN. `scripts/verify-lan.py` controlla readiness JSON, dashboard/asset e rifiuto anonimo con TLS/CA verificati, senza redirect, login o mutazioni. Le migrazioni e gli account sintetici dello smoke restano limitati al progetto temporaneo isolato.

Il workflow Windows esegue parsing PowerShell prima della build, test contratti Tray, publish Agent/Tray, self-test Agent e singolo MSI con checksum. Preparato localmente, non ancora eseguito su GitHub Actions. Il runtime del servizio, consenso remoto, lifecycle MSI e LAN da Windows richiedono verifiche successive e distinte.

### Ripresa con accesso completo — ticket/Tray 0.6.0 (2026-10-19)

- Accesso Docker e GitHub ripristinato; `gh auth status` riuscito, senza rotazione o divulgazione credenziali.
- Baseline `bash scripts/test.sh`: 21 casi xUnit e 3 Vitest passati, comprendendo contratti Tray e scenario PostgreSQL composto. Build Tray iniziale: zero errori/avvisi.
- Nuovo modulo supporto: migrazione EF 10.0.13 `SupportTickets` generata; scenario PostgreSQL composto esteso a consenso screenshot, PNG invalido, idempotenza/conflitto, isolamento richiedenti, permessi ticket separati, download protetto, risoluzione/versione attesa e rifiuto della credenziale revocata. Suite aggiornata: 21 casi xUnit e 3 Vitest passati (`/tmp/omnidesk-support-tests2.log`). Numero invariato perché le verifiche di integrazione sono nello scenario composto.
- Agent/Tray publish self-contained win-x64 riuscito (`/tmp/omnidesk-support-windows-publish.log`), non esecuzione su Windows. Primo errore nome Capture in WinForms corretto in CaptureScreen. Un tentativo test concorrente con publish ha condiviso obj e fallito restore ILLink; rieseguito sequenzialmente con successo. Non eseguire build/publish/test contemporanei sullo stesso checkout/obj.
- Test remoto restano con fake. Sorgenti MeshCentral 1.2.5 controllati: relay gestisce removedDeviceShare per chiudere entrambe le estremità, ma questo non prova la chiusura di un desktop Windows reale. Advisory ufficiale node-forge https://github.com/advisories/GHSA-86w9-cpqp-85rv ancora senza patched version: gate rilascio pubblico del motore mantenuto.
- Autorizzati branch dedicato, commit/push senza merge e GitHub Actions Windows. Esito delle esecuzioni registrato di seguito quando disponibile. Nessuna prova MSI/desktop/screenshot/pipe reale dichiarata dal solo publish.

### Build Windows e pubblicazione LAN dell'incremento supporto

- Branch `feature/windows-support-lan` pubblicato senza merge. Primo workflow Windows https://github.com/ico88/OmniDesk/actions/runs/39203639959 riuscito: Agent/Tray, test contratto IPC, self-test Windows Agent e MSI unico generato; console Windows compilata nel job separato. Questo primo run non include ancora il successivo test automatico install/crash recovery/uninstall.
- Backup predeploy `.backups/20261019T051528Z.cJc84r` completato; build API/dashboard passata, migrazione SupportTickets applicata e soli container API/dashboard ricreati. Prima sonda immediata ha rilevato 502 durante avvio; ripetizione dopo avvio: readiness HTTPS con CA, dashboard/asset e anonymous401 passati. Nessun login con credenziali live né creazione di ticket sintetici nell'istanza LAN.
- Smoke Chromium esteso con ticket da credenziale RSA fixture sul solo database isolato, apertura nella console e risoluzione: passato (`/tmp/omnidesk-support-browser3.log`). Primo tentativo ha individuato label select non univoca; corretto label/for esplicito, quindi smoke ripetuto con successo.
- Protocollo esteso con rifiuto duplicati anche annidati e test payload/metodi. Suite finale: 23 casi xUnit e 3 Vitest passati (`/tmp/omnidesk-support-final-tests.log`).
- Aggiunto test Windows runner per installazione MSI, servizio SYSTEM/automatico, presenza Agent+Tray, startup HKLM, DACL servizio, crash/recovery e uninstall. Nuovo workflow da eseguire; non anticiparne l'esito. Questo test non effettua login standard/RDP, screenshot, IPC interattiva, enrollment LAN o controllo remoto.
