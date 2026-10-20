# OmniDesk

Piattaforma RMM self-hosted per Windows. Implementazione iniziale M1–M5, destinata a collaudo controllato; i gate di produzione e le verifiche Windows mancanti sono riportati in [validation.md](docs/validation.md).

Disponibili: API ASP.NET Core .NET 10, PostgreSQL con migrazioni, Identity multiutente, CSRF/lockout, ruoli granulari globali/gruppo/device e audit, dashboard React con branding e layout personali, enrollment monouso, agente Windows .NET con snapshot metriche/inventario, coda Windows Update e console Windows WebView2. TOTP con recovery code implementata e cifrata nel DB, **disattivata per default**. Cambio password dalla console.

La schermata Utenti gestisce gruppi/concessioni, sospensione e riattivazione account, ruoli globali e revoca sessioni web. L'ultimo amministratore attivo è protetto; stato account e security stamp verificati a ogni richiesta. Backup locale e prova di ripristino PostgreSQL isolata: [istruzioni e limiti](docs/backup-restore.md).

Dalla scheda Dispositivi l'amministratore può modificare etichetta/gruppo e revocare l'identità con conferma. I job queued vengono rivalidati prima del ritiro: perdita permessi o revoca non autorizzano nuovi comandi. I lavori già avviati sul PC non vengono dichiarati interrotti dalla revoca server.

Adapter MeshCentral con sessioni limitate e audit, motore Docker opzionale: non abilitato dal setup standard. Rimane un blocco audit node-forge e manca il collaudo desktop/terminale/file su Windows/NAT; non dichiarato operativo in produzione. Il singolo MSI attuale installa solo OmniDesk Agent: integrazione Mesh agent e firma restano aperte. MSI unsigned compilato dalla pipeline Windows; installazione/upgrade/uninstall ancora da verificare.

## Installazione Ubuntu Server

Il deployment pubblico usa **https://omnidesk.cricatania.it**, con Caddy e certificati Let's Encrypt su TCP 80/443. Il DNS deve puntare all'IP pubblico del server e le porte devono già essere raggiungibili; l'installer non cambia firewall, router, SSH o servizi di sistema. L'indirizzo interno previsto per il nuovo host è `10.0.0.53`. Requisiti: Ubuntu, Docker Engine/Compose, Python 3 e curl. L'installazione Docker richiede opt-in esplicito con `--install-docker`.

La subnet Docker proxy è `172.29.84.0/24`: modificarla tramite `OMNIDESK_PROXY_SUBNET` in `.env` se confligge con reti esistenti. Non collegarvi container estranei. L'installazione usa il progetto Compose `omnidesk-production`, tag immagine `:production`, volumi distinti e segreti nuovi. In questo modo i container e i build di sviluppo, che usano il progetto `omnidesk` e tag `:local`, restano separati. Non importa database, volumi o credenziali da altri host.

```bash
./setup.sh                     # prepara .env e secret, senza avviare container
./setup.sh --start             # DNS, build, DB vuoto, migrazioni, bootstrap, avvio e HTTPS readiness
```

Solo su un server destinato a OmniDesk, con autorizzazione a installare pacchetti e attivare Docker:

```bash
sudo ./setup.sh --install-docker
sudo ./setup.sh --start
```

L'opzione `--install-docker` configura il repository apt ufficiale e abilita il servizio Docker; il normale setup non modifica servizi o firewall. Il daemon Docker può gestire autonomamente regole di rete all'avvio dei container. Non viene aggiunto alcun utente al gruppo docker.

L'amministratore iniziale viene creato solo con DB vuoto. La password casuale è nel file `.secrets/bootstrap_password`: trasferirla tramite un canale riservato, poi rimuovere il file. Nessun segreto viene stampato o salvato in Git. `.secrets` è 0700, file montati 0444 per permettere lettura ai processi container; proteggere anche i backup. Conservare `.secrets/db_password` e `.secrets/db_runtime_password`: il primo è riservato a migrazioni/provisioning, il secondo all'API. Cambiarli senza ruotare le rispettive password PostgreSQL interrompe il servizio.

Aprire `https://omnidesk.cricatania.it`. Il setup aspetta l'endpoint HTTPS e PostgreSQL pronto. Controllare anche dall'esterno della rete del server:

```bash
docker compose -p omnidesk-production -f compose.yaml -f compose.direct.yaml ps
docker compose -p omnidesk-production -f compose.yaml -f compose.direct.yaml logs --tail=100 api caddy
curl --fail https://omnidesk.cricatania.it/api/health/live
curl --fail https://omnidesk.cricatania.it/api/health/ready
./scripts/diagnose.sh
```

Il successo di setup include un health check HTTPS con validazione del certificato dal server. Verificare inoltre il dominio da una rete esterna per escludere problemi di NAT o instradamento. `./scripts/diagnose.sh` esegue questi controlli pubblici senza ignorare la verifica del certificato. La password iniziale è in `.secrets/bootstrap_password`: trasferirla tramite canale riservato, accedere e rimuovere il file. Le credenziali non sono recuperabili dal repository. Cambio password disponibile in Account e sicurezza. Reset assistito, disabilitazione account e gestione completa del ciclo di vita restano da completare.

## Test e sviluppo

Non sono richiesti SDK host se si usa Docker:

```bash
./scripts/test.sh
docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .
docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .
./scripts/smoke.sh --browser
```

Test PostgreSQL dedicato in tmpfs, senza porte host o segreti di produzione. Lo smoke test usa Caddy TLS interno e verifica i container reali; il probe non valida la CA locale e non prova Let's Encrypt. I secret temporanei smoke restano sotto `/tmp/omnidesk-smoke.*`, directory private, per ispezione; i container vengono arrestati a fine prova. Non lanciare gli stessi test contemporaneamente sullo stesso daemon (nomi progetto fissi).

Con SDK .NET 10 e Node 22: `dotnet build OmniDesk.sln`; per i test impostare TEST_DATABASE e ConnectionStrings__Database su un PostgreSQL di test vuoto. La suite di integrazione richiede DB fresco e applica migrazioni. Dashboard: `npm ci`, `npm run build`, `npm test` in src/dashboard. Il server Vite da solo non configura API o HTTPS: usare Compose per la console completa.

## Aggiornamenti e backup

Prima di aggiornare: backup PostgreSQL, directory secret, `.env`, volumi `identity_keys` e Caddy; verificare il ripristino in un ambiente separato. Esempio dump (contiene dati sensibili):

```bash
umask 077
docker compose exec -T db pg_dump -U omnidesk -d omnidesk -Fc > omnidesk-backup.dump
```

Confermata la compatibilità e salvati i backup: fermare API durante la migrazione, costruire le immagini, eseguire `docker compose -p omnidesk-production -f compose.yaml -f compose.direct.yaml run --rm migrate`, avviare API/dashboard/Caddy. Non cancellare volumi per aggiornare. Rollback schema non automatizzato. Il deployment pubblico usa `omnidesk` per migrazioni e `omnidesk_runtime` per l'API. Le chiavi DataProtection sono protette dai permessi del volume, non cifrate a riposo: usare storage cifrato e proteggere backup.

Dettagli milestone: [M2 agente](docs/milestone-2.md), [M3 remoto](docs/milestone-3.md), [M4 aggiornamenti](docs/milestone-4.md), [M5 console/2FA](docs/milestone-5.md).

Documenti: [architettura](docs/architecture.md), [database](docs/database.md), [API](docs/api.md), [roadmap](docs/roadmap.md), [verifiche e limiti](docs/validation.md). Regole di sviluppo in [AGENTS.md](AGENTS.md).

OmniDesk Service Desk è parzialmente implementato nel core: intake Agent/Tray, ticket e conversazione IT, categorie, code, assegnazioni e note interne con permesso dedicato. Portale con identità personale, allegati generici/quarantena, notifiche, email, KB e SLA restano da sviluppare: [specifiche e stato](docs/service-desk.md), [schema proposto](docs/service-desk-data-model.md), [roadmap](docs/roadmap.md#percorso-futuro-omnidesk-service-desk).

Kit Windows 0.8.0: [OmniDesk Agent e OmniDesk Tray](docs/windows-agent-tray.md), installati con Setup grafico e MSI unico; la disinstallazione MSI richiede la revoca dell’identità sul server. Il pacchetto è compilato, ma installazione, enrollment e rimozione richiedono ancora il collaudo sul PC Windows pilota. Non è firmato per la distribuzione pubblica.
