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
