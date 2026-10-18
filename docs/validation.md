# Verifiche M1 — 18 ottobre 2026

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

## Limiti espliciti

Non provati: apt install Docker e avvio systemd su Ubuntu pulito; setup --start end-to-end di produzione; DNS, porte pubbliche, rinnovo Let's Encrypt; rendering/interazioni in browser reale; deploy GitHub Actions (workflow preparato ma mai eseguito su GitHub); upgrade, backup/restore e carico. Lo smoke HTTPS usa una CA interna e il probe disabilita la validazione del certificato solo nel test: non certifica TLS pubblico. L'HTML è servito, ma questo non equivale a un test browser.

Non implementati: enrollment, agente/MSI Windows, telemetria/inventario, Windows Update, integrazione MeshCentral e console Windows. TOTP ha solo predisposizione Identity e protezione contro bypass del challenge non ancora disponibile. Layout widget per utente, upload logo, cambio/reset password, disabilitazione account, modifica/revoca ruoli globali e gestione completa gruppi/device restano fuori da M1. Le concessioni locali hanno create/list/revoke; i ruoli custom hanno create/list, non modifica.

Hardening residuo: chiavi DataProtection non cifrate a riposo (volume protetto da ACL); account PostgreSQL unico iniziale, da separare per migrazioni/runtime; immagini con tag major e non digest; lockout account e rate limit login condiviso per proxy; retention audit e metriche future. Nessuna attestazione di sicurezza per produzione.

Log locali non versionati: `/tmp/omnidesk-test-validation.log`, `/tmp/omnidesk-smoke-validation.log`; runtime smoke in `/tmp/omnidesk-smoke.*/runtime.log`. Container e reti di test arrestati/rimossi; secret temporanei restano nelle directory private per ispezione. Il cleanup dei test riguarda esclusivamente risorse create dai rispettivi progetti Compose.
