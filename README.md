# OmniDesk

Piattaforma RMM self-hosted per Windows. Implementazione iniziale M1–M5, destinata a collaudo controllato; i gate di produzione e le verifiche Windows mancanti sono riportati in [validation.md](docs/validation.md).

Disponibili: API ASP.NET Core .NET 10, PostgreSQL con migrazioni, Identity multiutente, CSRF/lockout, ruoli granulari globali/gruppo/device e audit, dashboard React con branding e layout personali, enrollment monouso, agente Windows .NET con snapshot metriche/inventario, coda Windows Update e console Windows WebView2. TOTP con recovery code implementata e cifrata nel DB, **disattivata per default**. Cambio password dalla console.

La schermata Utenti gestisce gruppi/concessioni, sospensione e riattivazione account, ruoli globali e revoca sessioni web. L'ultimo amministratore attivo è protetto; stato account e security stamp verificati a ogni richiesta. Backup locale e prova di ripristino PostgreSQL isolata: [istruzioni e limiti](docs/backup-restore.md).

Dalla scheda Dispositivi l'amministratore può modificare etichetta/gruppo e revocare l'identità con conferma. I job queued vengono rivalidati prima del ritiro: perdita permessi o revoca non autorizzano nuovi comandi. I lavori già avviati sul PC non vengono dichiarati interrotti dalla revoca server.

Adapter MeshCentral con sessioni limitate e audit, motore Docker opzionale: non abilitato dal setup standard. Rimane un blocco audit node-forge e manca il collaudo desktop/terminale/file su Windows/NAT; non dichiarato operativo in produzione. Il singolo MSI attuale installa solo OmniDesk Agent: integrazione Mesh agent e firma restano aperte. MSI unsigned compilato dalla pipeline Windows; installazione/upgrade/uninstall ancora da verificare.

## Installazione Ubuntu Server

La configurazione predefinita usa **https://192.168.1.233:8085**, lasciando libere le porte host 80/443 per Nginx. Servono quell'indirizzo configurato sul server, Docker Compose e Python 3. Per fidarsi del certificato LAN importare la CA locale Caddy; istruzioni e vhost Nginx in [LAN e Nginx](docs/lan-nginx.md). Non usare HTTP: i cookie restano Secure.

La subnet Docker proxy è `172.29.84.0/24`: modificarla tramite `OMNIDESK_PROXY_SUBNET` in `.env` se confligge con reti esistenti. Non collegarvi container estranei.

```bash
./setup.sh                     # prepara .env e secret, senza avviare container
./setup.sh --start             # build, DB, migrazioni, bootstrap e avvio
```

Solo su un server destinato a OmniDesk, con autorizzazione a installare pacchetti e attivare Docker:

```bash
sudo ./setup.sh --install-docker
sudo ./setup.sh --start
```

L'opzione `--install-docker` configura il repository apt ufficiale e abilita il servizio Docker; il normale setup non modifica servizi o firewall. Il daemon Docker può gestire autonomamente regole di rete all'avvio dei container. Non viene aggiunto alcun utente al gruppo docker.

L'amministratore iniziale viene creato solo con DB vuoto. La password casuale è nel file `.secrets/bootstrap_password`: trasferirla tramite un canale riservato, poi rimuovere il file. Nessun segreto viene stampato o salvato in Git. `.secrets` è 0700, file montati 0444 per permettere lettura al processo container non root; proteggere anche i backup. Conservare `.secrets/db_password`: cambiarlo senza ruotare la password PostgreSQL interrompe il servizio.

Aprire `https://192.168.1.233:8085` dopo aver importato la CA, oppure `https://<dominio>` dopo la configurazione autorizzata di Nginx. Controllare:

```bash
docker compose ps
docker compose logs --tail=100 api caddy
curl --cacert omnidesk-local-ca.crt --fail https://192.168.1.233:8085/api/health/ready
```

Il successo di setup non certifica DNS/TLS: verificare l'endpoint HTTPS. Le credenziali non sono recuperabili dal repository. Cambio password disponibile in Account e sicurezza. Reset assistito, disabilitazione account e gestione completa del ciclo di vita restano da completare.

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

Confermata la compatibilità e salvati i backup: fermare API durante la migrazione, costruire le immagini, eseguire `docker compose run --rm migrate`, avviare API/dashboard/Caddy. Non cancellare volumi per aggiornare. Rollback schema non automatizzato. PostgreSQL M1 usa l'utente iniziale del container: separare utente migrazioni e runtime a privilegi minimi prima di produzione. Le chiavi DataProtection sono protette dai permessi del volume, non cifrate a riposo: usare storage cifrato e proteggere backup.

Dettagli milestone: [M2 agente](docs/milestone-2.md), [M3 remoto](docs/milestone-3.md), [M4 aggiornamenti](docs/milestone-4.md), [M5 console/2FA](docs/milestone-5.md).

Documenti: [architettura](docs/architecture.md), [database](docs/database.md), [API](docs/api.md), [roadmap](docs/roadmap.md), [verifiche e limiti](docs/validation.md). Regole di sviluppo in [AGENTS.md](AGENTS.md).

OmniDesk Service Desk è un modulo futuro, **solo progettato**: [specifiche e confini](docs/service-desk.md), [schema PostgreSQL proposto](docs/service-desk-data-model.md), [fasi e dipendenze](docs/roadmap.md#percorso-futuro-omnidesk-service-desk). Non è disponibile nell'interfaccia corrente e non modifica le milestone implementate.

Architettura Windows futura: [OmniDesk Agent e OmniDesk Tray](docs/windows-agent-tray.md), componenti distinti con privilegi separati e un unico MSI previsto. Tray, named pipe e relativi workflow sono solo progettati; il pacchetto corrente installa esclusivamente l'Agent.
