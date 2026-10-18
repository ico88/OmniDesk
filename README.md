# OmniDesk

Piattaforma RMM self-hosted per Windows. **Milestone 1: piano di gestione iniziale**, non ancora un RMM operativo per endpoint.

Implementati: API ASP.NET Core .NET 10, PostgreSQL con migrazioni, Identity multiutente, CSRF e lockout, ruoli personalizzati e permessi globali/gruppo/device, audit, dashboard React/TypeScript con nome/logo/colore/tema persistenti, Docker Compose e Caddy HTTPS. Il parco macchine è popolato solo da enrollment reali.

M2 aggiunge enrollment monouso, agente Windows .NET cross-compilato, snapshot metriche/inventario, gruppi e dashboard dispositivi. Sorgenti MSI e pipeline Windows presenti; MSI, servizio e raccolta reale su Windows ancora da collaudare. Progettati/in corso: MeshCentral, Windows Update, console Windows e attivazione TOTP. Nessun controllo remoto è disponibile in questa versione.

## Installazione Ubuntu Server

La subnet proxy predefinita è `172.29.84.0/24`: modificarla tramite `OMNIDESK_PROXY_SUBNET` in `.env` se confligge con reti esistenti. Non collegare container estranei a questa rete. Servono DNS pubblico verso il server, porte TCP 80/443 raggiungibili dall'esterno, Docker Engine + Compose e Python 3. Non installare su un host con altri servizi sulle stesse porte senza pianificare il proxy.

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

Aprire `https://<dominio>`. Controllare:

```bash
docker compose ps
docker compose logs --tail=100 api caddy
curl --fail https://<dominio>/api/health/ready
```

Il successo di setup non certifica DNS/TLS: verificare l'endpoint HTTPS. Le credenziali non sono recuperabili dal repository. Cambio/reset password e gestione completa del ciclo di vita account sono ancora da implementare; M1 è destinata a collaudo controllato.

## Test e sviluppo

Non sono richiesti SDK host se si usa Docker:

```bash
./scripts/test.sh
docker build -t omnidesk-api:local -f src/OmniDesk.Api/Dockerfile .
docker build -t omnidesk-dashboard:local -f src/dashboard/Dockerfile .
./scripts/smoke.sh
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

Protocollo agente e istruzioni: [M2](docs/milestone-2.md).

Documenti: [architettura](docs/architecture.md), [database](docs/database.md), [API](docs/api.md), [roadmap](docs/roadmap.md), [verifiche e limiti](docs/validation.md). Regole di sviluppo in [AGENTS.md](AGENTS.md).
