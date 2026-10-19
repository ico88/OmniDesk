# Accesso LAN 192.168.1.233:8085 e Nginx esistente

La configurazione predefinita pubblica **solo 192.168.1.233:8085**, verso HTTPS Caddy nel container. Nessuna porta host 80/443 e nessuna modifica ai servizi Nginx/firewall. Il server deve realmente possedere quell'indirizzo. Variabili `.env`:

```
OMNIDESK_LAN_IP=192.168.1.233
OMNIDESK_BIND_ADDRESS=192.168.1.233
OMNIDESK_HTTPS_PORT=8085
OMNIDESK_DOMAIN=rmm.example.com
```

Se `.env` è stato creato prima di questa modifica, aggiungere queste variabili. Visitare `https://192.168.1.233:8085`, non HTTP: i cookie di sessione e antiforgery restano Secure. Caddy emette certificati tramite CA interna per l'IP LAN e il dominio. Let's Encrypt non viene usato per l'IP privato.

Dopo l'avvio, esportare **solo il certificato pubblico root**, mai la chiave privata:

```bash
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt ./omnidesk-local-ca.crt
```

Importare root.crt tra le autorità attendibili sui client autorizzati. Il certificato root permette a Caddy di emettere certificati attendibili per quei client: distribuire solo a dispositivi gestiti e proteggere il volume Caddy contenente la chiave CA. Non disabilitare la validazione TLS nell'agente o nel browser. Backup del volume conserva la CA tra riavvii; cancellare il volume cambierebbe la CA.

Per accesso pubblico, Nginx esistente continua a terminare HTTPS sul dominio con il suo certificato pubblico. Preparato `deploy/nginx-omnidesk.conf.example`: proxy HTTPS verso 192.168.1.233:8085, verifica certificato Caddy attiva tramite root.crt, nome TLS impostato al dominio. Sostituire dominio e percorsi, integrare nel vhost corretto, poi testare `nginx -t` e ricaricare **solo con autorizzazione a modificare il servizio**. Nessuna di queste modifiche è stata eseguita automaticamente. Certificati pubblici restano gestiti dall'installazione Nginx esistente.

MeshCentral opzionale richiede un secondo vhost Nginx per MESH_DOMAIN con stesso upstream/CA e header Upgrade/Connection per WebSocket; il motore resta bloccato per release dagli avvisi audit documentati in M3. L'overlay diretto `compose.direct.yaml` ripristina Caddy pubblico 80/443 con Let's Encrypt solo su server dedicati senza conflitto Nginx; non usarlo nel deployment LAN richiesto.

Avvio LAN verificato sul server con indirizzo 192.168.1.233: container API/dashboard/Caddy attivi e PostgreSQL healthy; HTTPS, readiness e login amministratore verificati dal server con CA esplicita. Raggiungibilità da un altro client LAN ancora da verificare. Non sono state aperte porte firewall né modificati i servizi esistenti su 80/443.

Il Caddyfile imposta `default_sni` all'IP LAN per i client che collegandosi tramite IP non inviano SNI; riferimento: [opzioni Caddy](https://caddyserver.com/docs/caddyfile/options#default-sni). La verifica del certificato rimane obbligatoria.

Configurazione locale iniziale: dominio interno `omnidesk.internal`, account `admin@omnidesk.local`, TOTP disattivato. La password casuale iniziale è nel file locale `.secrets/bootstrap_password`, escluso da Git e protetto dalla directory `.secrets`; consultarlo sul server, cambiare password dalla dashboard e rimuoverlo dopo il trasferimento sicuro. Il certificato pubblico da distribuire ai client è `omnidesk-local-ca.crt` nella root del progetto, escluso da Git. Nessuna chiave privata è stata esportata.

Gestione accessi: dalla schermata **Utenti** l'amministratore può consultare account/ruoli, creare gruppi e assegnare o revocare concessioni per gruppo/dispositivo. I gruppi creati sono selezionabili nella schermata Dispositivi per il token enrollment successivo. I ruoli globali e le altre concessioni restano attivi quando si revoca una singola concessione. Dopo un aggiornamento della dashboard ricaricare la pagina per acquisire il nuovo bundle.

Da **Gestisci account** si possono sospendere/riattivare account, sostituire ruoli globali e revocare sessioni web. Sospensione propria e rimozione dell'ultimo amministratore abilitato sono vietate. Dopo cambi ruoli, revoca o riattivazione il destinatario deve autenticarsi nuovamente; i vecchi cookie non vengono ripristinati. Backup e prova restore disponibili in [backup-restore.md](backup-restore.md).

Per Agent Windows usare la CA nello store LocalMachine Root, non solo nello store utente. Diagnostica e installazione 0.5.2: [windows-lan-install.md](windows-lan-install.md). Nessun bypass TLS o cambio firewall introdotto; il nuovo MSI richiede ancora build/collaudo Windows.

Gestione dispositivo: aprire la scheda da **Dispositivi → Apri**; un amministratore globale può salvare etichetta/gruppo o revocare l'identità. Il cambio gruppo può togliere visibilità e capacità agli utenti del gruppo precedente, preservando concessioni dirette; conferma obbligatoria prima dello spostamento e della revoca. Revocare non disinstalla l'Agent e non interrompe lavori già avviati: blocca nuove comunicazioni, annulla queued e richiede chiusura sessioni remote. Conflitto 409: ricaricare la scheda prima di salvare nuovamente.
