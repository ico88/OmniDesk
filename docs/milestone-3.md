# M3 — MeshCentral

Motore fissato al pacchetto ufficiale meshcentral 1.2.5, immagine costruita nel repository. Adapter .NET WebSocket con convalida certificato attiva; credenziali del servizio solo da secret file, mai al browser. Protocollo verificato contro sorgenti meshctrl.js/meshuser.js del pacchetto: createDeviceShareLink, removeDeviceShare, x-meshauth.

I link hanno durata di trenta minuti (verificata nel codice corrente e nel log runtime pubblicato), un protocollo per sessione (`desktop=2`, `terminal=1`, `files=4`) e consenso locale salvo policy unattended approvata per-device. OmniDesk verifica permesso/capability e ambito dispositivo, registra l'audit prima di restituire l'URL e non salva URL bearer nel DB. Il mapping dispositivo–nodo è amministrativo. I link sono bearer trasferibili: trattarli come credenziali e non copiarli in ticket/log. Il guard server rivalida permessi, ambito, account/dispositivo e scadenza, accoda `revocation_pending` e ritenta la revoca del motore; non si promette latenza massima in caso di errore. La prova PC reale di scadenza, rinnovo, revoca durante stream, unattended e multi-sessione resta gate di produzione.

L'overlay `compose.mesh.yaml` è opt-in: secondo dominio pubblico `MESH_DOMAIN`, file `.secrets/mesh_password` casuale in directory 0700 e file 0444. Account di servizio privilegiato necessario al provisioning, da restringere ai gruppi gestiti dopo collaudo. Il container crea l'account una volta e disabilita registrazione pubblica. Non cambiare il secret senza ruotare la password MeshCentral. Caddy termina TLS; upstream HTTP sulla rete proxy dedicata. Backup mesh_data e mesh_files insieme a PostgreSQL.

```bash
docker compose -f compose.yaml -f compose.mesh.yaml build meshcentral
docker compose -f compose.yaml -f compose.mesh.yaml up -d
```

Questi comandi espongono il dominio remoto: eseguirli solo su un server configurato e autorizzato. La generazione secret e setup opzionale sono descritti nel modulo. Non dichiarare M3 pronta: provisioning Mesh agent nel singolo MSI, mapping automatico, connettività Windows/NAT, desktop/terminale/file e revoca tunnel richiedono collaudo reale. Adapter e test con doppio controllato non equivalgono al controllo remoto operativo.

Audit dipendenze: override espliciti compression 1.8.2 e qs 6.16.1 correggono gli avvisi per quei pacchetti. Rimane node-forge (upstream 1.4.0, advisory GHSA-86w9-cpqp-85rv senza versione corretta indicata nell'audit): blocco release del motore. Non applicato npm audit fix --force, che proponeva un downgrade MeshCentral. Nessun motore remoto viene avviato dalla configurazione standard.

API M3: PUT /api/admin/devices/{id}/mesh {nodeId}, POST /api/devices/{id}/remote-sessions {capability}, GET stesso percorso senza URL bearer, DELETE /api/devices/{id}/remote-sessions/{sessionId}. Creazione richiede esattamente remote.control, terminal o files nel corretto ambito. Solo creatore o amministratore globale può revocare.

Ogni sessione conserva il nodeId originale: cambiare mapping non sposta il bersaglio della revoca (test di regressione).
