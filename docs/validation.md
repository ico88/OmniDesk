# Stato e verifiche — 18 ottobre 2026

### Migrazione GitHub Actions a Node 24 — 2026-10-20

- Migrazione workflow: `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/setup-node@v7` e `actions/upload-artifact@v7`. L'auto-cache npm è disattivata esplicitamente (`package-manager-cache: false`). Riferimenti upstream: [checkout](https://github.com/actions/checkout/releases), [setup-dotnet](https://github.com/actions/setup-dotnet/releases), [setup-node](https://github.com/actions/setup-node/releases), [upload-artifact](https://github.com/actions/upload-artifact/releases). Le release Node 24 richiedono runner Actions 2.327.1 o successivo.
- Linux [run 39358139780](https://github.com/ico88/OmniDesk/actions/runs/39358139780) — completato con successo sul commit `9af754b`, inclusi test, build Docker, smoke Chromium e restore isolato.
- Windows [run 39358139788](https://github.com/ico88/OmniDesk/actions/runs/39358139788) — completato con successo sullo stesso commit: setup-dotnet v6, workflow Agent/MSI e console; upload-artifact v7 ha caricato `omnidesk-agent-unsigned`, `omnidesk-console-unsigned` e `omnidesk-msi-diagnostics`.
- La CI prova la compatibilità dei workflow e degli artifact su runner GitHub, non equivale alla firma Authenticode o a una release MSI approvata. Nessuna pubblicazione LAN eseguita.

### Versionamento workflow installer Windows — 2026-10-20

- Corretto il job `native-installer`: legge la versione Agent dal progetto e la usa per nome/percorso artifact, sostituendo il valore statico 0.8.8; delega il packaging allo script comune, che verifica l’allineamento Agent/Tray/Setup/MSI, vendor hash e checksum. L'artifact è esplicitamente nominato `unsigned-lab`.
- Verifica locale XML/workflow: Agent, Tray, Setup e MSI allineati a 0.8.9; path/nome artifact derivati dall'output versione; nessun riferimento `windows-0.8.8` nel workflow. `git diff --check` — passato.
- Linux [run 39357044431](https://github.com/ico88/OmniDesk/actions/runs/39357044431) e Windows standard [run 39357044468](https://github.com/ico88/OmniDesk/actions/runs/39357044468) — passati sul commit `f50c5a4`, inclusi smoke/restore Linux e lifecycle MSI Windows.
- Dispatch lab [run 39357062002](https://github.com/ico88/OmniDesk/actions/runs/39357062002) — passato. Artifact `windows-0.8.9-unsigned-lab` scaricato; contiene MSI, Setup e `SHA256SUMS.txt`; `sha256sum -c SHA256SUMS.txt` ha confermato entrambi i file. Artifact CI di laboratorio, non firmato e non promosso alla LAN.
- Nessun servizio LAN modificato. Firma Authenticode, installazione client reale e gate MeshCentral/node-forge restano aperti.

### CI commit `2e41459` — 2026-10-20

- Linux [run 39356327778](https://github.com/ico88/OmniDesk/actions/runs/39356327778) — concluso con successo sullo SHA `2e4145995bf2a2071deb4b069caa9eddf0a3ad01`: test applicativi, configurazioni Compose, build Docker API/dashboard e `./scripts/smoke.sh --browser`, inclusi Chromium e restore isolato.
- Windows [run 39356327808](https://github.com/ico88/OmniDesk/actions/runs/39356327808) — concluso con successo sullo stesso SHA: parsing script, contratti Tray, build Agent/Tray/MSI e console, installazione/recovery/disinstallazione MSI e upgrade/repair/rollback da baseline fissata. Artifact unsigned di agent, console e diagnostica sono stati caricati sul run e restano artifact CI, non una release firmata/promossa.
- I due workflow verificano build/runtime isolati e lifecycle MSI sul runner GitHub; non coprono l’installazione/IPC interattiva sul PC Windows 10 `192.168.1.160`, né distribuiscono la revisione sulla LAN. Firma Authenticode e gate advisory MeshCentral/node-forge restano aperti.

### Gate dipendenza MeshCentral/node-forge — 2026-10-20

- `docker run --rm -v "$PWD/modules/remote-meshcentral:/work:ro" -w /work node:22-alpine npm audit --omit=dev --json` — riscontro: MeshCentral 1.2.5 → node-forge 1.4.0; advisory GHSA-86w9-cpqp-85rv severity high, range `<=1.4.0`, `fixAvailable: false`. Il codice di uscita 1 è l'esito atteso del controllo vulnerabilità.
- `npm view meshcentral version dependencies.node-forge` — registro: 1.2.5 con node-forge 1.4.0; `npm view node-forge version` — 1.4.0. L'advisory GitHub al controllo odierno non elenca release corrette e il [PR di correzione upstream #1152](https://github.com/digitalbazaar/forge/pull/1152) è aperto.
- Nessuna dipendenza o sorgente crittografica modificata. Il motore MeshCentral resta escluso dalle release pubbliche fino a una versione mantenuta e verificata; la build di laboratorio non chiude il gate. Nessuna installazione LAN effettuata.

### Freshness immagini nella verifica release — 2026-10-20

- `bash -n scripts/validate-release.sh`, Compose produzione/test e `git diff --check` — passati.
- `docker compose build api dashboard` — passata sulle immagini locali. `validate-release.sh` ora ricostruisce API e dashboard dal checkout prima dei test e dello smoke, impedendo che immagini preesistenti facciano verificare una revisione diversa. Questo comando non distribuisce né riavvia servizi LAN.

### Presenza persistente dell'Agent — 2026-10-20

- `./scripts/test.sh` — passato: 91 test API/PostgreSQL, build dashboard e 6/6 Vitest. Il flusso Agent inizializza la baseline senza audit sintetico, emette un audit offline una sola volta su sweep ripetuti e registra il ritorno online al successivo heartbeat. L'audit contiene solo l'ID dispositivo e l'azione; credenziali e payload non vengono registrati.
- Migrazione EF Core `20261020013520_DevicePresenceAudit` generata e applicata nello smoke isolato. `./scripts/smoke.sh --browser` — passato con API/worker, PostgreSQL, ClamAV, Chromium e restore completo database/blob/chiavi Identity/CA. Il worker hosted è partito nello stack; gli assert di transizione sono eseguiti direttamente contro PostgreSQL nella suite d'integrazione.
- `docker compose config --quiet`, `docker compose -f compose.test.yaml config --quiet`, `docker compose build api dashboard` e `git diff --check` — passati. Un tentativo di validare `compose.smoke.yaml` senza `SMOKE_SECRETS` non è applicabile fuori dallo script che genera i segreti temporanei; lo smoke completo sopra lo configura correttamente.
- Nessun servizio LAN modificato. La retention della telemetria e il deploy LAN sono ancora da fare; gli allarmi email/esterni restano fuori da questo incremento.

### Campanella con allarmi dispositivo scoped — 2026-10-20

- `./scripts/test.sh` — passato: 91 test API/PostgreSQL, build production dashboard e 6/6 Vitest. Il test Agent verifica che la campanella contenga un dispositivo offline autorizzato e non riveli un dispositivo offline fuori dallo scope dell'operatore.
- `docker compose build api dashboard` — passato sulle modifiche correnti. Dopo la build `./scripts/smoke.sh --browser` — passato: l'endpoint autenticato `/api/notifications/devices` risponde con lista, la UI aggiornata mostra le notifiche ticket e dispositivo, Chromium completa i flussi ticket, e il restore integrale di 46 migrazioni/database/blob/chiavi/CA riesce.
- Un primo smoke è stato avviato prima di ricostruire le immagini e ha restituito 404 sull'endpoint nuovo; è stato corretto eseguendo la build Docker delle immagini e ripetendo lo smoke completo, che ha passato tutti i controlli.
- Le notifiche sono in-app e limitate al perimetro `view`; email/integrazioni esterne e distribuzione LAN non sono incluse. Nessun servizio LAN è stato modificato.

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


## Controllo autorizzato servizi Windows — 19 ottobre 2026

Implementata capability separata `services.manage`, API queue/cancel/list con
check globale/scope dispositivo, audit e revalidazione al claim; l’Agent riceve
solo i tre comandi start/stop/restart e usa una policy condivisa che respinge
nomi malformati e servizi OmniDesk/Windows protetti. UI richiede conferma esplicita
e limita i controlli alla capability; ricevuta locale impedisce il retry automatico
dopo arresto anomalo.

`./scripts/test.sh` passato: 62 xUnit/PostgreSQL (inclusi default deny, validazione
servizio, idempotenza, claim/risultato e policy) e 4 Vitest. Cross-publish Agent
`win-x64`, build API/dashboard, `docker compose config -q` e `git diff --check`
passati. Workflow GitHub Actions Windows per il nuovo MSI 0.8.4 in corso. Il test
Agent non controlla servizi reali; nessuna operazione è stata inviata al PC pilota
e il deploy LAN è stato evitato.


## SD1 — priorità e assegnazione ticket — 19 ottobre 2026

Migrazione `TicketAssignmentAndPriority`: priorità persistente (default `normal`),
assegnatario Identity opzionale, FK Restrict e indici per sede/stato/priorità/data
e assegnatario. API consente elenco candidati solo con `tickets.manage` sul ticket;
sono inclusi account attivi che hanno sia `tickets.view` sia `tickets.manage`
nell’ambito storico del dispositivo/sede. La mutazione rivalida permessi,
ammissibilità e versione; audit e outbox registrano le modifiche senza contenuti
ticket. Dashboard aggiornata per elenco, dettaglio e selettori. Test coprono default,
default deny, ambito, assegnazione non autorizzata e aggiornamento versionato.

`./scripts/test.sh`: 63 xUnit/PostgreSQL e 4 Vitest passati. `docker compose build
api dashboard`, `docker compose config -q` e `git diff --check` passati. Workflow
Windows MSI 0.8.4 ha superato build, installazione, recovery e disinstallazione;
upgrade/repair/rollback è ancora in corso. Nessun deploy o test di upgrade su DB
LAN con record reali.

La console filtra ora ticket autorizzati per priorità e email assegnatario. La
suite copre filtri positivi e priorità non ammessa; non si effettuano conteggi o
risultati prima dell’applicazione dei normali filtri di autorizzazione.


## Numero ticket e migrazione con record esistenti — 19 ottobre 2026

`TicketReadableNumber` aggiunge `SupportTickets.Number` come Identity PostgreSQL
univoco. La Tray e la dashboard mostrano lo stesso numero `OD-000123`, mentre
UUID resta l’identificatore API interno. Test di migrazione crea uno schema
PostgreSQL isolato, applica tutte le migrazioni fino a `ProcessControlPermission`,
inserisce ticket/dispositivo/sede preesistenti, applica la migrazione nuova e
verifica numero assegnato al record storico e numero distinto al ticket successivo.
`./scripts/test.sh`: 63 xUnit/PostgreSQL e 4 Vitest passati. Upgrade eseguito solo
nello schema effimero dei test, non sul DB LAN. Pacchetto Windows aggiornato a 0.8.6;
CI completa su questa revisione ancora da attendere.


## Categorie ticket core — 19 ottobre 2026

`TicketCategories` introduce `Category` con default `general` e indice sede/categoria/stato/data. API consente solo `general`, `hardware`, `software`, `network`, `account`, `other`; la filtrazione avviene nella query autorizzata. La console filtra la categoria e la Tray la seleziona all'apertura; è immutabile dopo l'invio per conservare la ripetibilità idempotente. L'evento outbox `ticket.created` include la categoria senza contenuti utente. `./scripts/test.sh`: 63 xUnit/PostgreSQL e 4 Vitest passati. Cross-publish Agent, Tray e Setup `win-x64`, `docker compose config -q` e `git diff --check` passati. Nessuna migrazione o pubblicazione sul database LAN effettuata. Pacchetto Windows candidato 0.8.7 e workflow CI da attendere.


## Code ticket — sorgenti in verifica

`SupportQueues` introduce code globali con nome normalizzato univoco, audit/outbox per creazione e disattivazione morbida. La gestione richiede `administration`; elenco code richiede almeno una capability ticket. Assegnazione/rimozione nel ticket richiede `tickets.manage`, coda attiva e versione attesa. Il filtro `queue=id|unassigned` si applica alla query già limitata dai normali grant `tickets.view`; nessuna coda espande la visibilità per sede/dispositivo. La UI amministrativa crea/disattiva le code, la console ticket assegna, filtra e seleziona ticket gestibili. `PUT /api/support/bulk-queue` applica una coda comune a massimo 50 ticket con versione attesa e controllo grant per ciascuno; lock ordinati per dispositivo e transazione rendono l'aggiornamento atomico. `./scripts/test.sh`: 63 xUnit/PostgreSQL e 4 Vitest passati, incluse migrazioni, default deny, conflitto di versione e assegnazione bulk; `docker compose config -q` e `git diff --check` passati. Nessun deploy LAN.

### Membership operatori nelle code — 2026-10-19

Migrazione `SupportQueueMembership` aggiunge `SupportQueueMembers` con chiave composta, FK Identity e versione ottimistica delle code. API amministrative sostituiscono fino a 500 membri account attivi per richiesta, con versione attesa, audit e outbox. Le opzioni assegnatario rispettano sia `tickets.view` sia `tickets.manage` sull'ambito del ticket e la membership della coda; assegnazioni singole e massive respingono assegnatari che non ne fanno parte. La membership non modifica i grant e non concede visibilità su ticket o dispositivi. Le code non attive non accettano modifiche di membership o nuove assegnazioni.

`./scripts/test.sh` passato dopo le modifiche: 63 test xUnit/PostgreSQL e 4 Vitest; migrazioni applicate al database temporaneo e dashboard TypeScript compilata. Verificati utente inesistente, sostituzione membership, conflitto versione obsoleta, filtro assegnatari e rifiuto di assegnatario non membro. `docker compose config -q`, configurazione Compose test e `git diff --check` passati. CI sul commit precedente `da96021`: build-and-test [39250303866](https://github.com/ico88/OmniDesk/actions/runs/39250303866) e Windows package [39250303878](https://github.com/ico88/OmniDesk/actions/runs/39250303878) riusciti; tali run non includono questa modifica. Nessun deploy LAN, upgrade del database live o test Windows interattivo effettuato.

### Routing automatico code ticket — 2026-10-19

Migrazione `SupportQueueRouting` aggiunge regole univoche per categoria globale o categoria/sede, con FK Restrict a sito e coda, versione di concorrenza, audit e outbox. La gestione richiede `administration`; la UI permette di creare e rimuovere le regole. I nuovi ticket selezionano prima la regola attiva della sede e poi quella globale; code disattivate non vengono selezionate. Il routing non sceglie assegnatari e non concede permessi.

`./scripts/test.sh`: 63 xUnit/PostgreSQL e 4 Vitest passati, con migrazione, fallback globale, precedenza sede, conflitti di unicità/versione, cancellazione versione-attesa e divieto per utente scoped. Build TypeScript e `docker compose build api dashboard` riuscite. `./scripts/smoke.sh --browser` passato: migrazioni/bootstrap isolati, HTTPS, Chromium, gestione account/grant, dispositivo fixture, ticket e permessi UI. Lo smoke non esercita i controlli routing dalla pagina amministrativa, coperti da API test e build. `docker compose config -q`, configurazione Compose test e `git diff --check` passati. Nessun deploy LAN o upgrade del database live.


## Terminazione processi RMM — 19 ottobre 2026

Nuova capability `processes.manage`, seed solo Administrator predefinito e grant
scoped per ruoli custom; endpoint queue/storico con lock device, idempotenza,
audit e outbox, cancellazione dei job queued e rivalidazione del permesso. Il claim
rivalida il permesso. L’Agent registra una ricevuta
protetta prima di eseguire e non ripete un’azione interrotta; ricontrolla PID,
nome e ora d’avvio (tolleranza 250 ms) per evitare PID riutilizzati. Policy blocca processi Windows
critici, Defender, MeshCentral e OmniDesk. UI richiede conferma esplicita.

La suite aggiornata verifica default deny, validazione timestamp/PID, blocco
processi protetti, assegnazione custom scoped, idempotenza e claim/result senza
eseguire processi sul PC pilota. L’Agent self-test Windows crea un processo
PowerShell temporaneo e prova ad arrestare solo quella fixture. `dotnet publish` cross-target
win-x64 e build API/dashboard passati. Workflow Actions 0.8.5 in corso sulla prima
revisione della feature; una nuova esecuzione verificherà anche l’ultimo hardening;
nessun deploy LAN né azione su processi reali.

## Code Service Desk per sede — 2026-10-19

Implementato `SupportQueue.SiteId` nullable con migrazione additiva: le code già presenti restano globali, i nomi sono univoci separatamente per code globali e per sede. API amministrative per creare/listare code con ambito; l'ambito non è modificabile dopo la creazione. Le regole di instradamento e le assegnazioni singole/massive rifiutano code di un'altra sede. L'intake usa prima la regola attiva della sede e poi il fallback globale. Le API restituiscono code di sede solo a operatori con ticket visibili in quella sede e le opzioni ticket richiedono `tickets.manage` sul dispositivo.

La dashboard carica le opzioni globali e quelle delle sedi dei ticket visibili, deduplicandole per filtro, assegnazione massiva e scheda ticket. Nessun dato di sede non autorizzato viene dedotto dal client; ogni mutazione è comunque rivalidata lato server. Non è stata eseguita alcuna migrazione o pubblicazione sull'istanza LAN.

Verifiche locali: `./scripts/test.sh > /tmp/omnidesk-site-queues-final2.log 2>&1` passato (63 test xUnit con PostgreSQL effimero e migrazioni, inclusa autorizzazione della lista code per sede, 4 test Vitest e build TypeScript); `docker compose config -q` e `docker compose -f compose.test.yaml config -q` passati; `docker compose build api dashboard` e build API dopo l'affinamento autorizzativo passati; `./scripts/smoke.sh --browser > /tmp/omnidesk-site-queues-browser.log 2>&1` passato (container, migrazioni/bootstrap, HTTPS, Chromium e flussi UI esistenti); `git diff --check` passato. Lo smoke Chromium non ha esercitato direttamente le nuove opzioni queue per sede: queste sono coperte dai test API e dalla build UI, non da uno scenario browser dedicato. Nessun test runtime sui servizi LAN o sull'Agent Windows è implicato da queste verifiche.

## Diagnostica apertura sessione MeshCentral — 2026-10-19

L'operatore segnala che la console resta su “Apertura della sessione autorizzata…”. Verificati i container LAN attivi, le richieste `availability` e l'heartbeat Agent; l'API registra creazione sessione `201`, che attesta solo il rilascio del link temporaneo. I log MeshCentral riportano l'agente Windows `192.168.1.160` trattenuto per `Agent bad web cert hash`; pertanto il flusso desktop dell'agente non è verificato e la sessione autorizzata non equivale a video funzionante. Il link è a breve scadenza. La dashboard ora toglie lo spinner dopo 20 secondi e indica che il motore remoto va verificato, mantenendo l'opzione di apertura in nuova scheda. `docker compose build dashboard` e `./scripts/smoke.sh --browser > /tmp/omnidesk-remote-loading-smoke.log 2>&1` passati. Nessun container, servizio Windows, firewall o certificato è stato modificato. La soluzione operativa prevista è riallineare il motore Windows al certificato MeshCentral corrente e riprovare.

## Rigenerazione amministrativa provisioning remoto — 2026-10-19

Aggiunti `POST /api/admin/devices/{id}/remote-provisioning/refresh` e pulsante confermato nella UI admin. Il server serializza per dispositivo, rivalida `administration` dopo l'attesa MeshCentral, verifica credenziale/provisioning/gruppo, cifra le nuove impostazioni, ruota l'identificatore che induce il servizio Agent a riavviare il motore, incrementa la versione policy, accoda la revoca delle sessioni attive e scrive audit. La policy non presidiata resta invariata. Il test scenario verifica default deny per utente scoped, rotazione ID, nuove impostazioni cifrate, sessione in `revocation_pending`, audit refresh e pulizia via revoca. La prima esecuzione ha esposto una gara del test host fra worker periodici e sweeps invocati dallo scenario; il test host ora esclude i tre hosted worker e li lascia esercitare esplicitamente nel test. Verifiche finali: `./scripts/test.sh > /tmp/omnidesk-remote-refresh-final3.log 2>&1` passata (63 xUnit con PostgreSQL/migrazioni, 4 Vitest e build TypeScript); `docker compose build api dashboard` passato; `./scripts/smoke.sh --browser > /tmp/omnidesk-remote-refresh-smoke.log 2>&1` passato; `docker compose config -q`, `docker compose -f compose.test.yaml config -q` e `git diff --check` passati.

Deploy LAN successivo: backup protetto `.backups/20261019T131209Z.F4CFy3` completato; dopo aver verificato zero sessioni remote attive, ricreati solo API/dashboard e applicate le migrazioni versionate pendenti fino a `20261019123904_SupportQueueSiteScope`. La prima sonda readiness ha correttamente restituito 503 perché il DB era indietro; API fermata durante la migrazione come da procedura, poi riavviata. Verificati HTTPS con CA, `/api/health/ready` `ready`, dashboard HTTP 200, `/api/auth/me` anonimo 401 e bundle che contiene il pulsante “Rigenera configurazione remota”. Tutti i container risultano avviati; nessuna sessione remota o configurazione dispositivo è stata modificata dal deploy. Rimane da usare il pulsante per il dispositivo pilota e verificare il desktop video reale su Windows: nessun esito positivo di Mesh/video è ancora dichiarato.

CI del commit `1d8e9db`: Linux build/test/smoke browser [39257184292](https://github.com/ico88/OmniDesk/actions/runs/39257184292) e Windows Agent/Tray/MSI [39257184300](https://github.com/ico88/OmniDesk/actions/runs/39257184300) riuscite. Il runner Windows ha superato build MSI, installazione, recovery, disinstallazione, upgrade, repair e rollback. Questo valida il pacchetto isolato CI e non sostituisce il test video sulla macchina Windows del pilota.

## Note interne Service Desk — 2026-10-19

`SupportMessages.Visibility` distingue `public` e `internal`; la migrazione `TicketInternalNotes` mantiene pubblici i messaggi esistenti e assegna `tickets.note.internal` al solo ruolo Administrator. `POST /api/support/{id}/internal-notes` richiede anche `tickets.view`, capability scoped sul ticket storico, CSRF e `expectedVersion`; serializza per dispositivo, aggiorna versione/data, scrive audit senza testo e outbox con soli metadati. La lettura conversazione esclude le note se il chiamante non ha il permesso; lista/macchina non ricevono note. La UI mostra un composer distinto, etichettato e stilizzato come interno. Non sono stati introdotti note pubbliche editabili per il portale né identità del portale.

Verifiche: `./scripts/test.sh` passato (63 xUnit/PostgreSQL, 4 Vitest e TypeScript); scenario ticket verifica default deny, body non visibile all'utente scoped, inserimento admin, conflitto di versione, audit e assenza del body nell'outbox. `docker compose config -q`, `docker compose -f compose.test.yaml config -q`, `docker compose build api dashboard` e `git diff --check` passati. `./scripts/smoke.sh --browser` passato dopo aggiornamento del test harness per generare assertion Agent fresche a ogni richiesta: Chromium verifica la UI admin e che il feed dell'Agent non contenga la nota. CI del commit `c3e609e`: Linux [39258442637](https://github.com/ico88/OmniDesk/actions/runs/39258442637) e Windows [39258442710](https://github.com/ico88/OmniDesk/actions/runs/39258442710) riuscite.

Deploy LAN note interne: backup protetto `.backups/20261019T132311Z.zsZ0gC` completato; verificate zero sessioni remote attive, applicata `20261019131702_TicketInternalNotes`, aggiornati solo API/dashboard. HTTPS readiness `ready`, dashboard 200, anonimo `/api/auth/me` 401, bundle aggiornato confermato e seed Administrator verificato. Non creati ticket o note fittizi. Non sono ancora verificati su dati reali l'accesso con capability scoped o il flusso operatore dall'interfaccia; restano coperti da test isolati e da collaudo utente.

### Nome leggibile dell’operatore remoto — 2026-10-19

Implementato il nome visualizzato modificabile in Account e sicurezza; la sessione
MeshCentral riceve il nome scelto oppure l’email come fallback, mai l’ID interno.
Aggiunta la migrazione versionata `20261019140500_OperatorDisplayName`. La prova
end-to-end verifica la validazione del profilo e che il provider Mesh riceva il
nome configurato invece dell’ID account. `./scripts/test.sh` passato: 64 test
API/PostgreSQL, 4 test Vitest, TypeScript e build dashboard. Deploy LAN completato con backup protetto `.backups/20261019T133343Z.6LTaS8`;
migrazione applicata con API ferma dopo verifica zero sessioni attive, poi API e
dashboard ricreate. `verify-lan.py` ha confermato CA/readiness, dashboard/assets e
rifiuto anonimo. Il test browser isolato verifica che il nome si possa salvare e persista al reload.
La prova visiva dell’indicatore sul PC Windows resta pendente. Gli account esistenti
hanno il nuovo campo vuoto e vedono l’email come fallback finché non impostano il
nome in Account e sicurezza.

La durata del link MeshCentral è stata estesa a 30 minuti (prima 5); il countdown
UI continua a riflettere `ExpiresAt` fornito dal server. Deploy LAN completato con
backup `.backups/20261019T133343Z.6LTaS8`, dopo aver verificato zero sessioni attive;
API e dashboard aggiornate e readiness HTTPS verificata.

Verifica finale dopo la durata di 30 minuti: `./scripts/test.sh` passato con 64
test xUnit/PostgreSQL, 4 test Vitest, TypeScript e build dashboard. Test dedicato
vincola la durata a 30 minuti; `docker compose build api dashboard` passato. CI per il commit `9355c75` ha passato Linux build/test/smoke; la pipeline Windows
per packaging dell’agente resta in corso e non blocca le modifiche web.

Smoke Chromium esteso per impostare il nome operatore e verificarne la persistenza; `./scripts/smoke.sh --browser` passato. Questo non avvia MeshCentral né sostituisce la verifica visiva sul PC pilota.

### Riepilogo scoped ticket — 2026-10-19

Aggiunto `GET /api/support/summary`: restituisce totale, aperti, in lavorazione e
urgenti non chiusi per l’insieme autorizzato, con filtri facoltativi per device/sede.
L’aggregazione non eredita il limite di 100 righe della tabella. UI con cards
responsive. `./scripts/test.sh` passato (64 xUnit/PostgreSQL, 4 Vitest, TypeScript);
lo scenario verifica conteggio device-scoped e zero conteggi per un account senza
`tickets.view`. `./scripts/smoke.sh --browser` passato su container isolati con
conteggio UI; `docker compose build api dashboard` e `git diff --check` passati.
Deploy LAN completato dopo il CI Linux con backup protetto `.backups/20261019T134504Z.FHXrbK`; zero sessioni remote prima/dopo lo stop API, nessuna migrazione necessaria, ricreati API/dashboard. `verify-lan.py` ha confermato CA, readiness, asset e rifiuto anonimo; il bundle pubblico contiene i nuovi contatori. Nessun login o ticket di produzione è stato creato.

CI Linux del commit `a331894` completato con test, configurazione Compose, build API/dashboard e smoke Chromium; workflow Windows dello stesso commit `39262750153` concluso con successo.

### Risposta del richiedente dalla Tray — sorgenti 0.8.8

`./scripts/test.sh` passato: 64 test xUnit con PostgreSQL/migrazioni, 4 Vitest,
build TypeScript/Vite. Gli scenari verificano lettura solo dei messaggi pubblici,
associazione a device + requester key, risposta, rifiuto chiave errata/versione
obsoleta e payload IPC invalidi. `docker compose config -q`,
`docker compose -f compose.test.yaml config -q`, `docker compose build api` e
`git diff --check` passati. La macchina di
sviluppo non ha il comando `dotnet`; la build nativa Agent/Tray/MSI verrà verificata
dal workflow GitHub Windows dopo il push. Nessuna installazione Windows o deploy
LAN erano ancora stati eseguiti al momento di questa nota.

### CI e kit LAN Agent/Tray 0.8.8

Commit `4d515e5b9f6783b4c4a6d41edf1096448bf1ac26`: CI Linux
[39265279164](https://github.com/ico88/OmniDesk/actions/runs/39265279164) e CI
Windows [39265279323](https://github.com/ico88/OmniDesk/actions/runs/39265279323)
completate con successo. Windows ha compilato Agent/Tray/MSI e passato
installazione, recovery, disinstallazione e upgrade/repair/rollback da baseline
0.6.0 e 0.7.0. L’artifact Windows non prova l’installazione interattiva o la
conversazione sul PC pilota.

Artifact Windows confezionato con `scripts/package-lan-kit.py` in
`artifacts/windows-lan/OmniDesk.Windows.LAN-0.8.8.zip` e pubblicato nel volume
`/packages` montato dall’API, senza riavvio o stop dei servizi. Manifesto letto
anche dal container API: commit sopra, ZIP SHA-256
`ae988c07bda5a64c3c8404efc5341c719a40b0b937a2addbfbfb21374138edd3`, MSI
SHA-256 `530894b8bd4ddc518872109694456d2377338df74931a8ba9c7d4bfdc773b8bd`.
`verify-lan.py` ha confermato readiness HTTPS/CA, dashboard e rifiuto anonimo.
Il test HTTP autenticato del download non è stato eseguito; non è stato usato né
creato un account o ticket di produzione. Il pacchetto resta unsigned, per
collaudo LAN; non è una release firmata né una validazione di Defender.
Il packager ricava la versione dal nome `windows-X.Y.Z` se `--version` è omesso;
la prova in directory temporanea è passata e `ZipFile.testzip()` ha restituito
`None` per i cinque file del kit.

### Aggiornamento runtime API LAN — immagine sessioni da 30 minuti

L’ispezione read-only ha trovato che il container API in esecuzione usava
l’immagine del 13:47, mentre la build locale del 13:59 includeva già
`SessionLifetime = 30 minuti`. Le sessioni create prima del deploy avevano
`ExpiresAt - CreatedAt = 5 minuti`; dopo l’avvio del vecchio container non ne
erano state create altre e il conteggio di sessioni `active`/
`revocation_pending` era zero. Il DB era già alla migrazione
`20261019140500_OperatorDisplayName`; nessuna migrazione richiesta.

Backup `.backups/20261019T141342Z.umBpLS` completato e ripristinato su PostgreSQL
temporaneo: checksum valido, 26 migrazioni, 1 account e 1 dispositivo. Ricreato
solo `api` con `docker compose up -d --no-deps api`, usando immagine
`sha256:6c964272243bb0ab5d38e9d970495d2f000e8fcbef2b7a76e1a8a97550072c3f`;
dashboard, Caddy, DB e MeshCentral non ricreati. `verify-lan.py` ha confermato
HTTPS/CA, readiness, dashboard e 401 anonimo; tutti i servizi risultano running.
Il test `Session_lifetime_is_thirty_minutes` è passato nella suite CI. Nessuna
sessione remota reale è stata aperta dopo il deploy: verifica interattiva della
durata e del banner sul PC pilota ancora aperta.

### Raccolta Processi e Servizi Windows — 2026-10-19

I `409` sulle raccolte diagnostiche erano causati dal blocco globale dei job:
un job Processi impediva di accodare Servizi (e viceversa), e l’indice univoco
PostgreSQL lo imponeva anche a livello database. Rimossi il vincolo e il blocco
tra letture diagnostiche; richieste duplicate della stessa categoria riusano il
job ancora `queued`/`leased`. Update, terminazione processi e controllo servizi
restano serializzati fra loro e non vengono bloccati dalle sole diagnostiche.
I messaggi `409` delle azioni operative ora mostrano la spiegazione inviata
dall’API.

Verifiche: `docker compose -f compose.test.yaml run --build --rm tests` — 64/64
test passati, incluse richieste Processi+Servizi in coda e nuova migrazione
versionata; `docker compose build api dashboard`, `npm test -- --run` nella
dashboard, `docker compose config --quiet`, `docker compose -f compose.test.yaml
config --quiet` e `git diff --check` passati. Il server LAN è stato aggiornato
con backup verificato e migrazione; la conferma dal PC pilota resta aperta.


Deploy LAN della correzione diagnostica: backup `.backups/20261019T142835Z.6lW51s`
verificato su PostgreSQL temporaneo (26 migrazioni, 1 account, 1 dispositivo);
conteggio job attivi e sessioni remote prima del deploy era zero. Applicata la
migrazione `20261019142600_ConcurrentDiagnosticJobs`, quindi ricreato solo il
container API. L’istanza riporta la nuova migrazione e zero job attivi.
`verify-lan.py --server https://192.168.1.233:8085 --ca omnidesk-local-ca.crt`
ha passato readiness HTTPS/CA, dashboard e rifiuto anonimo. Nessun login o
comando diagnostico sul dispositivo di produzione è stato eseguito; test
interattivo da browser ancora da fare.


### Ticket manuali da console — 2026-10-19

Aggiunta la rotta `POST /api/support/manual`, protetta da `tickets.manage` per
il dispositivo e transazione con lock di dispositivo. `RequestId` crea una sola
riga per operatore/dispositivo; riuso con contenuto diverso risponde 409. Il
ticket acquisisce sede, priorità e coda di routing; `Origin` distingue intake
Agent e inserimento operatore. Audit e outbox non includono oggetto o
descrizione. La UI offre il form nella vista Assistenza e nel pannello ticket
del dispositivo, selezionando solo dispositivi visibili all'operatore.

Verifiche: `docker compose -f compose.test.yaml run --build --rm tests` — 64/64
(passano grant scoped, deny fuori ambito, creazione, retry, payload in conflitto
e priorità/sede); `docker compose build api dashboard`, `npm test -- --run`
(5/5), entrambe le configurazioni Compose e `git diff --check` passati.
Migrazione versionata `20261019143300_ManualSupportTickets`, applicata
sull'istanza LAN dopo backup verificato. Il runtime è verificato sotto.


Deploy LAN ticket manuali: backup `.backups/20261019T143739Z.13NE6M`
verificato in PostgreSQL isolato (27 migrazioni, 1 account e 1 dispositivo);
conteggio job e sessioni remote prima del deploy era zero. Applicata la
migrazione `20261019143300_ManualSupportTickets`, ricreati solo i container API
e dashboard. `verify-lan.py` ha passato HTTPS/CA, readiness, dashboard e rifiuto
anonimo; il bundle JS pubblicato contiene il form ticket manuale e l'etichetta
dell'origine. Nessun login o ticket è stato creato sull'istanza. Prova
interattiva autenticata dal browser ancora aperta.

Retry UI ticket manuali: il modulo conserva lo stesso `RequestId` se il browser
ritenta senza modificare i campi; ogni modifica al payload genera una nuova chiave
di richiesta. Build TypeScript/Vite e Vitest 5/5 passati. La dashboard è stata aggiornata
soltanto dopo CI Linux; il bundle pubblico mostra il form, readiness HTTPS/CA e
rifiuto anonimo sono passati. Nessun login o invio ticket runtime è stato eseguito.


### Rinnovo sessione remoto nella stessa console — sorgenti verificati

Con grant in scadenza entro cinque minuti, la barra remota propone «Rinnova
sessione». L'operatore resta nella pagina; OmniDesk richiede un grant nuovo
attraverso l'endpoint esistente, che rivalida capability, dispositivo, policy e
consenso MeshCentral. Il link precedente viene revocato prima di sostituire
l'iframe; in caso di errore il nuovo link viene revocato come compensazione e la
sessione corrente resta aperta. Non c'è rinnovo automatico né estensione nascosta;
la console può mostrare nuovamente la richiesta di consenso del PC assistito.

Verifiche: `docker compose build dashboard` e Vitest 5/5 passati; API/PostgreSQL
64/64 con database Compose isolato. CI Linux 39273108094 è passata sul commit
`8f785bf`; il workflow Windows 39273108228 era ancora in esecuzione durante il
deploy e non compila la dashboard né include modifiche Windows.

Pubblicazione LAN: ricreato solo il container `dashboard`, dopo aver verificato
zero sessioni remote e zero job attivi. API, DB, Caddy e MeshCentral non sono
stati ricreati. `verify-lan.py --server https://192.168.1.233:8085 --ca
omnidesk-local-ca.crt` ha passato HTTPS/CA, readiness, dashboard e rifiuto
anonimo; il bundle HTTPS contiene «Rinnova sessione». Dopo il deploy i servizi
sono running e sessioni/job attivi restano zero. Il test interattivo di
riconnessione MeshCentral e la ripetizione del consenso sul PC pilota restano
aperti.

### Disconnessione desktop riutilizzabile e associazione multi-dispositivo ticket — sorgenti/test

MeshCentral installato nel container LAN implementa `endDesktopMultiplex`: il
server chiude i viewer desktop selezionati per identità proprietario e guest
name, mantenendo il grant share distinto dalla revoca. L'adapter risolve tali
valori dal record `deviceShares` usando il `publicid` conservato lato server e
poi invia il comando; nessun ID Mesh è accettato dal client. La barra offre
«Disconnetti operatore» con riconnessione manuale sul grant esistente e mantiene
«Termina» per revocare il link. Verificato da build TypeScript/Vite e test API
con adapter fake: autorizzazione negata al viewer, sessione resta attiva dopo la
richiesta di disconnessione. Il comando MeshCentral reale non è stato ancora
eseguito contro una sessione desktop interattiva; la chiusura usa protocollo
interno della versione installata e richiede regressione dopo ogni upgrade.

Correzione successiva: la barra usa «Disconnetti e revoca» per non lasciare
riutilizzabile il grant corrente. Se la revoca è pending, l'interfaccia mantiene
lo stato in attesa e l'API blocca nuove sessioni sul dispositivo fino alla
conferma MeshCentral; dopo la conferma, una nuova sessione è possibile solo con
le autorizzazioni e la policy correnti. La suite PostgreSQL copre pending,
blocco nuove sessioni, retry riuscito e sessione autorizzata successiva; smoke
Chromium usa una risposta Mesh simulata per verificare conferma, toolbar e
ritorno allo stato pronto. Build API/dashboard, Compose e smoke Chromium passati.
Il comportamento del motore remoto e una sessione Windows reale non sono stati
verificati.

SD2 aggiunge migrazione con backfill del device primario, API link/unlink con
permessi su ticket e dispositivo, versione concorrente, limite 10, lock,
audit/outbox e UI ticket. Suite PostgreSQL isolata: 64/64 xUnit passati,
includendo deny fuori ambito, link idempotente, rimozione del secondario e
protezione del primario. Build dashboard e Vitest 5/5 passati.

Deploy LAN: backup completo in `.backups/20261019T152326Z.l2bttW`; aggiornate le
sole immagini API e dashboard dopo aver verificato zero sessioni remote e zero
job update. La prima sonda ha restituito readiness 503 per la migrazione
`20261019150122_TicketDeviceLinks` ancora pendente; eseguito esplicitamente
`docker compose run --rm api --migrate`, senza modifiche automatiche allo schema
all'avvio. `verify-lan.py --server https://192.168.1.233:8085 --ca
omnidesk-local-ca.crt` ora passa readiness/CA, dashboard e rifiuto anonimo; il
bundle pubblicato contiene i controlli di disconnessione e dispositivi ticket.
Tutti i cinque servizi risultano running e il DB resta a 0 sessioni remote / 0
job update. Nessun login, click autenticato o comando di disconnessione reale
MeshCentral è stato eseguito. CI Linux `39276800149` e Windows `39276800079`
sono passate sul commit finale con codice applicativo; anche Windows
`39276440522` è passato sul commit della feature. I workflow del successivo
commit solo documentale sono stati avviati dopo la validazione runtime.

### Ticket multi-dispositivo: vincolo stessa sede

La specifica SD1 richiede che ogni dispositivo associato appartenga alla sede
storica del ticket al momento del link. API aggiornata per filtrare i candidati
alla stessa sede e rifiutare link cross-site; dopo i lock dispositivo il server
rilegge sede/nome, ricontrolla il permesso e produce lo snapshot aggiornato.
Test PostgreSQL: cross-site rifiutato, target same-site senza grant negato,
link same-site amministrativo consentito. `./scripts/test.sh` passa con 64/64
xUnit e 5/5 Vitest. Il deploy LAN dell'API è stato completato sul commit
`beb53cb` dopo backup `.backups/20261019T153003Z.bqc6Hr` e controllo DB a zero
sessioni remote / zero job update. Nessuna migrazione necessaria. La sonda
`verify-lan.py` passa readiness/CA, dashboard e rifiuto anonimo; cinque servizi
running. Nessun login o test ticket autenticato è stato eseguito.

### Badge presenza Agent nell'elenco dispositivi

L'API aggiunge `online`, `revoked` e `lastSeen` a `GET /api/devices`. Online usa
la stessa soglia di tre minuti dell'endpoint scheda dispositivo; una credenziale
revocata è visualizzata separatamente. La UI aggiorna l'elenco ogni 30 secondi
mentre la scheda Dispositivi è aperta. `AgentScenario` verifica heartbeat recente,
heartbeat oltre la soglia dei tre minuti, rientro online dopo un nuovo heartbeat,
ultimo contatto e revoca. `./scripts/test.sh` passato con 64/64 xUnit/PostgreSQL,
build TypeScript/Vite e 5/5 Vitest. `docker compose build api dashboard` e
`docker compose config --quiet` passati. Nessun login autenticato nel browser;
backup completo `.backups/20261019T153639Z.mzZpdG` effettuato dopo verifica DB
con 0 sessioni remote e 0 job attivi. Deploy con `docker compose up -d --no-deps
api dashboard`; API, dashboard, Caddy, DB e MeshCentral risultano running. `python3
scripts/verify-lan.py --server https://192.168.1.233:8085 --ca
omnidesk-local-ca.crt` passa readiness HTTPS/CA, dashboard e rifiuto anonimo.
Asset bundle `index-BL35s1G4.js` verificato con le etichette badge. Conteggio
successivo: 0 sessioni remote e 0 job. Non eseguiti login o verifica visiva
autenticata nel browser sulla LAN. Esteso `scripts/browser-smoke.mjs` per inviare
un heartbeat firmato al dispositivo fixture e controllare il badge Online dalla
UI autenticata. `./scripts/smoke.sh --browser` passato: migrazioni/bootstrap,
HTTPS, login, badge, support desk, device management e permessi viewer; lo stack
temporaneo è stato rimosso dal trap. Workflow GitHub Linux e Windows sono ancora
in coda.

### Gestione coda GitHub Actions

I workflow Linux e Windows ignorano push e pull request composti solo da file
Markdown (`docs/**` e `*.md`). Ogni workflow usa un gruppo per workflow/ref con
`cancel-in-progress: false`: i run in esecuzione vengono preservati e una nuova
revisione può sostituire quella ancora pending per lo stesso ref. Verificato lo
YAML con PyYAML e asserzioni su trigger, filtri e concorrenza. Dopo il commit
workflow `826d1ca`, il push documentale `d3127bf` non ha creato altri workflow:
il filtro Markdown è verificato. Il comportamento di coalescenza non è ancora
verificato: i run in coda/in esecuzione sono precedenti alla nuova regola e
continuano il loro corso senza essere interrotti.
## Incremento SD2 — Knowledge Base interna iniziale

Implementati articoli testuali con slug univoco, bozze/pubblicazione, versione concorrente ottimistica e audit; permessi globali `knowledge.view` (Operator) e `knowledge.manage` (Administrator), default deny per Viewer. La UI React offre ricerca, lettura e editor solo ai gestori; il body è reso come testo e non interpreta HTML. Non sono inclusi portale self-service, ambiti sede/gruppo, allegati, Markdown/HTML, revisioni consultabili o workflow di approvazione.

`./scripts/test.sh` passato: build API, migrazioni PostgreSQL temporanee, 64/64 test xUnit e build dashboard/5 Vitest. Lo scenario KB verifica rifiuto Viewer, lettura Operator limitata agli articoli pubblicati, bozze invisibili, creazione/audit, slug duplicato, pubblicazione e conflitti di aggiornamento concorrenti/stale. `docker compose build api dashboard` passato. `./scripts/smoke.sh --browser` passato con container isolati e Chromium: login Administrator, creazione/pubblicazione da UI, contenuto `<script>` non eseguibile, Viewer senza voce UI e 403 API. Compose standard validato con `docker compose config --quiet`; `git diff --check` passato. Nessun deployment LAN eseguito in questo incremento.

## Diagnostica processi/servizi — dettaglio 409 per credenziale Agent

Le API di diagnostica, controllo servizi/processi e aggiornamento ora restituiscono un errore 409 strutturato (`agent_credential_unavailable`) quando l'identità Agent è assente o revocata. La UI esistente mostra il messaggio server invece del solo codice HTTP; la policy resta deny. Aggiunte asserzioni API per i tre percorsi dopo revoca. `./scripts/test.sh` passato: 64/64 test xUnit/PostgreSQL e 5/5 Vitest; in particolare le tre risposte sono verificate dopo revoca dell’identità dispositivo. Sul database LAN la sola verifica read-only ha trovato 1 dispositivo, 1 credenziale attiva, 0 job mutanti in coda/in lease e 0 sessioni remote attive; l’errore 409 storico non è riproducibile nello stato corrente. Il messaggio strutturato chiarisce quindi il caso credenziale assente/revocata; gli altri 409 (conflitto di job attivo o request id riutilizzato) mantengono la loro semantica. Nessuna immagine o servizio LAN ricreato; test interattivi non interrotti.

### Revoca remota: chiusura stream prima del link

`DELETE /api/devices/{id}/remote-sessions/{sessionId}` invia `endDesktopMultiplex`
per le sessioni `remote.control`, poi rimuove il device share. La sessione passa
prima a `revocation_pending`; il guard ripete entrambi i passaggi e lo stato
continua a negare nuove sessioni finché la revoca non viene confermata. I test
API/PostgreSQL coprono l'ordine, il fallimento della chiusura, il fallimento
della revoca dopo una chiusura inviata, il retry e la riapertura autorizzata.
`./scripts/test.sh` è passato con 64/64 test e 5/5 Vitest. Il browser smoke
verifica il messaggio della console con MeshCentral simulato. `Disconnect`
invia il comando MeshCentral senza attesa di risposta: il test non dimostra che
il viewer Windows abbia effettivamente chiuso lo stream. La prova interattiva
MeshCentral resta aperta; nessun deploy LAN è stato eseguito. In questa verifica
i container LAN sono rimasti attivi, senza ricrearli.

### Inbox notifiche ticket — 2026-10-19

`./scripts/test.sh` passato con 64/64 test xUnit/PostgreSQL e 5/5 Vitest. Gli
scenari verificano proiezione del ticket alla coda, risposta al responsabile o
alla coda, assegnazione, verifica permessi corrente, lettura proprietario-only,
idempotenza, retry con backoff e dead-letter dopo dieci errori. I payload e i
testi delle conversazioni non vengono registrati nei log della consumer.
`docker compose config --quiet` e `docker compose build api dashboard` passati.
`./scripts/smoke.sh --browser` passato su stack Compose isolato con Chromium:
creazione ticket fixture, ricezione inbox, click-through al ticket, flussi
esistenti supporto e device; i container temporanei sono stati rimossi dal trap.
Questa esecuzione non ha ricreato o aggiornato container, schema o dati LAN; non
è una verifica runtime sull'istanza `192.168.1.233:8085`. Non sono previsti invii
email o push in questa implementazione.

### Icona prodotto Tray/Setup e favicon — 2026-10-19

Creato `assets/omnidesk.ico` con frame 16/24/32/48/256 px e `favicon.svg` con
lo stesso simbolo. La Tray incorpora l'ICO nel binario e non sostituisce più il
marchio con icone generiche di stato; il suo tooltip e menu continuano a
riportare connessione e ultimo heartbeat. Tray e Setup impostano anche l'icona
Windows dell'eseguibile e Setup usa il marchio nella finestra. La dashboard
collega la favicon e riutilizza l'SVG nel login e nel marchio predefinito.
Pacchetto sincronizzato a 0.8.9. `./scripts/test.sh` passato con 64/64 xUnit e
5/5 Vitest; `docker compose build dashboard` è riuscito e
`./scripts/smoke.sh --browser` ha verificato favicon e marchio condiviso in
Chromium. Cross-build `dotnet build OmniDesk.sln -c Release
-p:EnableWindowsTargeting=true` riuscito: Tray incluso, 0 errori; il progetto
Setup Windows separato compila con 0 warning/errori. La soluzione segnala tre
warning nullable già presenti nei test API. CI Linux 39290031242 ha passato
suite, configurazione/backup Compose, build immagini e smoke Chromium. CI
Windows 39290031180 ha compilato l'MSI 0.8.9, estratto l'icona dal binario
installato e superato installazione, recovery, disinstallazione e
upgrade/repair/rollback. `docker compose config --quiet` e `git diff --check`
passati. Nessun deployment o aggiornamento sulla LAN.

### KB — storico delle revisioni consultabile — 2026-10-19

Ogni articolo crea una revisione snapshot iniziale e ogni aggiornamento aggiunge
la revisione successiva nella stessa transazione del controllo ottimistico e
dell'audit. La migrazione `20261019170935_KnowledgeArticleRevisions` crea la
tabella append-only lato API e copia la versione corrente degli articoli
esistenti; le revisioni non espongono mutazioni o ripristino automatico. Gli
endpoint di elenco e dettaglio richiedono `knowledge.manage`; la UI presenta
revisioni in sola lettura con autore e data, e torna esplicitamente alla versione
corrente.

`./scripts/test.sh` passato con 64/64 test xUnit/PostgreSQL: copre creazione
v1, esclusione del body dall'elenco, negazione Viewer, un solo vincitore in
update concorrenti, conflitto su versione obsoleta, v3 dopo ritiro dalla
pubblicazione e permanenza delle revisioni precedenti. `docker compose build api
dashboard` passato; TypeScript/Vite e `dotnet publish` sono stati eseguiti nei
build container. `./scripts/smoke.sh --browser` passato al secondo tentativo:
Chromium crea una guida, modifica la descrizione, apre v1, verifica il contenuto
precedente e ritorna alla versione corrente. Il primo tentativo è fallito per
un selettore test troppo rigido; corretto il selettore e il flusso è passato.
Lo smoke usa PostgreSQL e container effimeri isolati; `docker compose config
--quiet` per test e `git diff --check` passati. Nessun container, schema o dato
LAN è stato aggiornato.

### KB — ambiti di lettura per sede e gruppo — 2026-10-19

Gli articoli supportano scope globale, singola sede o singolo gruppo; una
constraint PostgreSQL vieta di impostare entrambi gli scope. La migrazione
`20261019172727_KnowledgeArticleScopes` lascia globali gli articoli esistenti,
aggiunge FK restrittive e conserva SiteId/GroupId in ogni snapshot. La lettura
richiede `knowledge.view` globale o un grant corrispondente alla sede/gruppo;
la modifica richiede `knowledge.manage` globale. I gestori selezionano lo scope
nell'editor e vedono gli articoli autorizzati nella lista; i tentativi fuori
ambito sono indistinguibili da un articolo assente. Gli scope archiviati non
sono proposti e nuove scritture su scope inesistenti/archiviati sono rifiutate.
Gli aggiornamenti da client precedenti che non inviano scope mantengono quello
esistente; la UI corrente dichiara esplicitamente quando cambia ambito, evitando
che un edit da sessione web obsoleta pubblichi involontariamente una guida.

`./scripts/test.sh` passato con 64/64 test xUnit/PostgreSQL e 5/5 Vitest.
`KnowledgeScenario` copre lettura isolata fra scope, rifiuto Viewer, rifiuto di
scope doppio, spostamento sede→gruppo e conservazione dell'ambito nella revisione
storica; `MigrationTests` inserisce un articolo prima delle migrazioni finali e
verifica il backfill globale. Un update nel formato precedente mantiene il sito,
mentre la richiesta esplicita sposta l'articolo e genera la revisione di gruppo.
`docker compose build api dashboard` passato con
`dotnet publish` e TypeScript/Vite. `./scripts/smoke.sh --browser` passato su
Compose isolato: Chromium crea un gruppo fixture e una guida associata al gruppo,
quindi verifica modifica e consultazione della versione precedente. Compose
standard/test validato e `git diff --check` passato. Nessun dato, container o
schema LAN è stato modificato. GitHub Actions `build-and-test` run
39295874327 e `windows-agent-package` run 39295874352 passate; il secondo ha
incluso MSI, recovery/uninstall e upgrade/repair/rollback su runner Windows.

### KB — ricerca autorizzata nei contenuti — 2026-10-19

`GET /api/knowledge/search?q=...` accetta 2–100 caratteri, interroga titolo,
descrizione e corpo sul server, limita a 50 metadati e non include il corpo
nella risposta. I filtri di pubblicazione e grant sono applicati alla query
prima di cercare: `knowledge.view` globale vede gli articoli pubblicati,
`knowledge.view` scoped vede solo gli scope concessi; bozze richiedono
`knowledge.manage` globale. La dashboard usa la query dopo 250 ms e scarta
risposte obsolete quando cambia la ricerca. La ricerca locale per parole brevi
resta limitata ai metadati già consegnati dall'API.

`./scripts/test.sh` passato: 64 test xUnit/PostgreSQL e 5 Vitest, inclusi
risultati trovati nel corpo, output senza corpo articolo, negazione senza grant,
limite minimo e isolamento sede/gruppo. `docker compose build api dashboard`
passato; `./scripts/smoke.sh --browser` passato verificando API e campo di ricerca
con Chromium su Compose isolato. Compose standard/test e `git diff --check`
passati. GitHub Actions `build-and-test` run 39297406507 e
`windows-agent-package` run 39297406508 passate, inclusi i test MSI Windows.
Nessun dato, container o schema LAN è stato modificato.

### Portale ticket personale e conferma marchio Tray/favicon — 2026-10-19

Il portale usa un'associazione amministrativa account-dispositivo separata dai
grant RMM. `RequesterUserId` distingue l'identità web dall'identità macchina;
creazione e risposte sono protette da antiforgery, ownership, link attivo,
versione attesa, rate limit, audit e outbox. La lettura restituisce solo messaggi
pubblici; la revoca del link rimuove immediatamente l'accesso e blocca nuove
risposte. Migrazione EF `20261019180416_PortalRequesterAccess` applicata dalla
suite al database PostgreSQL temporaneo, non al database LAN.

Verifica `./scripts/test.sh`: 64/64 xUnit/PostgreSQL e 5/5 Vitest; build
TypeScript/Vite passata. `./scripts/smoke.sh --browser` passato su Compose
isolato, incluso Chromium: account/grant, ticket, supporto, permessi Viewer,
favicon e flusso portale autenticato. Il test browser crea l'account cliente,
collega il dispositivo, verifica che l'invio resti disabilitato finché manca il
consenso, invia un PNG, renderizza l'immagine, controlla risposta pubblica e
assenza della nota interna, quindi revoca il link e verifica il 404 sullo
screenshot. Lo stesso scenario controlla le notifiche e la console IT con i due
ticket fixture; nessun deploy o test autenticato sul server LAN.
`docker compose build api dashboard`, configurazione Compose standard/test e
`git diff --check` passati. Il test del lockout verifica lo stato Identity
persistito, evitando che il limite HTTP di 20 tentativi al minuto mascheri la
risposta dopo cinque password errate. Non dichiarati test del portale su browser
interattivo o runtime LAN.

Conferma dell'icona richiesta: Tray e icona Windows dell'eseguibile leggono lo
stesso `assets/omnidesk.ico`; `NotifyIcon` mantiene il marchio anche in stato di
errore. La favicon SVG già presente riproduce il simbolo OmniDesk, ed è collegata
in `dashboard/index.html`, usata anche da login e brand mark. Lo smoke Chromium
ha verificato URL e asset servito. Nessuna modifica Windows MSI o nuovo asset è
necessario per questa richiesta; la pipeline Windows 39290031180 aveva già
verificato il pacchetto con l'icona condivisa. Sul commit `1f65d57`, GitHub
Actions `build-and-test` run
https://github.com/ico88/OmniDesk/actions/runs/39300451667 e
`windows-agent-package` run
https://github.com/ico88/OmniDesk/actions/runs/39300451660 sono riusciti; il
run Windows ha ricompilato l'MSI, verificato installazione/recovery/uninstall e
upgrade/repair/rollback contro entrambe le baseline storiche.

### Portale personale — screenshot di ticket con consenso — 2026-10-19

Il portale permette di allegare allo screenshot iniziale solo un PNG fino a
512 KiB, selezionato dall'utente e accompagnato da una checkbox di consenso
esplicito. L'API applica la stessa validazione strutturale/limiti del percorso
Agent, conserva l'immagine nella colonna screenshot già presente e registra
l'evento di consenso senza inserire il contenuto nei log/outbox. La lettura ha un
endpoint autenticato che verifica account attivo, proprietà del ticket, link
account-dispositivo attivo, sede non archiviata e credenziale dispositivo non
revocata; restituisce `no-store`/`nosniff`, registra l'accesso e diventa 404 dopo
revoca. Il portale mostra l'immagine nella conversazione. Gli allegati generici,
storage esterno, antivirus e retention non sono inclusi.

`./scripts/test.sh` passato con 64/64 xUnit/PostgreSQL, build TypeScript/Vite e
5/5 Vitest. `PortalScenario` verifica consenso assente, CSRF, PNG valido,
idempotenza, lettura byte-for-byte, isolamento dell'altro account e revoca;
Chromium su Compose isolato verifica ora il flusso browser end-to-end descritto
sopra. Compose standard/test e `git diff --check` passati.
`docker compose build api dashboard` passato. Immagini aggiornate solo
localmente: nessun container, migrazione o dato LAN è stato avviato o aggiornato.
La verifica runtime sulla LAN resta aperta.

Push del commit `cff9e0b842e054c028dd0a4a33f1da048e997852` sul branch
`feature/windows-support-lan`: GitHub Actions [build-and-test
39302068936](https://github.com/ico88/OmniDesk/actions/runs/39302068936) e
[windows-agent-package
39302068937](https://github.com/ico88/OmniDesk/actions/runs/39302068937) entrambi
riusciti. Il runner Windows ha compilato Agent/Tray/MSI e superato installazione,
recovery, disinstallazione e upgrade/repair/rollback; l'installer nativo è
saltato come configurato. Nessun deploy sul server LAN.

### Branding Tray e favicon — 2026-10-19

La Tray usa `assets/omnidesk.ico` per l'icona dell'eseguibile e per
`NotifyIcon`; il test Windows del pacchetto verifica l'icona incorporata. La
favicon è stata riallineata alle stesse proporzioni e allo stesso simbolo
monitor, mantenendo il blu OmniDesk. La favicon è un asset SVG servito
direttamente dalla dashboard.

`favicon.svg` supera il parsing XML e `git diff --check`. In questo ambiente
host non sono installati `npm` né `dotnet`; il packaging Windows e le verifiche
Linux hanno superato GitHub Actions sul commit `91a8522` (run 39306240849 e
39306240756). La compilazione Docker del successivo incremento SD1 è registrata
qui sotto; nessun servizio LAN è stato riavviato.

### SD1 — correlazione ticket e interventi RMM — 2026-10-19

La console IT elenca sessioni remote e job RMM già registrati sui dispositivi
collegati al ticket e consente di associarli alla cronologia interna. Creazione
e lettura rivalidano sul server i permessi ticket (`tickets.manage`/`tickets.view`)
e la capability corrispondente sul dispositivo collegato. I riferimenti sono
idempotenti per ticket, tipo e ID artefatto anche se lo stato del job cambia;
viene conservato uno snapshot con dispositivo, capability, tipo, stato e orario.
Payload, risultati diagnostici, URL di sessione e credenziali non entrano nella
cronologia. Il messaggio RMM non appare nella chat pubblica né nella proiezione
interna note, ma nell'endpoint dedicato che filtra i risultati per permesso.
Non avvia sessioni o job e non introduce migrazioni.

`./scripts/test.sh` passato: 64/64 test API/PostgreSQL, build TypeScript/Vite e
5/5 Vitest. I test coprono autorizzazione negata, device non collegato, tipo
artefatto non supportato, associazione idempotente dopo cambio stato, snapshot
senza output sensibile e isolamento scoped. `./scripts/smoke.sh --browser`
passato dopo aver ricostruito prima `omnidesk-api:local` e
`omnidesk-dashboard:local`: Chromium crea un job diagnostico isolato, lo
seleziona nella UI ticket e verifica il collegamento persistito. Compose smoke
è isolato e rimosso al termine; nessuna istanza LAN è stata aggiornata. Le
immagini locali riflettono questo sorgente, ma non sono state distribuite.
`docker compose config -q`, Compose test config e `git diff --check` sono
passati. A esecuzione CI completata, risultati registrati di seguito.

CI GitHub Actions sul commit `c85c24f` conclusa con successo:
[build-and-test 39310091486](https://github.com/ico88/OmniDesk/actions/runs/39310091486)
ha completato build e smoke Linux; [windows-agent-package
39310091476](https://github.com/ico88/OmniDesk/actions/runs/39310091476) ha
completato publish Console Windows, build Agent/Tray/MSI, installazione,
recovery, disinstallazione e verifica upgrade/repair/rollback contro la
baseline storica. L'MSI è stato collaudato sul runner isolato, non sul PC LAN.
Nessun deploy, riavvio o mutazione del server LAN.

### SD1 — tempi verificabili nella cronologia degli interventi RMM

Lo snapshot immutabile conserva stato e `occurredAt` al momento del collegamento.
La lettura autorizzata rivalida l'artefatto tramite tipo, ID e dispositivo,
restituendo anche lo stato e `completedAt` correnti se il job esiste ancora; un
artefatto rimosso resta consultabile tramite lo snapshot senza payload. La
console visualizza stato corrente e storico quando differiscono, avvio,
completamento e durata derivata solo da timestamp disponibili e coerenti. I
record delle sessioni remote non mostrano un orario di fine: il modello Mesh
non registra la chiusura effettiva, quindi l'interfaccia non la deduce da
`ExpiresAt` o dallo stato.

`./scripts/test.sh` passato: 64/64 test xUnit/PostgreSQL e 5/5 Vitest, compresi
assertions sui timestamp persistiti e sull'assenza del risultato diagnostico.
`docker compose build api dashboard` passato. `./scripts/smoke.sh --browser`
passato su stack temporaneo isolato; Chromium verifica che l'intervento
collegato mostri l'orario di avvio. Il test PostgreSQL modifica lo stato del job
dopo il collegamento e verifica che l'API mantenga lo stato storico e mostri
quello corrente più il completamento. `docker compose config -q`, Compose test
config e `git diff --check` passati. Nessuna migrazione, deploy o modifica al
server LAN.

Sul commit `815cd76`, GitHub Actions ha verificato nuovamente entrambi i target:
[build-and-test 39312456323](https://github.com/ico88/OmniDesk/actions/runs/39312456323)
e [windows-agent-package
39312456274](https://github.com/ico88/OmniDesk/actions/runs/39312456274) sono
riusciti. Il runner Windows ha superato publish Console, build Agent/Tray/MSI,
installazione, recovery, disinstallazione e upgrade/repair/rollback. Il collaudo
resta CI isolata: nessuna installazione sul PC Windows LAN e nessun deploy del
server.

### SD1 — transizioni di stato del workflow predefinito — 2026-10-19

`TicketWorkflowPolicy` definisce le transizioni consentite e il dettaglio
ticket (`GET /api/support/{id}/messages`) restituisce gli stati selezionabili.
La UI usa tale elenco e l'API rifiuta transizioni non ammesse dopo il controllo
versione, con risposta 409 che include stato corrente e transizioni permesse.
Per risolvere serve una risposta pubblica; chiudere un ticket già risolto non
richiede un duplicato. Riaprire cancella il campo di risoluzione attuale, mentre
la risposta storica resta nella conversazione. Non è stata aggiunta una
migrazione; i workflow personalizzabili e versionati restano da sviluppare.

`./scripts/test.sh` passato: 77/77 test API/PostgreSQL e 5/5 Vitest. I test
verificano la matrice di transizione, API default-deny per transizioni vietate,
risoluzione, riapertura, chiusura terminale e risposta pubblica preservata.
`docker compose build api dashboard`, configurazione Compose standard/test,
`git diff --check` e test API/dashboard passati. La pipeline Linux sul commit
`f2cfb05` ha invece rilevato che lo smoke browser leggeva le opzioni di stato
prima del completamento della richiesta thread; non è una regressione API. La
correzione dell'attesa è verificata nel successivo incremento sotto. Nessuna
migrazione, deploy o modifica all'istanza LAN per questo incremento.

### SD1/RMM — timestamp verificato di chiusura sessione remota — 2026-10-19

Migrazione `RemoteSessionEndTime` aggiunge `RemoteSessions.EndedAt` nullable,
senza backfill: i record storici non dichiarano un orario che il sistema non ha
registrato. Il valore viene scritto solo quando la revoca MeshCentral è
confermata, incluso il retry del guard, oppure quando la creazione fallisce e
non resta una revoca pendente. L'API sessioni lo espone e la timeline ticket lo
usa per completamento/durata, rivalidando stato e timestamp correnti. Permessi
ticket e capability RMM rimangono richiesti; nessun URL, credenziale o risultato
diagnostico viene aggiunto.

`./scripts/test.sh` passato con 77 test API/PostgreSQL e 5 Vitest; copre la
migrazione da schema vuoto, revoca sincrona/ritentata e timestamp nella timeline.
Il primo smoke browser ha rivelato una race nel test che non attendeva il thread;
aggiunta attesa sul controllo popolato. `./scripts/smoke.sh --browser` poi passato
su stack isolato con Chromium. `docker compose build api dashboard`, Compose
standard/test/backup-check (con `BACKUP_CHECK_FILE` esplicito) e `git diff
--check` passati. Nessun deploy LAN. Sul commit di codice `3446a70` sono
riusciti [build-and-test 39316014360](https://github.com/ico88/OmniDesk/actions/runs/39316014360)
e [windows-agent-package 39316014371](https://github.com/ico88/OmniDesk/actions/runs/39316014371),
inclusi lifecycle MSI e baseline upgrade/repair/rollback. Il successivo commit
`562bfe7` aggiorna solo questa documentazione.

## SD1 — workflow ticket configurabili e versionati — 2026-10-19

Aggiunte bozze workflow globali o per sede, modifica con versione attesa,
pubblicazione in snapshot immutabili e disattivazione per i soli nuovi ticket.
Alla creazione, un ticket fissa la versione attiva della propria sede, altrimenti
il workflow globale; quando nessun workflow è attivo usa la policy predefinita.
I ticket esistenti continuano a risolvere la propria versione dopo nuove
pubblicazioni o disattivazioni. API e UI amministrative sono protette dal permesso
globale `administration`; le transizioni restano server-authoritative, con
etichette e stati ammessi esposti alla console. La migrazione aggiunge tabelle
versionate e binding nullable, senza riscrivere i ticket precedenti.

`./scripts/test.sh`: 79/79 test xUnit con PostgreSQL effimero/migrazioni e 5/5
Vitest; include autorizzazione admin/scoped, precedenza sede/fallback globale,
conflitto su workflow duplicato e versione attesa, immutabilità/binding delle
versioni ticket, validazione delle transizioni e risoluzione. Build TypeScript e
Vite incluse nella suite. `docker compose build api dashboard` passato;
`docker compose config -q`, `docker compose -f compose.test.yaml config -q` e
`BACKUP_CHECK_FILE=/tmp/omnidesk-test-backup.dump docker compose -f
compose.backup-check.yaml config -q` passati. `./scripts/smoke.sh --browser`
passato su stack temporaneo separato: migrazioni/bootstrap/HTTPS e Chromium
creano, modificano, pubblicano e disattivano un workflow dall'interfaccia admin,
poi verificano i flussi UI esistenti. `git diff --check` passato.

Nessuna migrazione o immagine è stata applicata al server LAN; nessun servizio
live è stato riavviato. Questa registrazione copre solo la suite isolata e non
è attestazione runtime del server Windows/LAN. Sul commit `df011c8`,
[build-and-test 39319355985](https://github.com/ico88/OmniDesk/actions/runs/39319355985)
è passato, inclusi test, build immagini e browser smoke. Il workflow
[Windows 39319355926](https://github.com/ico88/OmniDesk/actions/runs/39319355926)
ha completato con successo build MSI, installazione/recovery/disinstallazione e
build console; il controllo upgrade/repair/rollback dalle baseline è ancora in
corso, quindi il gate Windows complessivo resta aperto.

## SD1/SD3 — riepiloghi strutturati degli interventi RMM — 2026-10-19

Aggiunta `SupportInterventionSummaries`, tabella append-only con ticket, device,
artefatto RMM già collegato, capability, autore, riepilogo, esito controllato,
seguito opzionale e timestamp server-side. API di creazione richiede versione
attesa, `tickets.view` e `tickets.manage` sul ticket e device e la capability
RMM. La lettura rivalida `tickets.view` e capability per ogni device e omette le
righe fuori scope. L'interfaccia ticket permette di registrare e consultare i
riepiloghi accanto agli artefatti correlati. Timestamp d'avvio/completamento
provengono dall'artefatto e non sono presentati come durata dell'attività umana.
Audit e outbox registrano riferimenti/esito senza copiare il testo del riepilogo.
La migrazione è additiva; la rimozione utente conserva la riga con autore nullo.

`./scripts/test.sh`: 79/79 test xUnit con PostgreSQL effimero/migrazioni e 5/5
Vitest; scenari verificano esito/seguito obbligatorio, append-only, versioni
attese, `tickets.view/manage`, capability artefatto e assenza del testo privato
da audit/outbox. Build TypeScript/Vite incluse. `docker compose build api
dashboard` passato; Compose standard, test e backup-check (con
`BACKUP_CHECK_FILE` esplicito) validati. `./scripts/smoke.sh --browser` passato
su stack isolato: Chromium collega il job RMM, registra e rilegge il riepilogo
dalla UI. `git diff --check` passato.

Nessuna migrazione o immagine è stata applicata al server LAN e nessun servizio
live è stato riavviato. La migrazione e gli endpoint sono verificati sul database
temporaneo; CI Linux `39322836205` passata su `c410f48`. Il gate Windows
`39319355926` è ancora impegnato nella verifica upgrade/repair/rollback; il
workflow Windows per `c410f48` è in coda. I test non attestano tempi reali di
lavoro umano né runtime Windows/LAN.

## SD1 — priorità ticket P1–P4 — 2026-10-19

`SupportTickets.Priority` usa ora `p1` critica, `p2` alta, `p3` normale e `p4`
bassa, con default `p3`. La migrazione `TicketPrioritiesP1P4` converte i valori
legacy esistenti. Per consentire un aggiornamento progressivo dei client,
creazione, modifica e filtro API accettano ancora `urgent/high/normal/low`, ma
le risposte e i nuovi record usano sempre il codice canonico. La dashboard mostra
etichette P1–P4 e il contatore urgente ora corrisponde a P1.

La suite `./scripts/test.sh` è passata: 79 test xUnit/PostgreSQL e 5 Vitest.
`MigrationTests` crea ticket con ciascuno dei quattro valori legacy prima di
applicare le migrazioni restanti e verifica la conversione; gli scenari API
verificano valori canonici, alias compatibili e filtri. `docker compose build api
dashboard` e `./scripts/smoke.sh --browser` sono passati, incluso il controllo dei
filtri P1–P4 nella UI. `git diff --check` è passato.

Questa modifica è pubblicata sul branch ma non distribuita sulla LAN. CI Linux
`39324222545` è passata sul commit `6161758`, inclusi test, migrazioni, build
container e smoke browser. Il job Windows precedente è tuttora in corso sul test
upgrade MSI; il nuovo workflow Windows è in coda e il run precedente non è stato
interrotto.

## SD1 — partecipanti ticket scoped — 2026-10-19

La migrazione `SupportTicketParticipants` aggiunge una relazione univoca ticket/
account con autore e timestamp. API e console consentono di elencare, aggiungere
e rimuovere account attivi che hanno già `tickets.view` sul device/sede; le
mutazioni richiedono `tickets.manage`, lock, versione attesa, audit e outbox. La
partecipazione non concede accesso: letture e notifiche verificano di nuovo
account attivo e permesso. Le notifiche coprono aggiunta, risposta pubblica e
aggiornamento successivo alla data di ingresso, senza inoltrare contenuto privato.
La dashboard mostra partecipanti e permette le modifiche solo agli operatori
autorizzati.

Verifica: `./scripts/test.sh` passato con 79 xUnit/PostgreSQL e 5 Vitest; il test
include lettore scoped, utente senza grant, rifiuto della scrittura, notifiche,
audit e revoca relazione. `docker compose build api dashboard`, `docker compose
config -q`, `git diff --check` e `./scripts/smoke.sh --browser` passati; Chromium
ha verificato aggiunta/rimozione in UI nel compose effimero, insieme alle
regressioni preesistenti. Un primo smoke aveva usato immagini dashboard obsolete;
è stato ripetuto dopo build e con l'utente read-only predisposto prima di aprire
il ticket. CI Linux `39326865633` passata sul commit `dc07520`, inclusi test,
build delle immagini, configurazione Compose e smoke browser. Nessun deploy,
migrazione o test sull'istanza LAN. CI Windows MSI `39326865702` in coda e il run
precedente `39324222524` ancora attivo; non interrotti. Allegati generici in
quarantena, notifiche esterne, email, allarmi, SLA e automazioni restano da
sviluppare.

## SD1 — allegati ticket con scansione antivirus — 2026-10-19

Implementati la migrazione `SupportAttachmentsQuarantine`, storage filesystem
privato con key casuale, hash SHA-256 e quote (10 MiB/file, 50 file e 100 MiB per
ticket), API staff/portale, consenso esplicito per gli upload pubblici, visibilità
public/internal, download forzato come attachment solo dopo scansione pulita e
UI console/portale con aggiornamento dello stato di scansione. Il worker usa claim
PostgreSQL `SKIP LOCKED`, lease e retry durevoli; stati terminali infetto e
scansione fallita non consentono il download. ClamAV 1.4.5 è fissato al digest
`sha256:4de20bd9ab45a4b763c5412b769217ef5082572ebc8a63aff1a77943419e5dd8`;
la porta 3310 non è pubblicata e il servizio è raggiungibile dall'API solo sulla
rete Compose privata. Lo script smoke prova il protocollo reale del motore con
fixture pulito ed EICAR. Il test API separato usa uno scanner fake deterministico
per verificare quarantena, pulito, infetto, download e autorizzazione; i test
scanner verificano framing INSTREAM e risposta ClamD.

Verifiche locali: `docker compose -f compose.test.yaml up --build
--abort-on-container-exit --exit-code-from tests` passato con 82 xUnit/PostgreSQL;
build production API/dashboard passata; npm build e 6 Vitest passati; `docker
compose config -q` e Compose smoke config passati; `./scripts/smoke.sh --browser`
passato su stack effimero, incluse migrazioni, login/HTTPS, ClamAV clean/EICAR e
Chromium. Il backup ora copia i blob dopo lo snapshot PostgreSQL e genera un
manifest SHA-256; questo ordinamento si basa sui blob immutabili (l'app non li
cancella). Non è stato eseguito un backup o restore completo dei blob sul server.
Nessun servizio LAN è stato riavviato o aggiornato; questa implementazione non è
ancora pubblicata e non ha ancora esito CI sul nuovo commit.

### SD1 follow-up — retry scansione e integrità restore blob — 2026-10-19

L'operatore autorizzato con `tickets.manage` può riaccodare una scansione in
stato terminale `scan_failed`; il server azzera tentativi, lease ed errore,
registra audit e rimette il blob in quarantena. Lo stato `infected` e il download
restano invariati. Il test PostgreSQL prova diniego al lettore scoped, retry
autorizzato, reset a quarantena, nuova scansione pulita e accesso finale.

Il verificatore backup ripristina il dump in PostgreSQL isolato e confronta ogni
`SupportAttachments.StorageKey` con il relativo file nell'archivio; i checksum
sono verificati prima del restore. Prova locale su dump PostgreSQL effimero:
riferimenti validi accettati, blob mancante rifiutato. È un controllo DB/blob,
non un avvio completo dell'API ripristinata.

Verifiche follow-up: 82/82 test xUnit/PostgreSQL; build dashboard e 6/6 Vitest;
backup verifier testato con PostgreSQL 17 temporaneo; `bash -n`, Compose standard,
Compose smoke, Compose test e Compose backup-check config, `git diff --check`
passati. Il codice follow-up non è ancora stato pubblicato né verificato in CI;
nessun test o servizio LAN è stato interrotto.

### SD1 follow-up — notifica allegato pulito — 2026-10-19

Il worker di scansione emette `ticket.attachment.ready` nell'outbox solo per un
verdetto pulito. Il consumer crea notifiche per coda/assegnatario e collaboratori,
ricontrolla account attivo e `tickets.view`, ed esige `tickets.note.internal` per
notifiche relative ad allegati interni. Il payload non contiene filename, hash o
contenuto; la dashboard distingue la notifica con icona graffetta.

Lo scenario PostgreSQL verifica che un lettore scoped riceva la notifica per il
file pubblico pulito ma non per gli allegati interni, e copre insieme il retry
autorizzato della scansione fallita. Suite API 82/82, build dashboard e 6/6
Vitest passati; `docker build` API e dashboard riusciti. Configurazioni Compose,
`bash -n`, `git diff --check` e `./scripts/smoke.sh --browser` passati sulle
immagini aggiornate, incluse scansioni ClamAV clean/EICAR e prove Chromium.
Modifica non pubblicata e non inclusa nei job CI Windows attivi/in coda.
I gruppi di concorrenza GitHub Actions sono stati differenziati per SHA commit,
così un push successivo non sostituisce un job precedente ancora in attesa. I run
Windows in corso e in coda non sono stati cancellati.

### Allegati self-service nel portale — 2026-10-19

Esteso `PortalScenario` per coprire consenso obbligatorio, upload multipart,
visibilità limitata al richiedente, quarantena con download negato, e revoca
dell'accesso al dispositivo. Esteso lo smoke Chromium sul portale reale dello
stack isolato: carica un file innocuo, richiede consenso esplicito, attende il
verdetto ClamAV pulito e verifica il link di download prima di proseguire con
chat e revoca.

Verifiche: `./scripts/test.sh` passato dopo aver isolato l'installazione npm in
una copia temporanea: 82/82 test API, build dashboard e 6/6 Vitest. Anche
`./scripts/smoke.sh --browser` è passato con migrazioni, login, ClamAV
clean/EICAR e scenario Chromium completo, incluse le nuove verifiche degli
allegati portale. `bash -n scripts/test.sh scripts/smoke.sh` e
`git diff --check` passati. CI GitHub sul commit `25724bc`: Linux run
`39331892091` completata con successo, inclusi suite, build Docker e smoke;
Windows run `39331892158` completata con successo, inclusi contratti Tray,
compilazione MSI, install/recovery/uninstall, upgrade/repair/rollback delle
baseline e pubblicazione artifact non firmati. Nessun servizio LAN è stato
modificato; l'artifact non è stato promosso né distribuito.

### Restore applicativo isolato — 2026-10-19

Esteso `verify-backup.sh` per verificare il manifest completo dei nuovi backup
e, se sono presenti dump, chiavi Identity, dati Caddy e password DB, avviare un
restore applicativo effimero. Il progetto Compose usa rete `internal`, nessuna
porta host e volumi univoci rimossi al termine; DB e migrazioni restano in copia.
Il secret DB è montato da una directory privata temporanea e non viene stampato.

`bash scripts/verify-backup.sh .backups/20261019T153639Z.mzZpdG` passato:
PostgreSQL ha importato il dump (29 migrazioni storiche, 1 account, 1 dispositivo),
l'immagine API locale corrente ha applicato le migrazioni pendenti; readiness,
diniego anonimo, CSRF e cookie Secure su HTTPS con CA Caddy ripristinata passano.
Il test ha rilevato e corretto il mancato inoltro HTTPS nel proxy temporaneo. Il
backup usato è storico: non contiene allegati né manifest generale, quindi la
prova allegati/integrità generale resta aperta. Mesh non è stata ripristinata.
`bash -n`, `docker compose config` isolato e `git diff --check` passati. Nessun
servizio LAN modificato.

Aggiornamento del gate restore: `./scripts/smoke.sh --browser` passato anche
dopo aver aggiunto la creazione di un archivio effimero dallo stack appena
collaudato. Il fixture conteneva un ticket con un allegato pulito; il manifest
generale, i checksum blob e i riferimenti nel DB sono risultati validi. Il
verificatore ha ripristinato DB, file, chiavi Identity e storage CA in volumi
isolati, migrato il DB senza modifiche pendenti, avviato API e Caddy e superato
probe HTTPS/CSRF/anonimo. Lo scenario browser ora scarica il contenuto reale e
verifica che la revoca dell'accesso portale non elimini il file visibile allo
staff. Il trasferimento del fixture legge i volumi con gli UID dei container,
senza allargare ACL. Questo prova il percorso software con dati sintetici; il
restore di una fotografia LAN/off-site e i dati Mesh restano non verificati.

### Retention outbox e restore CI — 2026-10-19

Implementata retention configurabile degli eventi outbox consegnati (default
180 giorni, 0 per disabilitare): sono eliminati solo gli eventi con almeno una
consegna e senza consegne pendenti o dead-letter; notifiche collegate e ricevute
vengono eliminate nella stessa transazione e il conteggio è auditato. Retry,
dead-letter, eventi senza consumer e tutti i contenuti di ticket/allegati restano
conservati. Il test PostgreSQL dedicato verifica le ricevute collegate, i casi
esclusi e il record di audit; la cascade delle notifiche è definita dalla FK.

`./scripts/test.sh` è passato localmente: 83/83 test API, build production della
dashboard e 6/6 Vitest. La CI Linux `39336034038` ha rilevato un difetto di
ambiente nel restore completo: il container `alpine:3.22` usato dal passaggio
restore-seed non veniva scaricato perché aveva `pull_policy: never`. Il comando
ora richiede il pull solo quando l'immagine manca (`--pull missing`); lo smoke
locale di questa revisione è passato: `./scripts/smoke.sh --browser` ha eseguito
browser Chromium, scansioni ClamAV clean/EICAR, backup con allegato e restore
completo DB/blob/chiavi/CA con probe HTTPS/CSRF/anonimo. La suite completa passa
83/83 API e 6/6 Vitest con build production. Il workflow Windows
`39336034027` è terminato con successo, inclusi upgrade/repair/rollback MSI.
La CI Linux `39337297948` è terminata con successo sul commit `da91205`; CI
Windows `39337297929` è ancora in esecuzione. Nessun servizio LAN è stato
modificato.

### Policy SLA versionate e calendario lavorativo — 2026-10-19

`BusinessCalendar.AddWorkingTime` calcola tempo lavorativo con finestre
settimanali non sovrapposte, festività e fuso IANA. Gli orari locali inesistenti
avanzano al primo minuto valido; nell'ora autunnale ripetuta, l'intervallo apre
alla prima e chiude all'ultima occorrenza. I quattro test del calcolatore coprono
weekend/festività e i due cambi DST.

Le migrazioni PostgreSQL `SupportSlaPolicies` e `TicketSlaTargets` aggiungono
policy globali/per sede e versioni immutabili JSONB con autore/data di
pubblicazione, calendario e minuti di primo riscontro/risoluzione. Tutte le tre
origini ticket (Agent, portale e operatore) applicano la policy della sede, con
fallback globale, e persistono due target iniziali che conservano policy,
versione, ciclo e istanti di inizio/scadenza. Gli endpoint amministrativi esigono
`tickets.sla.manage`, rispettano lo scope della sede e il versionamento
ottimistico. Il test API/PostgreSQL verifica diniego anonimo, gestione site
scoped, diniego cross-site, pubblicazione di una nuova versione, conflitto stale
e assenza del permesso per Operator; verifica inoltre snapshot della versione e
scadenze calcolate sul calendario lavorativo, inclusa la settimana successiva.
`./scripts/test.sh` passa: 88 test API/PostgreSQL, build production dashboard e
6/6 Vitest. I target iniziali sono applicati ai nuovi ticket; la risposta
pubblica dell'operatore completa idempotentemente il primo riscontro e la
transizione a resolved/closed completa la risoluzione nella stessa transazione
ticket. Il test verifica timestamp, indipendenza dei due target e idempotenza.
`SlaBreachWorker` seleziona una scadenza alla volta con `FOR UPDATE SKIP LOCKED`,
persiste `BreachedAt` e un audit di sistema nella stessa transazione; il test
PostgreSQL porta entrambi i target fixture oltre scadenza, verifica due sole
registrazioni e ripete la scansione senza duplicati, quindi annulla la fixture.
Nessuna notifica o escalation viene dichiarata attiva. Restano pause/ripresa,
cicli di riapertura, deduplica escalation e consegna degli allarmi.

CI Linux e Windows `39339233583`/`39339233542` sono verdi per la policy SLA
base. Sul commit dei target iniziali, CI Linux `39340488203` è verde; il job
Windows `39340488177` sta completando la baseline MSI upgrade/repair/rollback.
Ricostruito `omnidesk-api:local` dai sorgenti aggiornati, `./scripts/smoke.sh
--browser` è passato dopo entrambe le modifiche applicative, applicando entrambe le migrazioni
`20261019225641_SupportSlaPolicies` e `20261019230803_TicketSlaTargets`;
verificati stack HTTPS, Chromium, ClamAV clean/EICAR, backup con allegato e
restore completo isolato DB/blob/chiavi/CA con probe HTTPS/CSRF/anonimo. Il
completamento dei target è stato poi verificato con una seconda suite API e un
secondo smoke runtime locale. Aggiunti endpoint `GET /api/support/{id}/sla`
protetto da `tickets.view` e pannello degli obiettivi nel dettaglio ticket; lo
browser smoke crea una policy, verifica i due target, vede entrambi e controlla
il completamento dopo la risposta/risoluzione. Dopo rebuild API/dashboard,
`./scripts/test.sh` e `./scripts/smoke.sh --browser` sono passati (88 test API,
6 Vitest, migrazioni, Chromium, ClamAV e restore isolato). L’estensione SLA iniziale è stata verificata localmente. Il worker breach è
stato validato con la suite PostgreSQL e il rebuild API; il successivo smoke
Chromium ha verificato migrazioni, UI SLA e restore. Nessun servizio LAN è stato
modificato; ogni nuova estensione attende la propria CI sul push.

### SLA breach notification — 2026-10-19

- `./scripts/test.sh` — passed: 88 API/PostgreSQL tests, production dashboard build, 6/6 Vitest. The SLA scenario forces an overdue target, verifies one durable breach audit/outbox event and confirms the notification is projected to a scoped, active `tickets.view` recipient.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .` — passed.
- `docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .` — passed. (The Docker build context is the repository root because the Dockerfile copies `deploy/dashboard.Caddyfile`.)
- `docker compose config --quiet`, `docker compose -f compose.test.yaml config --quiet`, and `git diff --check` — passed.
- `./scripts/smoke.sh --browser` — passed with isolated containers: migrations, HTTPS/UI, ClamAV, Chromium portal/ticket/SLA workflows and full isolated DB/blob/identity-key/CA restore. The live LAN service was not touched. Runtime worker delivery of an actual expired SLA notification was not separately observed; that path is covered by the PostgreSQL integration test.


### Amministrazione policy SLA — 2026-10-19

- `./scripts/test.sh` — passed after adding the scoped `/api/admin/support-sla/scopes` endpoint and the immutable policy-name behavior: 88 API/PostgreSQL tests, production dashboard build, 6/6 Vitest. The SLA test checks anonymous denial, site-only scope disclosure, cross-site denial, stable policy identity and per-version names.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .` and `docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .` — passed.
- `docker compose config --quiet`, `docker compose -f compose.test.yaml config --quiet`, `git diff --check`, and `node --check scripts/browser-smoke.mjs` in `node:22-alpine` — passed.
- `./scripts/smoke.sh --browser` — passed with isolated containers. Chromium created a policy in the dashboard, published two immutable revisions, edited a split-day calendar, verified the stored two-interval definition and confirmed a newly created ticket snapshots version 3. Migration/HTTPS/ClamAV, ticket/portal flows and full isolated DB/blob/identity-key/CA restore also passed. Live LAN services were not modified.
- CI `39342551037` (build/test) and `39342551020` (Windows package) passed for parent commit `ada0320`; the policy-admin changes in this entry are uncommitted at validation time and require CI after push.

### SLA pause/resume and reopen cycle — 2026-10-20

- `./scripts/test.sh` — passed: 88 API/PostgreSQL tests, production dashboard build, 6/6 Vitest. Coverage includes configured workflow pause states, overdue targets remaining breachable, paused targets excluded from breach processing, resumed deadlines and accumulated duration, resolution cycle creation on reopen, and scoped breach notifications.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .` and `docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .` — passed.
- `./scripts/smoke.sh --browser` — passed on rebuilt local images: migrations/bootstrap, HTTPS, Chromium SLA administration and ticket flows, ClamAV clean/EICAR scan, and isolated DB/blob/Identity-key/CA backup restore. Live LAN services were not modified.
- Runtime delivery of an actual expired SLA notification on the LAN, email/escalation channels, production restore, and CI for the pending commit remain unverified.

### SLA escalation steps and hosted-worker runtime — 2026-10-20

- `./scripts/test.sh` — passed: 88 API/PostgreSQL tests, production dashboard build, 6/6 Vitest. SLA coverage verifies policy validation/version snapshots, pause/resume and reopen cycles, ordered escalation thresholds, no emission before a step, once-only emission on repeated worker passes, persisted audit/outbox entries, and scoped notification projection.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .` and `docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .` — passed.
- `./scripts/smoke.sh --browser` — passed with rebuilt images and isolated PostgreSQL/API/dashboard/Caddy/ClamAV containers. Chromium persisted the escalation policy; the live hosted breach worker processed an overdue fixture, the escalation worker emitted the configured 15-minute step from the persisted breach time, and the notification worker projected it to an authorized recipient. Full DB/blob/Identity-key/CA restore passed. No LAN container or service was modified.
- `bash -n scripts/smoke.sh`, `git diff --check`, and both production/test Compose configuration checks — passed. Runtime on the LAN, external email/escalation delivery, CI for this pending commit, and release promotion remain unverified.

### Portale personale — refresh automatico — 2026-10-20

- `./scripts/test.sh` — passed after adding 30-second background refresh for the portal device/ticket list and selected conversation. The production TypeScript/Vite build and 6/6 dashboard Vitest tests passed; the API/PostgreSQL suite completed successfully.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .` and `docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .` — passed. After rebuilding these local images, `./scripts/smoke.sh --browser` passed: Chromium saw a staff status change and public reply on the already-open portal page without reload, verified that internal notes stay hidden, and the complete isolated SLA worker/backup restore probes passed. Transient polling failures retain the last successful view.
- A prior browser smoke against stale local images was discarded; the current evidence uses rebuilt API/dashboard images. A real authenticated customer account and LAN runtime remain unverified; no LAN service was touched.
- The Tray executable and notification icon plus the web favicon were already unified on `assets/omnidesk.ico` and `/favicon.svg` in commits `93d3f6c` and `91a8522`; this refresh change does not alter those assets.

### SD3 — webhook intake email firmato — 2026-10-20

- `./scripts/test.sh` — passato dopo la migrazione `SupportEmailIntakes`: 90 test API/PostgreSQL, build production TypeScript/Vite e 6/6 test Vitest. Lo scenario verifica firma alterata e timestamp scaduto/range estremo, mailbox autorizzata, accettazione idempotente, conflitto sullo stesso Message-ID con corpo diverso, accesso negato anonimo, consultazione/discard con `tickets.manage` e audit; non viene creato un ticket automaticamente.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .` — passato. `docker compose config --quiet`, `docker compose -f compose.test.yaml config --quiet` e `git diff --check` — passati.
- Test e database sono isolati; nessun servizio LAN è stato aggiornato o riavviato. Il webhook non è stato esercitato contro un provider reale e rimane disattivato se chiave o allowlist non sono configurate. Non ancora verificati runtime live, retention dei dati email, adattatore provider, thread binding, promozione in ticket o invio/ricezione reply. CI `39350220043` (Linux) e `39350220086` (Windows packaging) sono passate sul commit endpoint `eef1853`.

### SD3 — inbox dashboard per intake email — 2026-10-20

- `./scripts/test.sh` — passato: 90 test API/PostgreSQL, build production TypeScript/Vite e 6/6 Vitest. `docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .`, entrambe le configurazioni Compose e `git diff --check` — passati.
- La vista mostra solo i pending a utenti con permesso globale `tickets.manage`, renderizza il corpo come testo e richiede conferma prima dello scarto server-side auditato. Nessuna promozione a ticket o identità del mittente è introdotta. Lo smoke Chromium locale è passato; le nuove pipeline CI per il commit UI sono pendenti. Non distribuito sulla LAN.
- Correzione della fixture di test: la chiave webhook effimera è montata soltanto nei container API/Chromium necessari. `bash -n scripts/smoke.sh`, `node --check scripts/browser-smoke.mjs` (Node 22 Alpine), Compose principale/test e `git diff --check` — passati.
- `./scripts/smoke.sh --browser` — passato nello stack locale isolato dopo la correzione fixture. Chromium ha acquisito un payload HMAC, verificato la inbox, controllato che un body con `<script>` resti testo inerte, scartato il messaggio e verificato l’audit; sono passati anche probe HTTPS/ClamAV, worker SLA/escalation e restore runtime DB/blob/Identity/CA. Nessun servizio LAN è stato modificato.
- I componenti UI e smoke sono stati pubblicati in `7370317` e `5135bf4`; le pipeline CI attivate dal push sono in corso. Il workflow precedente sul commit API `eef1853` è passato su Linux (`39350220043`) e Windows Agent/MSI upgrade/repair/rollback (`39350220086`).

### SD3 — promozione controllata intake email — 2026-10-20

- `./scripts/test.sh` — passato: 90/90 test API/PostgreSQL, build TypeScript/Vite e 6/6 Vitest. I test controllano il deny anonimo, il deny di un ruolo senza `tickets.manage`, il rifiuto senza conferma esplicita del mittente non verificato, creazione autorizzata e ripetizione idempotente; il ticket email mantiene `RequesterUserId` nullo, associa solo il dispositivo scelto e registra stato/audit.
- Build delle immagini API e dashboard con Docker — passate. `docker compose config --quiet`, Compose test, `bash -n scripts/smoke.sh`, `node --check scripts/browser-smoke.mjs` con Node 22 Alpine e `git diff --check` — passati.
- `./scripts/smoke.sh --browser` — passato nello stack isolato: Chromium ha scelto un device, modificato l’oggetto, confermato la natura non verificata del mittente e creato/aperto il ticket; i restanti controlli Chromium, ClamAV, worker SLA/escalation/notifica e restore runtime DB/blob/Identity/CA sono passati. Nessun servizio LAN è stato toccato.
- Non verificati provider reali, collegamento mailbox/thread, reply email, CI per questo incremento o runtime LAN. I test usano chiavi e dati effimeri isolati; la promozione non attribuisce l’identità del mittente.

### Privacy — retention intake email — 2026-10-20

- `./scripts/test.sh` — passato: 91/91 test API/PostgreSQL, build TypeScript/Vite e 6/6 Vitest. Il test dedicato verifica purge dopo 30 giorni dei pending e 180 dei record discarded/promoted, conserva righe recenti e stati sconosciuti e controlla che l'audit contenga soltanto il conteggio.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .`, `docker compose config --quiet`, `docker compose -f compose.test.yaml config --quiet`, `bash -n scripts/smoke.sh` e `git diff --check` — passati.
- `./scripts/smoke.sh --browser` — passato: API con il nuovo worker avviata nello stack isolato, smoke Chromium, ClamAV, worker SLA/escalation/notifica e ripristino runtime DB/blob/Identity/CA completati. Nessun servizio LAN è stato toccato.
- Nessuna modifica allo schema o dipendenza. La retention intake non cancella il ticket promosso né i suoi messaggi; backup preesistenti seguono la policy separata. Retention ticket/allegati/audit/backup e test sulla LAN restano da definire/eseguire.
- CI sul codice `3be39f0`: Linux `39353156966` — passata; Windows `39353156938` — compilazione e test MSI install/recovery/uninstall passati, upgrade/repair/rollback ancora in esecuzione al momento della verifica. I successivi commit documentali non cambiano il codice.

### Verifica read-only istanza LAN — 2026-10-20

- `python3 scripts/verify-lan.py` — passato contro l’origine configurata nello script con CA locale: readiness HTTPS, bundle dashboard e rifiuto di `/api/auth/me` anonimo (`401`). Il controllo non esegue login, mutazioni, sessioni remote o test Windows.
- Questa evidenza descrive solo l’istanza già pubblicata al momento della verifica; non certifica né distribuisce il branch `feature/windows-support-lan`. Il server non è stato riavviato o modificato.

### SD4 — notifiche email outbound — 2026-10-20

- `./scripts/test.sh` — passato: 91 test API/PostgreSQL; il test integrato esercita il worker con SMTP fake, verifica stato `sent`, include numero/link ticket e controlla che il soggetto del ticket non venga divulgato. Build TypeScript/Vite e 6/6 Vitest passati.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .` e `docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .` — passati.
- `docker compose config --quiet`, `docker compose -f compose.test.yaml config --quiet` e overlay `compose.email.yaml` con configurazione SMTP temporanea — passati; `git diff --check` — passato.
- Migrazione `20261020023034_OutboundEmailNotifications` generata con EF Core 10.0.13 e applicata dalla suite sul PostgreSQL usa-e-getta. SMTP reale, ricezione mailbox, deploy LAN e CI per questi commit non sono stati verificati. La feature resta disabilitata nella configurazione Compose base; l'overlay richiede opt-in esplicito e secret esterno al repository.
- `./scripts/smoke.sh --browser` — passato su stack isolato: Chromium verifica anche il deep link `/?ticket=<id>` fino al dettaglio autorizzato e poi rientra nella dashboard; probe HTTPS/ClamAV, worker SLA/notifiche e ripristino runtime completo DB/blob/Identity/CA passati. Nessun servizio LAN è stato modificato.

### SD4 — recupero amministrativo consegne email — 2026-10-20

- `./scripts/test.sh` — passato: 91/91 test API/PostgreSQL. Lo scenario verifica preferenza esplicita attivabile, accesso negato a utenti non admin, lista priva dell'indirizzo destinatario e requeue atomico del dead letter con reset tentativi e audit.
- Build Docker API/dashboard e configurazioni Compose base/test — passate; dashboard TypeScript/Vite e Vitest 6/6 — passati.
- `./scripts/smoke.sh --browser` — passato su stack isolato: Chromium apre la pagina amministrativa e verifica la coda vuota; probe HTTPS, ClamAV, worker SLA/notifiche e restore DB/blob/Identity/CA passano. Nessun servizio LAN è stato toccato.
- CI Linux `39360140407` e Windows `39360140450` sono verdi per i commit outbound `9af0e30`; il pannello di recupero è un commit successivo e richiede nuove pipeline. SMTP reale non verificato.

### SD1 — retention opt-in allegati e screenshot — 2026-10-20

- `./scripts/test.sh` — passato: 92/92 test API/PostgreSQL; build dashboard TypeScript/Vite e 6/6 Vitest. Il test dedicato verifica opt-in disattivato per default, purge dei contenuti vecchi solo su ticket risolti/chiusi, preservazione dei record conversazione/ticket e audit per conteggio.
- Test mirato `Ticket_attachment_retention...` — passato su PostgreSQL usa-e-getta, inclusa applicazione della nuova migrazione `20261020043000_TicketAttachmentRetention`.
- `docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .`, `docker compose config --quiet`, `docker compose -f compose.test.yaml config --quiet` e `git diff --check` — passati.
- Non eseguiti smoke runtime completo, deploy LAN o CI per questa revisione; non risultano superati da questi test.
