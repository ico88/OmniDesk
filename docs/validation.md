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

### Gate MSI: installazione inizialmente fallita

Il run Windows 39203992128 ha compilato il pacchetto ma il test di installazione e terminato con MSI 1603. Nessun pacchetto di quel run e stato promosso come installabile. Aggiunti log verbose install/uninstall e raccolta eventi nel solo runner senza enrollment o segreti. La build precedente segnalava WIX1149: ServiceConfigFailureActions non funziona come atteso secondo Windows Installer SDK. Sostituita questa funzionalita con util:ServiceConfig di WixToolset.Util.wixext 6.0.2, come raccomandato da WiX (https://docs.firegiant.com/wix/schema/util/serviceconfig/), con riavvio dopo 30 secondi e reset dopo un giorno. Nuova dipendenza fissata; necessaria nuova build e prova lifecycle. La causa precisa del 1603 va confermata con il log, non dedotta dal warning.

Log completo del run diagnostico 39204355721 conferma errore 1939 durante ServiceConfigureFailureAction con Win32 5 (access denied), non un errore di avvio dell'eseguibile. La DACL del servizio e stata resa esplicita con SERVICE_ALL_ACCESS 0xF01FF per SYSTEM/Administrators e 0x20005 per Interactive, evitando generic bits nel descrittore. Il test ora verifica anche questi diritti amministrativi espliciti e rifiuta generic all/write su principal standard. Sono preservati query-config/query-status/read-control per Interactive, senza start/stop/change-config/delete/write-DACL/write-owner. Riferimento: https://learn.microsoft.com/en-us/windows/win32/services/service-security-and-access-rights. Attendere il test Windows prima di dichiarare risolto il gate.

### Consegna verificata Agent/Tray e kit LAN 0.6.0

- Commit runtime/installer: `98bba683420ff6ecdc14f9f401495b17291606b3`, branch `feature/windows-support-lan`, nessun merge nel principale.
- Windows Actions https://github.com/ico88/OmniDesk/actions/runs/39204693297 riuscito: parsing PS5.1, 10 test contratto Tray, publish Agent/Tray, self-test Windows (collector/DPAPI/ACL), MSI unico, installazione reale sul runner, servizio automatico LocalSystem, startup Tray, DACL standard e amministrative, crash del processo e recovery con nuovo PID, disinstallazione e assenza di residui servizio/startup. Console WebView2 compilata nel job separato. Nessun login/RDP/desktop interattivo o enrollment LAN eseguito dal runner.
- Linux Actions https://github.com/ico88/OmniDesk/actions/runs/39204693314 riuscito: build/test e smoke Chromium. Suite locale estesa a quota ticket e CRC corrotto: 23 casi xUnit e 3 Vitest passati (`/tmp/omnidesk-support-quota-tests.log`).
- Correzione recovery confermata anche dal precedente run 39204520750 con util:ServiceConfig; ultimo run aggiunge/verifica diritti servizio espliciti. I pacchetti dei run falliti non sono stati promossi.
- Artifact del run finale scaricato e checksum originali verificati. Kit `artifacts/windows-lan/OmniDesk.Windows.LAN-0.6.0.zip`: MSI, script LAN, sola CA pubblica, istruzioni e checksum. Nessun token, password, chiave privata o MeshAgent configurato.
- Verificati contenuti ZIP, CRC archivio e checksum di ogni file; helper packaging rifiuta artifact alterato senza produrre ZIP. Helper/istruzioni successivi non cambiano i binari del commit verificato.
- SHA256 ZIP: `c348f6349f7d60683c38e2cd6164080fbb60b2002403f244f5d152f9c0cf40e7`.
- SHA256 MSI: `18ec3c94312d1eae93a5e1feb444e78b639efad1f213604c42855be3bd62fc30`.
- API/dashboard avviate: confronto image ID container/immagine locale riuscito; ultima sonda TLS con CA, readiness, dashboard/asset e anonymous401 passata. Migrazione SupportTickets e backup predeploy gia registrati sopra.
- Gate ancora aperti: sessione utente Windows reale (Tray/IPC/screenshot), prova enrollment/heartbeat/ticket LAN da quel client, MSI repair/upgrade/rollback e firma, provisioning e motore desktop/terminale/file remoti reali. Il desktop remoto completo non e incluso nel kit. Nessun test fake o compile chiude questi gate.

### Avvio incremento remoto e download web 0.7.0

Sorgenti: provisioning server/worker Windows, vendor nell'MSI, overlay LAN e download web. Prima cross-build Agent/API e migrazione RemoteProvisioning generate con successo; suite PostgreSQL con provisioning fake e download protetto passata (23 casi xUnit complessivi; scenario composto esteso). Il fake verifica soltanto contratti/autorizzazione, non MeshCentral reale. Runtime 0.7 e Windows ancora da verificare. Client concordato: Windows 10, IP 192.168.1.160, installazione amministrativa disponibile; nessun accesso interattivo a quel PC in questa sessione.

### Prove reali motore e download web

- Download web implementato e pubblicato nella sezione Dispositivi (amministratori): manifest versione/hash, verifica SHA256 sullo stesso stream prima dell'invio, file ZIP autenticato e audit. Smoke Chromium isolato esteso a download effettivo e rifiuto viewer, passato (`/tmp/omnidesk-07-browser.log`). Il fixture ZIP esiste soltanto nello smoke; il server LAN serve il kit verificato 0.6 finche il nuovo artifact non e promosso.
- Server API/dashboard aggiornati con backup `.backups/20261019T055223Z.EfwPhw`, migrazione RemoteProvisioning e readiness HTTPS passata. Configurazione locale COMPOSE_FILE abilita esplicitamente i tre file compose core/mesh/mesh-lan, non committata; motore pubblicato esclusivamente sull'indirizzo LAN :8086. Nessun firewall o servizio host modificato.
- Primo avvio reale ha rilevato il checker upstream che tentava di reinstallare compression 1.8.1, incompatibile con l'override sicurezza 1.8.2 gia nel lockfile. Aggiunta patch di build deterministica e verificata del solo pin del checker. Non rimosso l'override o fatto downgrade. Il provisioning CLI ora verifica il messaggio di successo e non solo exit code zero. Account servizio senza promozione a site admin, nessuna registrazione pubblica.
- Sonda .NET contro il motore reale, TLS con CA e senza bypass: `/tmp/omnidesk-real-engine-probe2.log` PASS autenticazione/control, provisioning gruppo e retry idempotente, rifiuto nodo inesistente e rimozione gruppo diagnostico. Nessun dispositivo fittizio o metrica creata nel database OmniDesk. Questa prova non attesta tunnel desktop o consenso sul PC.
- Prova reale ha individuato la risposta meshes correlata tramite tag (upstream non restituisce responseid per questa azione): adapter corretto mantenendo correlazione stretta. Verifica sorgente _agentNodeId ha inoltre rilevato l'output hash esadecimale; aggiunta conversione canonica con test. Non promuovere il primo MSI 0.7 finche questa correzione non e inclusa.
- Primo Windows run 39206635518 riuscito per build/payload/ACL/service demand/recovery/uninstall. Ulteriori controlli nativi e revoca persistente del gruppo in corso, da rieseguire prima della consegna aggiornata.

### Kit 0.7 scaricabile dalla UI e prova nativa Windows

- Windows Actions https://github.com/ico88/OmniDesk/actions/runs/39207328177 riuscito sul commit 742d13f1c6a82dc035613e2ee86d9b1e0c0df2b0. Oltre al lifecycle MSI: avvio effettivo di OmniDeskRemote sul runner, generazione identita nativa esadecimale nel database privato, arresto del motore da parte di un Agent privo di enrollment. La MSH fixture punta a loopback chiuso: nessuna sessione desktop, consenso o server reale coinvolto in quel test.
- Kit 0.7.0 del run scaricato, checksum verificati, pubblicato con manifest atomico in artifacts/windows-lan. Il pulsante UI autenticato ora serve quella versione; aggiornare la pagina Dispositivi per ricaricare il manifest. Nessun token nel kit.
- API/dashboard ricompilate e avviate con overlay LAN e CA pubblica; migrazione RemoteProvisioningRevocation applicata. Backup .backups/20261019T060351Z.ZUnfFR include DB/Identity/Caddy, dati/file Mesh e password servizio in directory privata. Copia Mesh a caldo, non attestazione restore consistente.
- Readiness HTTPS, dashboard/asset, anonymous401 e download ZIP anonimo401 passati dopo il deploy. Sonda motore reale precedente conserva il suo perimetro (control/provisioning), non il desktop.
- Suite locale con guard revoca gruppo e conversione identita: 28 xUnit e 3 Vitest passati. Guard non rimuove il gruppo finche le revoche dei link noti non sono confermate. Aggiunti script CI di major upgrade da baseline 0.6 verificata, repair con ripristino ACL e rollback di un MSI temporaneo modificato con errore deferred; artifact originale non modificato. Queste nuove prove attendono esecuzione.

### Semplificazione enrollment interattivo

Lo script chiede il token con Read-Host -AsSecureString quando manca
EnrollmentTokenFile. Staging privato e cleanup restano invariati; aggiunto launcher
LAN generato con CA/hash verificati e istruzioni senza file token manuale.
Python py_compile del packaging passato; prompt e parsing PowerShell 5.1 aggiunti
alla CI Windows, da verificare prima di promuovere il kit aggiornato.
Il run 39207940658 ha superato lifecycle ma fallito il nuovo test upgrade:
invocazione della proprieta COM indicizzata ProductState corretta tramite
InvokeMember/GetProperty. Non dichiarare repair/rollback superati fino al rerun.

### Kit con token interattivo: pacchetto script senza ricompilare MSI

- Run Windows 39209165420: parsing PS5.1, test prompt mascherato (token valido e
  rifiuto input invalido), contratti e lifecycle MSI passati. Il test aggiuntivo
  upgrade e fallito sulla lettura dello stato prodotto: non chiude repair/rollback.
  Il rerun 39209623222 e ancora in esecuzione; non e fonte di un MSI promosso.
- Kit aggiornato mantiene byte per byte l'MSI del run riuscito 39207328177,
  commit 742d13f1c6a82dc035613e2ee86d9b1e0c0df2b0. Sostituisce solo lo script con
  quello del commit bfa378f (provenienza Git esatta registrata nel LEGGIMI e
  installerScriptCommit del manifest), verificato dal test prompt Windows.
- Launcher Installa-OmniDesk.ps1 contiene server, CA e hash MSI verificati e
  richiama lo script senza EnrollmentTokenFile. Richiede ancora PowerShell
  amministrativa: non e un wizard grafico MSI. Nessun token nel kit.
- Candidate verificato: CRC ZIP, tutti i checksum interni, uguaglianza binaria MSI
  con artifact originale e script con git show, assenza di chiave privata CA.
  Pubblicazione con sostituzione atomica ZIP/manifest tramite helper packaging.
- API ricompilata/avviata: sonda HTTPS readiness/dashboard/asset/anonymous401 passata.

Kit interattivo pubblicato: SHA256 ZIP
4123397bb86382afefac576a0d8d518f6491a2c1cf0798ed4e0111abc30ad728.
Verificati anche ZIP definitivo, manifest e checksum di tutti i file dopo la
pubblicazione; readiness HTTPS e asset passati. Nessun collaudo interattivo sul
PC Windows dichiarato. Il run esteso 39209623222 resta in esecuzione al momento
della pubblicazione dello script; l'MSI distribuito resta quello del run verde
39207328177.

### Primo enrollment LAN reale Windows 10

L'operatore ha eseguito il launcher interattivo e incollato il token: output
conferma TLS readiness, enrollment e verifica locale ACL/DPAPI. Verifica diretta
read-only PostgreSQL alle 2026-10-19 06:37:07 UTC: dispositivo DESKTOP-EK7AD7M,
ID bca1f63f-afde-4445-93ac-87829f422351, heartbeat ricevuto alle 06:36:53 UTC e
inventario alle 06:36:53 UTC. Questo conferma la comunicazione Agent/server LAN;
non dimostra ancora Tray, screenshot, consenso o sessione desktop remota.

### Correzione pipe Tray 0.7.1

PC reale: servizio Running e heartbeat recente ma nessuna pipe/evento utile.
Individuato un difetto deterministico in InteractiveLogons e controllo client:
WindowsIdentity.Groups esclude esplicitamente i gruppi SE_GROUP_LOGON_ID, quindi
non puo essere usato per estrarre o verificare il SID logon. Fonte primaria:
https://github.com/dotnet/runtime/blob/main/src/libraries/System.Security.Principal.Windows/src/System/Security/Principal/WindowsIdentity.cs

Corretto usando GetTokenInformation(TokenLogonSid), controllo di un solo SID
logon e relativi attributi, sia per enumerare sessioni sia per autenticare il
client impersonato. ACL SYSTEM/logon, verifica utente, sessione e SCM preservate.
Agent/Tray/MSI portati a 0.7.1; build Agent locale in SDK Docker passata senza
warning. Self-test Windows esteso alla lettura nativa e regressione Groups.
Attendere CI Windows e nuovo collaudo PC prima di dichiarare la Tray operativa.
Run esteso 39209623222 bloccato nel test MSI aggiuntivo: richiesta cancellazione,
non superato; aggiunto timeout per ogni msiexec test e rilascio view COM nel
fixture rollback. Non usa il PC destinatario.

### Hotfix 0.7.1: CI Windows riuscita

Run https://github.com/ico88/OmniDesk/actions/runs/39211896966 riuscito sul commit
e88a5c8: lettura nativa SID logon nel self-test Windows, parsing e prompt,
contratti, build Agent/Tray/MSI, installazione/crash/recovery/disinstallazione,
upgrade baseline 0.6 con stato locale preservato, repair con ripristino ACL e
rollback deferred con assenza servizi/startup residui. Log locale:
/tmp/omnidesk-071-windows-success.log. Linux run 39211897037 riuscito.
Il test upgrade precedentemente pendente e ora passato con rilascio view COM e
timeout espliciti. Queste prove non comprendono il desktop interattivo o la Tray
nella sessione reale dell'operatore. Artifact originale scaricato e da usare per
kit 0.7.1 (senza overlay script). Aggiornamento in-place, identita preservata.

Kit 0.7.1 pubblicato nella directory montata dal download UI. SHA256 ZIP:
799c30abf71a0fa462456b3734d2389f41c21b6a48e52610861a9c7b54e20a5b.
Verificati ZIP definitivo/CRC, tutti i checksum, uguaglianza MSI con artifact
originale e versione/hash manifest. Sonda HTTPS readiness/dashboard/asset e
rifiuto anonimo passata dopo la pubblicazione. Attesa prova Tray del client.

### Correzione runtime provisioning 503: permessi CA container

Le richieste reali POST remote-provisioning raggiungevano l'API ma fallivano con
WebSocketException; nessuna approvazione salvata, Agent riceveva correttamente
204 senza configurazione. CA pubblica host mode 0600 UID1000, montata con gli
stessi permessi nell'API UID1654: test -r falliva. Corretto esclusivamente il
file pubblico in 0644 e verificata leggibilita dall'API, poi riavviato il solo
container API. Nessun bypass TLS, cambiamento firewall o chiavi private.
Sonda reale con immagine API, UID/GID1654 e rete Docker proxy, stessa CA e
credenziale servizio montate read-only: PASS TLS/control auth, provisioning
idempotente e rifiuto nodo inesistente, rimozione gruppo diagnostico. Nessun
mapping dispositivo o autorizzazione amministrativa scritti fuori dagli endpoint.
Attendere nuovo tentativo UI dell'operatore e mapping Agent per verificare il
provisioning del PC; nessuna sessione desktop ancora attestata.

### Diagnosi PC reale: payload remoto assente dopo aggiornamento

Operatore conferma Agent 0.7.1+e88a5c8, cartella Remote owner SYSTEM e sole ACL
SYSTEM/Administrators, ma MeshService64.exe assente e nessun servizio
OmniDeskRemote. TCP LAN 8086 riuscito. Questo distingue il problema dal trust
server e dal difetto SID Tray. Non ancora attribuito con certezza a upgrade o
rimozione successiva sul PC. Prevista riparazione MSI con log, senza cancellare
identita o allargare ACL. Estesa CI: upgrade sia da baseline 0.6 sia da 0.7.0,
verifica esplicita servizio/payload dopo upgrade; fixture con eliminazione di
servizio e payload sul runner e verifica ripristino mediante /fa. Esito pendente.

### Errore repair 1706: sorgente MSI temporanea eliminata

Log reale operatore: 1603/1706 durante InstallFinalize; Windows Installer cerca
agent.msi nella cartella OmniDesk-Install-* eliminata dal finally del launcher.
Questo conferma un difetto del nostro script per manutenzione, non dimostra da
solo la causa originaria della scomparsa del motore. Corretto il cleanup:
MSI verificato e directory privata SYSTEM/Administrators vengono conservati dal
momento in cui inizia msiexec; token e CA temporanei vengono comunque eliminati.
Prima di tale momento il cleanup rimuove tutto. Test regressione sul finally
reale aggiunto alla CI Windows. Ripristino client previsto mediante recache v e
repair amus dalla copia originale dello stesso pacchetto, rinominata agent.msi.
Fonte flag v: https://learn.microsoft.com/windows-server/administration/windows-commands/msiexec
Esito CI/client ancora da registrare; niente promessa di repair riuscito.

### Causa confermata della scomparsa del motore: quarantena Defender

Eventi reali forniti dall'operatore confermano quarantena di MeshService64.exe
E servizio OmniDeskRemote alle 09:23 locali. Detection
HackTool:Win32/Meshagent.MBZ!MTB, ID 2147970383, intelligence 1.461.100.0, engine
1.1.26090.9. Non piu ipotesi di difetto MSI per la scomparsa post-riparazione.
Il difetto sorgente MSI/1706 rimane distinto ed e stato corretto. Nessun
falso positivo dichiarato e nessuna esclusione/protezione disattivata.
Dossier di revisione preparato in remote-engine-defender-review.md, nessun invio.
Windows run 39215503901 e Linux 39215503914 passati: include cleanup persistente
MSI, upgrade da 0.6/0.7 e repair payload/servizio mancanti sul runner isolato.
Questi esiti non superano il blocco antivirus del PC pilota.

### Correzione mapping dopo avvio reale del motore

Dopo autorizzazione manuale Defender sul PC di test: motore Running e POST
/api/agent/remote/node ricevute dall'API ma rifiutate 409. Verifica del sorgente
upstream meshuser.js realmente eseguito nel container mostra che la risposta
nodes raggruppa per meshid e cancella docs[i].meshid. Il nostro ContainsNode
richiedeva quel campo dentro il nodo, causando un rifiuto sistematico.
Corretto controllo su chiave esatta del gruppo, nodo unico, ID esatto e conn
bit1; eventuale meshid esplicito discordante resta rifiutato. Test regressione
con risposta reale compressa, gruppo/nodo diversi, offline e duplicati.
Nessuna associazione manuale o modifica SQL; il worker deve completare il POST
autenticato al prossimo retry. Attendere test/deploy/mapping runtime.

Correzione mapping verificata: 8 test MeshNodeResponseTests passati (risposta
compressa positiva, online flag, gruppo/nodo errati, offline, mismatch esplicito,
duplicati e lista vuota). API Docker compilata e avviata; 502 transitorio durante
startup, poi readiness/dashboard/asset/anonymous401 passati. Al retry automatico
il POST reale /api/agent/remote/node e riuscito 204 e MeshDeviceMapping esiste
per DESKTOP-EK7AD7M. Nessun inserimento SQL/manuale del nodo. Questo conferma
associazione autenticata con nodo online nel gruppo corretto; apertura desktop,
consenso e revoca restano prove distinte da eseguire con l'operatore.

### Incremento non presidiato e riordino UI: sorgenti e limiti

Migrazione RemoteAccessPolicy generata con EF10.0.13; sessioni esistenti marcate
con consenso richiesto (backfill), device UnattendedAllowed=false. Versione
policy attesa contro aggiornamenti obsoleti; attivazione/disattivazione solo
administration globale, verifica dentro lock dispositivo e dopo chiamata motore.
Modifica marca sessioni active revocation_pending e guard le revoca; versione
snapshot e ricontrollo finale negano link creati durante una modifica policy.

Mesh gruppo consent=7 (notifiche desktop/terminale/file), share per protocollo
consent=9/18/36 in modalita presidiata e 1/2/4 non presidiata. La combinazione
mantiene notifica e permessi separati. Engine account/group non esposti al browser.
Nessun cambio autoAccept globale, nessuna modifica Tray o privilegi Windows.
Primi test: 43 xUnit e 3 Vitest passati; aggiunta poi regressione cambio policy
durante creazione, UI responsive e diagnostica Update: attendere run finali.
Il test fake dimostra autorizzazione/contratto, non collegamento senza logon.

Windows Update: errore reale precedente solo "Windows Update process failed".
Avvio PS del worker senza ExecutionPolicy process era compatibile con il blocco
Restricted gia rilevato sul PC. Aggiunto RemoteSigned esplicito per il solo
processo dello script fisso locale e risultato con exit code/categoria sicurezza,
senza stderr raw. Group Policy prevale; nessuna policy globale modificata.
Test Windows di lancio fixture senza chiamate Windows Update aggiunto; Agent/Tray
MSI portati a 0.7.2. Nessuna scansione reale riuscita dichiarata finche verificata.

UI: SVG locali senza dipendenze nuove, ricerca e dettagli espandibili, stati
basati su risposte reali, controlli remoti disabilitati fino ad associazione,
aggiornamento stato ogni15s, apertura popup soltanto dopo link autorizzato e
fallback visibile. Test browser adattati al flusso reale con disclosure e ricerca;
screenshot previsti solo sull'ambiente isolato, nessun token in screenshot.

### Pubblicazione policy e nuova UI LAN

- Suite completa scripts/test.sh: 43 xUnit e 3 Vitest passati, con migrazioni,
  autorizzazione policy, default consenso, override client ignorato, stale version,
  fallimento motore senza cambio policy, tre protocolli non presidiati, revoca
  al ritorno presidiato e cambio policy durante creazione senza consegna link.
- Build API/dashboard Compose e config --quiet passate. Due run smoke browser
  (secondo dopo polish mobile) passati: permessi, account/grant, enrollment,
  rinomina/gruppo/revoca, ticket, download, ricerca e assenza overflow pagina a390px.
  Screenshot ambiente isolato in artifacts/ui-preview: desktop/mobile e admin.
  Controllo visivo ha individuato wrapping eccessivo nelle colonne mobile:
  corretti tabella dispositivi e pulsante Apri, verificati nel secondo run.
- Sonda reale UID1654/rete Docker: TLS/control, applicazione policy notifiche su
  gruppo diagnostico, provisioning idempotente, rifiuto nodo inesistente e rimozione
  gruppo passati. Nessuna modifica al gruppo del PC pilota da questa sonda.
- Backup predeploy .backups/20261019T075738Z.0TrfgG; migrazione
  20261019074810_RemoteAccessPolicy applicata. API e dashboard ricreate e avviate;
  readiness HTTPS/CA, dashboard/assets e rifiuto anonimo passati dopo deploy.
  Database pilota UnattendedAllowed=false/version0 confermato read-only: nessuna
  attivazione automatica. Provare dalla UI con amministratore del dispositivo.
- Desktop presidiato reale precedentemente confermato dall'operatore. Modalita
  non presidiata, notifica, lock screen/senza logon e revoca tunnel in quella
  modalita richiedono ancora collaudo interattivo; nessun test fake li sostituisce.
- Agent0.7.2 per lancio Update corretto ancora in CI Windows al momento del deploy
  web; vecchio risultato scan resta failed, nessuna scansione rilanciata da noi.

### Kit 0.7.2 verificato e scaricabile

Windows Actions https://github.com/ico88/OmniDesk/actions/runs/39219223914 riuscito
sul commit 89e366d. Passati prompt, cleanup con sorgente MSI conservata, fixture
PowerShell con policy process restrittiva ereditata (nessuna chiamata WUA),
contratti e self-test, build MSI, install/crash/recovery/uninstall e due cicli di
upgrade/repair/rollback da baseline 0.6 e 0.7.0. Il messaggio test stampava ancora
"major upgrade 0.6" in entrambi i cicli: le baseline sono esplicite nel workflow.
Linux run 39219223812 riuscito con suite e browser del commit.

Artifact originale scaricato e usato senza overlay per il kit 0.7.2. SHA256 ZIP:
27823081bf91fd4ec894472ebeb2b1635627e39f094f2ac101c074412724c27c.
CRC ZIP, tutti i checksum interni, manifest e MSI identico all'artifact verificati
anche dopo pubblicazione; sonda HTTPS readiness/dashboard/asset/anonymous401
passata. Windows Update reale dopo aggiornamento ancora da eseguire, il vecchio
job failed resta tale. Nessun test dichiara risolto un eventuale errore WUA/WSUS
che emerga dopo il lancio del processo.

### Assistenza: nome computer e navigazione contestuale

API staff /support ora proietta deviceName dal dispositivo gia correlato al
ticket, dopo il filtro tickets.view globale/gruppo/device esistente. La UI mostra
quel nome nella tabella/dettaglio e non l'UUID enrollment; la chiave UUID resta
per autorizzazione e apertura scheda. Risposte agent/support e contratto condiviso
Tray invariati per compatibilita con i client installati. Nome attuale della
console, inizialmente Environment.MachineName: nessuna risoluzione DNS CNAME.

Ricerca staff per oggetto/nome computer, filtro stato validato e max200 caratteri,
applicati prima del limite100 ma sempre dopo i vincoli di visibilita nella stessa
query. Nessuna ricerca globale di nomi o conteggi non autorizzati. Pulsante PC
visibile solo se gia presente nell'elenco dispositivi autorizzati, e status API
ricontrolla view. Migrazione non necessaria.

Suite PostgreSQL/Vitest iniziale passata: nome corretto, ricerca case-insensitive,
stati, wildcard percentuale letterale, input invalidi, ricerca scoped senza
esposizione del dispositivo fuori ambito. Browser esteso a nome senza UUID,
filtri, nome aggiornato dopo rinomina e apertura scheda contestuale; esito da
registrare dopo esecuzione.

Incremento Assistenza verificato e pubblicato: 43 xUnit e 3 Vitest passati;
Compose build/config riusciti. Primo browser run fallito sul collegamento del PC
appena registrato, per elenco dispositivi ancora in cache nel componente App;
corretto refresh autorizzato entrando in Dispositivi/Assistenza. Secondo run
Chromium passato: nome PC senza UUID nella tabella, ricerca case-insensitive,
filtro stato, risoluzione, nome aggiornato dopo rinomina e apertura scheda PC,
oltre ai controlli account/permessi/download preesistenti. Screenshot isolato
artifacts/ui-preview/support-device-names.png ispezionato; non sono dati live.

Backup predeploy .backups/20261019T081123Z.4mKM55; nessuna migrazione nuova. API e
dashboard avviate con la modifica, readiness HTTPS/CA, pagina/asset e rifiuto
anonimo passati. Contratti IPC e Agent invariati, nessun aggiornamento Windows
richiesto. Non dichiarati eseguiti nuovi collaudi screenshot/Update/remoto sul PC.

## Conversazione ticket — 19 ottobre 2026

Implementazione: endpoint staff messaggi con controllo tickets.view per dispositivo,
paginazione a cursore (100), risposte e cambi di stato nella stessa transazione
con controllo versione e lock dispositivo; scrittura con tickets.manage.
Screenshot inline sul medesimo endpoint autorizzato/auditato, no-store e nosniff;
il download preesistente rimane compatibile. Nessun nuovo endpoint anonimo.
400 esplicito per risposta mancante/stato/lunghezza; compositore web valida prima
dell'invio. Migrazione SupportConversation conserva l'ultima risposta legacy
senza inventare autore/data originale. Contratto Tray invariato: ultima risposta.
Esiti build/test/browser e avvio LAN registrati dopo esecuzione.

Verifiche riuscite: ./scripts/test.sh (43 xUnit, 4 Vitest), docker compose build
api dashboard, ./scripts/smoke.sh --browser. Chromium su ambiente isolato ha
verificato risposta obbligatoria prima della risoluzione, PNG inline decodificato,
ingrandimento/chiusura dialog, risposta persistente dopo ricaricamento thread e
stato Risolto, oltre ai controlli permessi/download/account esistenti.
Screenshot artifacts/ui-preview/support-conversation.png ispezionato: fixture
isolata, nessun dato del pilota. Log /tmp/omnidesk-ticket-chat-{tests,browser,docker-build}.log.

Pubblicazione LAN riuscita: backup .backups/20261019T082343Z.4wgbTE (Mesh copiato
 a caldo, non prova di restore consistente), Compose config, migrazione
20261019081809_SupportConversation applicata, API/dashboard avviate.
python3 scripts/verify-lan.py: HTTPS/CA, readiness, pagina/asset e rifiuto anonimo
passati. Nessuna modifica di stato su ticket reali durante il collaudo; nessun
nuovo test client Windows o chat bidirezionale in tempo reale dichiarato.

## Tray IPC 0.7.3

Osservata richiesta elenco ticket HTTP 200 sul server; errore generico della
Tray non identifica da solo la causa. Corretta contesa pipe (retry esclusivamente
Windows 2/231, cancellabile) e messaggi diagnostici. Test dedicati coprono
contesa/assenza transitorie, nessun retry su access denied e cancellazione.
Notifiche remoto gia presenti per sessioni non presidiate; comparsa effettiva
sul desktop Windows non verificabile da questo server.

Tray: ./scripts/test.sh riuscito (47 xUnit, 4 Vitest), build Tray cross-target
net10.0-windows riuscita senza warning/errori. Log /tmp/omnidesk-tray-retry-tests.log
e /tmp/omnidesk-tray-073-cross-build.log. MSI 0.7.3 in verifica su runner Windows
39223715334, sorgente ac674b69ccb8d0f49e7262b73919382e97a8539b.

Avviso remoto: esaminato il meshcore.js del motore installato: flag1 richiama
Toast, flag64 notifybar-desktop; createDeviceShareLink accetta tali flag.
Aggiunta barra per desktop (73 presidiato, 65 non presidiato), terminale/file
invariati. ./scripts/test.sh riuscito (47 xUnit, 4 Vitest), build API Docker
riuscita. Log /tmp/omnidesk-remote-notification-{tests,build}.log.
La presenza visiva del toast/barra sul PC rimane un collaudo interattivo separato.

API con barra remota pubblicata: backup .backups/20261019T083607Z.6pEV7X
(Mesh a caldo, nessuna prova restore), Compose config valido, API ricreata.
Prima verifica immediata HTTP502 durante avvio; ripetuta dopo startup con
successo HTTPS/CA/readiness, asset dashboard e rifiuto anonimo. Nessun popup o
controllo remoto reale sul pilota eseguito dall'agente. Link preesistenti invariati.

Windows 0.7.3: GitHub Actions 39223715334 completata con successo, compresi
installazione/recovery/uninstall e upgrade/repair/rollback dalle baseline 0.6.0
e 0.7.0. Artifact scaricato e checksum verificati dal packager. Kit LAN pubblicato
artifacts/windows-lan/OmniDesk.Windows.LAN-0.7.3.zip, SHA256
8ff6a2b1389b7d5b27da224427a68df069e3d0def2fc3fae3a20cf327647d957.
Manifest 0.7.3 e ZIP leggibili dall'UID API 1654; HTTPS/readiness/asset e rifiuto
anonimo passati. Log CI /tmp/omnidesk-windows-073-ci.log. Nessuna installazione
sul pilota o prova della lettura ticket/notifica remota interattiva dichiarata.

## Setup nativo e rimozione 0.8.0 — 19 ottobre 2026

Nuova UI WinForms richiede token mascherato, verifica CA pubblica tramite pin,
readiness HTTPS, installa MSI senza reboot e invia enrollment via stdin. Aggiunto
comando macchina revoke con firma dispositivo e endpoint self-service che revoca
credenziale, sessioni remote, provisioning e update in coda; MSI lo esegue come
SYSTEM prima di RemoveFiles e non rimuove durante major-upgrade. La rimozione
interrompe se il server non conferma (401 è già revocato). Kit aggiornato a Setup.exe,
MSI, CA e istruzioni, senza script eseguibili dall'utente. Queste sono proprietà
implementate nel sorgente, da convalidare sul runner Windows e sul PC.

Solo compilazioni cross-target eseguite: Setup net10.0-windows, Agent
net10.0-windows, API net10.0; zero warning/error. Comando:
`docker run --rm -u 1000:1000 -v "$PWD:/work" -w /work -e DOTNET_CLI_HOME=/tmp/dotnet mcr.microsoft.com/dotnet/sdk:10.0 sh -c 'dotnet build src/OmniDesk.Setup.Windows -c Release && dotnet build src/OmniDesk.Agent.Windows -c Release && dotnet build src/OmniDesk.Api -c Release'`.
Non eseguiti test, compilazione WiX Windows, installazione, enrollment o disinstallazione reali.

Build-only Windows Actions run 39229033734 (commit sorgente d0d9cce45cec5dbcb2206a8707a5afad8775cf57) riuscita: publish Agent/Tray/Setup, vendor remote hash, WiX MSI build 0 warning/0 error, upload artifact. Job test agent-msi e console erano skipped; nessun MSI installato.
Kit LAN `artifacts/windows-lan/OmniDesk.Windows.LAN-0.8.0.zip`, SHA256
b98e174782a6fcf750f9383def93943479cf07fe4d1e3045e3663d871846d109; l'API UID1654 legge ZIP e manifest. API con endpoint self-uninstall pubblicata sul server dopo backup .backups/20261019T092313Z.9SVv40; container API ricreato e avviato. Non è stata eseguita una disinstallazione reale Windows; la revoca resta da verificare sul PC pilota.

## Console RMM React/MeshCentral — 2026-10-19

Interfaccia dispositivo a schede con overview, console Mesh incorporata, ticket
per dispositivo, inventario software e aggiornamenti. Processi, servizi ed eventi
sono esplicitamente marcati non disponibili: l'Agent/API non espongono ancora
contratti autorizzati per questi dati. La console non legge il contenuto
cross-origin e non dichiara uno stream attivo sulla sola base del load iframe.
Le opzioni di monitor/qualità/scaling/input/clipboard rimangono nella toolbar
nativa MeshCentral. Il server mantiene la verifica RBAC; la pagina applica
validazione HTTPS `/sharing`, `no-referrer` e cleanup/revoca best-effort di un
grant malformato. La allowlist iframe MeshCentral è configurata solo per gli
origin OmniDesk LAN e dominio HTTPS configurati in Compose; non è abilitato
alcun login token.

La readiness include anche lo stato di abilitazione del provisioning: un nodo
mappato ma disattivato non viene presentato come pronto nella UI. Scenario remoto
verifica il flag attivo/inattivo dalla stessa API di disponibilità.

Verifiche:

- `docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD/src/dashboard:/work" -w /work node:22-alpine sh -c 'npm ci && npm run build && npm test'`: riuscito; TypeScript/Vite e 4 Vitest.
- `./scripts/test.sh`: 47 xUnit API/PostgreSQL isolati riusciti; include filtro `deviceId` staff e 403 senza `tickets.view`.
- `docker compose -f compose.yaml -f compose.mesh.yaml -f compose.mesh-lan.yaml config --quiet`: riuscito.
- `docker compose -f compose.yaml -f compose.mesh.yaml -f compose.mesh-lan.yaml build api dashboard meshcentral`: riuscito; API pubblicata, dashboard Vite e immagine MeshCentral ricostruite.
- `docker compose -f compose.yaml -f compose.mesh.yaml -f compose.mesh-lan.yaml build api dashboard`: ripetuto dopo la correzione dello stato di abilitazione; riuscito.
- `docker compose -f compose.yaml -f compose.mesh.yaml -f compose.mesh-lan.yaml run --rm --no-deps --entrypoint caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile`: configurazione LAN valida.
- `node --check entrypoint.mjs` nel container Node 22: riuscito. `npm ci` riporta 0 vulnerabilità.
- `./scripts/smoke.sh --browser`: riuscito su stack effimero isolato; Chromium verifica le dieci schede dispositivo, i controlli disabilitati dopo revoca, e regressioni account/grant/ticket/download già coperte dallo smoke.

Non eseguiti avvio/deploy live, sessione iframe sul server o stream video remoto,
né test interattivo sul PC Windows. Il deploy è escluso per
non interrompere sessioni remote attive. Le build non dimostrano la connessione
Mesh né la compatibilità visiva con ogni browser.

## Aggiornamento remoto Agent Windows 0.8.1 — 19 ottobre 2026

Implementato il job `agent.update` con autorizzazione globale `administration`,
controllo online/versione, MSI legato alla release LAN, verifica ZIP/MSI lato API
e hash MSI lato Agent. Il servizio usa un updater separato protetto da ACL, MSI
silenzioso senza riavvio Windows e ricevuta per confermare l’esito dopo restart.
Il manifest di release ora include hash e dimensione MSI. La UI dispositivo
mostra versione e comando, disponibile solo agli amministratori globali.

Verifiche eseguite:

- `./scripts/test.sh`: riuscito; 47 test xUnit con PostgreSQL isolato e 4 Vitest; TypeScript/Vite build passata. Include coda/lease/download MSI e rifiuto di admin solo scoped.
- `docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD:/work" -w /work mcr.microsoft.com/dotnet/sdk:10.0 dotnet build src/OmniDesk.Agent.Windows/OmniDesk.Agent.Windows.csproj -c Release -p:EnableWindowsTargeting=true`: riuscito, 0 warning/errori.
- Pubblicazione self-contained win-x64 single-file cross-target riuscita.
- Fixture isolata `package-lan-kit.py`: manifest MSI SHA-256/dimensione verificati; `py_compile` riuscito.
- `git diff --check`: riuscito.

Il test update/restart sul PC pilota resta da eseguire. La release pubblica non
è firmata Authenticode; i checksum non attestano l’editore. La versione 0.8.0
installata deve ricevere una prima installazione MSI 0.8.1 manuale prima di
poter auto-aggiornarsi.

## Aggiornamento remoto Agent Windows — workflow e test LAN — 19 ottobre 2026

GitHub Actions Windows run 39236894238 completata con esito positivo. Il runner
ha compilato MSI e Setup nativo; ha verificato installazione, recovery e
disinstallazione, poi upgrade/repair/rollback partendo da baseline 0.6.0 e 0.7.0.
Artifact unsigned `omnidesk-agent-unsigned` scaricato dal run e verificato dal
packager tramite SHA256SUMS. Kit LAN 0.8.1 generato in
`artifacts/windows-lan/OmniDesk.Windows.LAN-0.8.1.zip`; SHA-256 ZIP
`2ca42cb0467b581394cfa0e51abba6d0634817909adcc6c9abdc78202bedad51`, MSI
SHA-256 `234c06b20e52746cb617f3c97473d3d255a1b1a89ae09f94b30792844d9aad47`.

Deploy di test: backup `.backups/20261019T102208Z.YoY9yM` eseguito prima del
riavvio dei soli container API e dashboard. Database, Caddy e MeshCentral non
sono stati ricreati. Verifiche runtime dopo il riavvio: `scripts/verify-lan.py`
passato (readiness HTTPS con CA, dashboard/asset e rifiuto anonimo); MeshCentral
HTTPS su `192.168.1.233:8086` ha risposto 200; il dispositivo ha inviato un
heartbeat negli ultimi tre minuti. La release 0.8.1 viene letta dal volume
`/packages` senza riavvio aggiuntivo. Le route `/api/admin/downloads/windows`
e `/api/admin/downloads/windows/kit` restituiscono entrambe 401 senza sessione.

Non è stato eseguito il download autenticato tramite browser, né un update
remoto sul PC pilota: l'Agent 0.8.0 installato non include l'updater. Serve
installare manualmente il kit 0.8.1 dalla UI come bootstrap; un aggiornamento a
una release successiva sarà poi necessario per collaudare end-to-end job,
download autenticato, installazione silenziosa e ricevuta dopo il riavvio
dell'Agent. Il pacchetto resta unsigned: checksum e TLS non attestano l'editore.

## Build di validazione Agent 0.8.2 — 19 ottobre 2026

Versione Agent/Tray/Setup e WiX incrementata a 0.8.2 senza modifiche
funzionali, per fornire una release successiva con cui collaudare l'updater
remoto dopo bootstrap manuale dell'Agent 0.8.1. `./scripts/test.sh` passato:
47 xUnit PostgreSQL isolati e 4 Vitest, build dashboard inclusa. Cross-build
Agent Windows riuscito con 0 warning e 0 errori. Compose config e `git diff --check`
passati. GitHub Actions Windows run 39239145131 ha superato build MSI,
installazione/recovery/disinstallazione e upgrade/repair/rollback dalle baseline
0.6.0 e 0.7.0. Artifact verificato e kit LAN 0.8.2 pubblicato in
`artifacts/windows-lan/OmniDesk.Windows.LAN-0.8.2.zip`; SHA-256 ZIP
`11a8905ac1e4e431fd84f2680bfabab6d1b110ebc21e26a907ec4324a848fa00`, SHA-256
MSI `0e63696ed06fa17a03a5ac3ac6cd9fca0d1b78abc56754ebe6365d8b5d6df5d8`.
L'API nel container legge il manifesto 0.8.2 dal volume `/packages`; dashboard
HTTPS 200, readiness `ready` e download anonimo 401. Nessun job agent.update
accodato: nella coda pilota era in corso `install` Windows Update.

Il test end-to-end dell'updater resta aperto: l'Agent pilota 0.8.0 non include
l'updater e deve essere installato manualmente alla 0.8.1. Poi l'amministratore
potrà accodare la 0.8.2 e verificare stato finale/ricevuta dopo il riavvio del
servizio. Installare direttamente la 0.8.2 non testa l'aggiornamento remoto.

## Diagnostica RMM su richiesta — 19 ottobre 2026

Implementate le viste processi attivi, servizi Windows e warning/errori dei
registri System/Application. L'Agent raccoglie solo dopo richiesta autenticata;
il backend applica `diagnostics.view` globale o scoped, rivalida il grant quando
il dispositivo reclama il job e registra audit di accodamento/esito. I risultati
hanno limiti di numero e lunghezza. Il worker rifiuta tipi di job sconosciuti;
nessuna API permette di terminare processi o modificare/avviare servizi.

Verifiche: `docker compose build api dashboard`, cross-publish Agent `win-x64`,
`docker compose config -q` e `git diff --check` riusciti. `./scripts/test.sh`
passato con 47 test xUnit PostgreSQL e 4 test Vitest; lo scenario API verifica
deny per un utente senza il permesso, claim diagnostico e ricezione risultato.
Non eseguito deploy né runtime
sull'Agent Windows. Il server live non è stato riavviato mentre il dispositivo
pilota aveva un job Windows Update `install` in corso. Queste viste richiedono
la prossima release MSI dell'Agent.


## SD0 core — sedi e permessi per sede — 19 ottobre 2026

Migrazione EF Core `CoreSites` versionata con sito iniziale di backfill,
associazione di ticket esistenti alla sede del dispositivo, FK/index e rimozione
dei default dopo la migrazione dati. Non crea concessioni implicite. La API
amministra sedi, sceglie la sede di enrollment, assegna i dispositivi, filtra i
ticket e autorizza i grant per sede; la UI fornisce le relative viste e form.
La sede del ticket è uno snapshot storico e non viene riscritta quando il device
viene spostato.

`./scripts/test.sh` passato dopo le modifiche: 47 test xUnit su PostgreSQL e 4
Vitest; compilazione TypeScript/Vite inclusa. Il test integrato verifica che un
grant site-scoped mostri solo dispositivi di quella sede, che `view` non conceda
`remote.control` e che la revoca rimuova la visibilità. `git diff --check`
passato. Build API/Dashboard già riuscite durante l'implementazione; `docker compose config -q`
e `git diff --check` passati. Non è stato eseguito deploy né test di upgrade con
righe preesistenti sul database live. La pipeline Windows 0.8.3 run
39242100048 ha completato con successo build MSI/Setup e prove lifecycle; questo
run precede il codice SD0 e non valida la migrazione Sites.


## Outbox core transazionale — 19 ottobre 2026

Migrazione EF `CoreOutbox` e API event writes per enrollment, create/update sede,
modifica device e create/update ticket. Le scritture usano lo stesso SaveChanges
e la stessa transazione applicativa della mutazione. Payload ticket contiene solo
ID device/sede, stato e versione; i test verificano che descrizione, immagine e
risposta non vi siano serializzati. `./scripts/test.sh` passato: 47 test
xUnit/PostgreSQL e 4 Vitest; build TypeScript/Vite inclusa. `docker compose build
api dashboard`, `docker compose config -q` e `git diff --check` passati. Nessun worker o
consegna a sistemi esterni è implementato o testato.
