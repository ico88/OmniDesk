# Ticket e screenshot — incremento 0.6.0

Il modulo implementa richieste da Windows Tray, elenco degli ultimi dieci ticket del richiedente e gestione IT degli ultimi cento ticket autorizzati. È un primo incremento distinto dalle future fasi SD per sedi, email, SLA, collaborazione e knowledge base.

## Identità e autorizzazione

La Tray non riceve credenziali macchina. Si connette alla pipe locale verificando processo/servizio/proprietario SYSTEM; il servizio verifica SID utente e SID di logon del client con impersonazione e WTS. Il richiedente è una chiave SHA256 derivata dal dispositivo e dal SID utente Windows ottenuto dalla pipe, mai da un SID dichiarato nel messaggio. Questa identità locale non è un account Identity o una prova dell'identità civile dell'utente. Gli amministratori locali restano entro il confine di fiducia della macchina.

Il servizio invia ogni richiesta HTTPS con una nuova assertion dispositivo firmata. Il server deriva sempre DeviceId dalla credenziale autenticata e applica revoca e anti replay esistenti. La lista macchina filtra dispositivo e richiedente; include oggetto, stato e risposta ma non il testo originale o l'immagine. La lista è limitata a dieci richieste per contenere il messaggio IPC. Le risposte di risoluzione sono visibili al richiedente: non inserirvi note interne.

La console usa Identity, CSRF sulle mutazioni e capacità separate `tickets.view` e `tickets.manage`, globali/gruppo/dispositivo. Administrator e Operator ricevono queste capacità tramite migrazione; Viewer no. Un permesso ticket non concede desktop, terminale o file. Gli aggiornamenti usano una versione attesa; conflitti restituiscono 409. Gli stati sono open, in_progress, resolved e closed; gli ultimi due richiedono una risposta. Nessuna notifica o azione remota automatica.

## Screenshot

La Tray chiede conferma prima di catturare lo schermo principale della sessione corrente, mostra anteprima e richiede una seconda scelta esplicita per allegarlo. È possibile rimuovere l'immagine e inviare solo testo. Nessuna acquisizione nel servizio, periodica o su altre sessioni. Ridimensionamento massimo 1280×720, formato PNG RGBA8, massimo 512 KiB. Metadati PNG rimossi dalla Tray. Il server valida firma, dimensioni, sequenza di chunk ammessi, CRC e stream zlib con limite esatto di pixel; rifiuta file generici, animazioni e metadati. Il download richiede tickets.view sul dispositivo, viene auditato ed è un attachment con nosniff/no-store.

La richiesta IPC può raggiungere 750000 byte, con timeout, singola connessione e ACL/logon già esistenti; i messaggi di stato restano limitati a 4096 byte lato client. L'intake HTTP ha limite 750000 byte; al massimo dieci nuovi ticket per dispositivo in cinque minuti, sotto lock PostgreSQL condiviso con la revoca. RequestId e vincolo univoco rendono i retry idempotenti; riuso con contenuto diverso restituisce 409. La Tray conserva il RequestId per retry dello stesso contenuto finché la finestra rimane aperta; chiuderla dopo un timeout richiede verificare l'elenco prima di reinviare.

Ticket e immagini sono conservati nel database PostgreSQL e inclusi nei backup. Non esiste ancora una retention automatica o una cancellazione self-service: definire la policy prima della produzione. Questo percorso accetta solo screenshot PNG; non sostituisce la quarantena/antivirus prevista per futuri allegati generici. Nessun servizio antivirus esterno collegato.

## Contratti

- Macchina: GET `/api/agent/support/{requesterKey}`, POST `/api/agent/support`.
- Operatori: GET `/api/support`, PUT `/api/support/{id}`, GET `/api/support/{id}/screenshot`.
- Migrazione: `SupportTickets`; tabella propria con FK Device, indice cronologico, vincolo device/requester/requestId e seed capacità.
- Pipe: GetConnectionStatus, CreateTicket e ListTickets. Nessun comando stop/start/uninstall o shell.

La build e i test server non attestano cattura schermo o IPC su Windows reale. Vedere validation.md per risultati effettivi e gate ancora aperti.

## Collaudo e distribuzione

Server pubblicato su LAN dopo backup e migrazione. Scenario PostgreSQL e smoke Chromium verificano realmente l'intake tramite credenziale RSA, autorizzazione, download e risoluzione ticket; i ticket fixture sono creati solo in ambienti isolati. Agent/Tray compilati e MSI generato su GitHub Actions Windows. Questi risultati non attestano ancora le interazioni reali Tray/screenshot sul PC dell'utente: installazione/enrollment e sessione interattiva vanno collaudati sul client LAN.
