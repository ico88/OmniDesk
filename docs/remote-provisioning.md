# Provisioning remoto 0.7.0

Incremento in sviluppo, risultati effettivi in validation.md. Il collaudo interattivo concordato usa Windows 10 su 192.168.1.160. Non derivare desktop funzionante dalla sola compilazione.

L'MSI unico contiene Agent, Tray e motore vendor MeshService64.exe, proveniente da meshcentral 1.2.5 nel lockfile npm, con SHA256 fissato nel contratto e nella build. Il motore e un'integrazione distinta, servizio SYSTEM OmniDeskRemote demand-start. Non parte senza configurazione approvata. Il servizio OmniDeskAgent rimane automatico e la Tray non elevata. Nessuna chiave, configurazione dispositivo o token entra nel MSI.

Un amministratore globale approva dalla console il provisioning per un dispositivo gia registrato. Il server crea un gruppo MeshCentral dedicato con richieste di consenso e notifiche per desktop, terminale e file. L'account motore possiede solo i gruppi creati, senza essere site administrator; registrazione utenti e download pubblico degli agenti sono disabilitati. Ogni retry recupera il gruppo dedicato e riapplica il consenso. Settings e MeshID sono conservati protetti con ASP.NET Data Protection. La risposta all'operatore non contiene settings o chiavi di gruppo.

Il servizio Windows recupera configurazione tramite HTTPS con la propria identita firmata anti replay, verifica settings allowlist, hash e permessi del binario, scrive la MSH nella directory protetta e avvia il motore. La directory Remote ammette SYSTEM/Administrators; solo l'exe ha lettura/esecuzione per utenti standard, per consentire i processi interattivi del vendor. File MSH/DB nuovi ereditano ACL private. Il comando -nodeid e fisso, non un comando passato dalla rete. Il server accetta il mapping solo dopo aver verificato contro il motore reale che il nodo sia online e l'unico nel gruppo approvato. NodeId di un altro dispositivo o sostituzioni conflittuali sono rifiutati.

Il servizio richiede rinnovi periodici: usa un intervallo monotono locale limitato a sessanta secondi e controlla ogni quindici secondi, con timeout di rete. Disabilitazione/revoca fa cessare il rinnovo e richiede stop; non e una garanzia temporale assoluta contro blocchi SCM, amministratori o problemi del sistema. Chiudere o disabilitare il servizio Agent richiede anche lo stop del motore nel percorso ordinario di shutdown. I riavvii del motore sono gestiti da recovery; il rinnovo resta necessario. Configurazione non equivale ad autorizzazione di sessione: ogni link richiede ancora capacita corrente e ambito dispositivo, scade dopo cinque minuti e forza consenso sul client. Nessun unattended automatico, debugConsole o shell generica via IPC.

Il consenso vendor disabilita autoaccept su timeout/assenza utente/blocco. Le sessioni esistenti vengono accodate per revoca se il provisioning viene disabilitato o l'identita revocata. Il guard verifica anche questo stato. La revoca di un link deve essere collaudata su tunnel attivi; non anticiparne l'esito.

## Server LAN

L'overlay compose.mesh-lan.yaml si combina con compose.yaml e compose.mesh.yaml. Usa il solo indirizzo LAN 192.168.1.233:8086 per il motore; la console resta su 8085. La CA pubblica esistente e importata nei trust store dei client backend tramite file; nessuna disabilitazione della verifica TLS. Non modifica firewall o servizi host. Il motore mantiene un advisory node-forge senza fix verificato: il rilascio pubblico resta bloccato, il percorso LAN e laboratorio autorizzato.

La migrazione dall'entrypoint precedente che promuoveva l'account a site admin richiede intervento esplicito; il nuovo entrypoint rifiuta quel marker anziche riutilizzare silenziosamente un account privilegiato.

I grant temporanei durano trenta minuti. La console desktop distingue la revoca
del link dalla disconnessione degli stream attivi. Quest'ultima usa l'azione
MeshCentral `endDesktopMultiplex` per il link associato al grant, risolvendo
lato server identità del proprietario e guest name dal record share; il browser
non invia tali identificatori. Il grant rimane riutilizzabile fino a scadenza.
«Termina» revoca il record share e impedisce il riuso. La disconnessione si basa
sul protocollo di controllo della versione MeshCentral installata, non su un
endpoint REST pubblico versionato: gli upgrade MeshCentral richiedono nuovi
test e revisione di questo comando.

La revoca del dispositivo o la disabilitazione del provisioning avvia anche la rimozione durevole del gruppo MeshCentral dedicato, con retry. Disabilitazione DB e rimozione del motore hanno commit distinti; un errore del motore non riabilita il dispositivo. Un lock dispositivo impedisce che una riapprovazione venga cancellata da una rimozione precedente. Il backup locale ora include dati/file Mesh e password servizio quando il motore e presente; la copia del database Mesh a caldo non e attestata come restore consistente e mantiene il gate di restore completo.

### Permessi CA pubblica nel container API

Il file `omnidesk-local-ca.crt` montato come config Compose deve essere leggibile
anche dall'utente API (UID 1654). Con file config locali Compose puo mantenere i
permessi del file sorgente invece del mode dichiarato. Per questa sola CA
pubblica, dopo verifica del contenuto/fingerprint, usare `chmod 644
omnidesk-local-ca.crt` e verificare `docker compose exec -T api test -r
/run/omnidesk-ca.crt`. Non applicare questa regola alle chiavi private o ai
segreti. Dopo una correzione del trust riavviare il container API per ricaricare
lo store TLS. La sonda host da sola non verifica permessi e trust dell'API.
