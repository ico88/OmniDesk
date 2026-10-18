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

Verifiche su host di sviluppo: configurazioni Compose/Caddy statiche. Indirizzo 192.168.1.233 non assunto presente sul nostro host; raggiungibilità LAN e Nginx devono essere verificati sul server reale. Non sono state aperte porte firewall.
