# OmniDesk Service Desk — specifica e stato

**Stato: implementazione parziale nel core API/dashboard.** Sono disponibili ticket da Agent/Tray e console IT, categorie, code con membership/routing, conversazione pubblica e note interne autorizzate. L’inbox correlata RMM permette di associare al ticket sessioni remote e job già registrati, solo sui dispositivi collegati e con permessi sia sul ticket/dispositivo sia sulla capability dell’artefatto; la cronologia è append-only e non espone output diagnostici. L’associazione non avvia né autorizza una nuova azione RMM. È implementato nel ramo il portale: un amministratore associa l’account verificato ai dispositivi, l’utente apre/consulta solo i propri ticket, risponde nella conversazione pubblica e allega screenshot PNG dopo consenso esplicito. La relazione portale è distinta dai grant RMM; ogni lettura richiede account attivo, associazione attiva e dispositivo ancora valido. La KB interna supporta bozze, revisioni, lettura globale/sede/gruppo e ricerca autorizzata; la gestione resta globale. Test PostgreSQL, build dashboard e smoke browser sono registrati in [validation.md](validation.md). Portale e incremento RMM non sono distribuiti sulla LAN; restano da fare allegati generici con quarantena, email, workflow ticket configurabile, riepiloghi intervento strutturati, allarmi persistenti, SLA e automazioni. La KB non supporta ancora Markdown/HTML né allegati. Questo percorso non chiude i gate M1–M5 o produzione.

## Confini e dipendenze

Modulo backend ASP.NET Core separato, futuro `OmniDesk.ServiceDesk`, con livelli Domain, Application, Infrastructure ed Endpoints. Inizialmente è caricato dallo stesso host API: non richiede un nuovo servizio pubblico. Possiede il proprio DbContext e le proprie migrazioni nello schema PostgreSQL `service_desk`. Riusa Identity, autorizzazione e audit tramite interfacce; non replica utenti, inventario o credenziali. La dashboard React/TypeScript avrà un'area Service Desk e un portale utenti con policy e DTO distinti. Nessuna nuova porta o modifica Nginx è prevista.

Il RMM attuale fornisce dispositivi, gruppi di autorizzazione, identità, snapshot di monitoraggio, sessioni remote e job aggiornamenti. Il registro sedi e le concessioni site-scoped sono implementati; il motore di allarmi persistenti non lo è. Una sede rappresenta un luogo operativo; non coincide con un gruppo di autorizzazione. Il prodotto resta single-tenant: sedi non costituiscono tenant isolati.

Le dipendenze RMM sono porte applicative: catalogo dispositivi/sedi, autorizzazione, monitoraggio/allarmi, sessioni remote, job aggiornamenti e audit. Il modulo non accede direttamente a credenziali MeshCentral o dispositivo. Le integrazioni asincrone usano eventi versionati con outbox transazionale e inbox deduplicata. Worker ASP.NET Core per notifiche, email, timer ed escalation usano lease persistenti, retry limitati e coda degli errori; un broker esterno non è necessario per la prima versione. Occorre definire prima atomicità degli eventi fra DbContext: transazione PostgreSQL condivisa dove disponibile, altrimenti consegna almeno una volta e riconciliazione; nessuna promessa di consegna exactly-once.

## Ticket e ingressi

Ogni ticket effettivo appartiene a **una sede e almeno un dispositivo**. Tutti i dispositivi collegati devono appartenere alla sede al momento del collegamento. Spostamenti successivi preservano sede e descrizioni storiche; ricollegamenti o cambi sede sono espliciti, autorizzati e registrati. Nessuna azione RMM usa i dati storici per decidere i permessi correnti.

Stati core attuali: aperto, in lavorazione, risolto e chiuso. Il workflow predefinito consente `open → in_progress/resolved`, `in_progress → open/resolved`, `resolved → open/in_progress/closed`; `closed` è terminale. Ogni transizione aggiorna stato, versione, conversazione pubblica e outbox nella stessa transazione. Entrare in `resolved` richiede una risposta pubblica; chiudere un ticket già risolto riusa quella risposta. Riaprire azzera il campo risoluzione corrente, mantenendo la risposta precedente nella cronologia. L'API esprime gli stati ammessi e rifiuta quelli non validi con conflitto anche fuori dalla UI. Workflow configurabili/versionati, attesa richiedente/esterna, motivo di risoluzione e priorità P1–P4 restano proposti. Le categorie core, le priorità low/normal/high/urgent, code/team, assegnatari e chat sono implementati. Le note interne usano una capability separata `tickets.note.internal`, sono escluse dalle proiezioni macchina e aggiornano il versionamento del ticket. Il collegamento multi-dispositivo è implementato nella console IT: sede corrente deve coincidere con quella del ticket al collegamento, con autorizzazione `tickets.manage` su ticket e dispositivo, snapshot storico, limite dieci, versione attesa e audit. Partecipanti e configurazione workflow restano da sviluppare. Cronologia append-only e versioni attese proteggono dagli aggiornamenti persi; l'intake macchina è idempotente per dispositivo/richiedente/RequestId.

| Ingresso | Verifica e comportamento proposti |
| --- | --- |
| Console IT/manuale | Utente autenticato, permessi di creazione e collegamento sui dispositivi/sede scelti |
| Portale web | Account verificato; selezione dei soli dispositivi assegnati e autorizzati al richiedente |
| Agente Windows | Identità macchina esistente, firma/replay protection e rate limit; può proporre richieste solo per sé. Un contatto dichiarato non diventa un'identità umana verificata |
| Email | Mailbox autorizzata e associazione verificata account/thread; From o SPF non bastano per autorizzare. Token di risposta hashati, limitati a ticket/destinatario/scadenza, deduplicazione Message-ID e protezione loop |
| Allarme | Evento persistente autenticato dal motore futuro, regola versionata, correlazione e deduplicazione per occorrenza |

Richieste senza sede, dispositivo o richiedente verificabile sono **intake in attesa di qualificazione**, non ticket incompleti. La promozione è atomica e autorizzata. Si conserva l'istante originale di ricezione: SLA e timer di triage non possono essere aggirati ritardando la promozione. La policy dovrà precisare decorrenza e trattamento degli intake non qualificati.

## Esperienza, comunicazioni e storico

La scheda mostra dispositivi autorizzati, sede, stato di monitoraggio, allarmi correlati e interventi. I controlli remoti restano nella scheda dispositivo e richiamano i servizi RMM con verifica della singola capability sul device selezionato. Nel ticket un operatore può selezionare e associare sessioni/job già esistenti, purché abbia `tickets.manage` e la capability sul dispositivo collegato; la lettura richiede `tickets.view` e la stessa capability. Il record mostra identificativo breve, dispositivo, tipo, stato e orario, senza payload o risultati. Per le sessioni remote l'ora terminale appare solo dopo conferma della revoca dell'engine; la durata deriva dai timestamp persistiti. Nessuna sessione o azione parte aprendo il ticket. Il collaudo remoto reale e i gate Windows delle milestone esistenti restano obbligatori.

Commenti pubblici e note interne hanno visibilità esplicita. Il portale, le email e la ricerca pubblica non ricevono note interne, contatori o allegati riservati. La pubblicazione di una nota interna richiede un'operazione separata e auditata, mai una regola automatica. Notifiche con template separati, preferenze, retry e deduplicazione rispettano i permessi al momento dell'invio.

Allegati: metadati PostgreSQL e contenuto in volume privato iniziale; storage S3 compatibile solo come adapter futuro. Quote e limiti, nomi storage opachi, verifica MIME/hash, quarantena e scansione antivirus prima della distribuzione; autorizzazione a ogni download, Content-Disposition attachment e nosniff. Visibilità ereditata dal commento, nessun percorso pubblico prevedibile. HTML/email sanitizzati; nessun download automatico di URL esterni. Provider antivirus, retention e backup coerente DB/blob sono decisioni preliminari, non servizi già disponibili.

Il portale personale iniziale usa una relazione account-dispositivo concessa e revocata da `administration`; le API non interpretano i permessi RMM come identità del richiedente. I DTO sono dedicati, i ticket sono filtrati per `RequesterUserId`, le note interne non sono esposte e le mutazioni richiedono antiforgery. La conversazione ha versionamento atteso e limiti di frequenza. Il portale supporta screenshot PNG con consenso; allegati generici, quarantena e antivirus restano da sviluppare. Lo smoke browser autenticato verifica i flussi del portale e la selezione di un job RMM nella conversazione interna. L'Agent/Tray continua a consentire intake e conversazione macchina. La Knowledge Base interna implementa bozze, pubblicazione, versioni consultabili e lettura globale o per sede/gruppo; la gestione contenuti è globale. Categorie avanzate, allegati e proposte di articoli da ticket risolti restano pianificati e non pubblicano contenuti automaticamente. La correlazione RMM memorizza riferimenti verificati e snapshot di stato senza affermare che un’operazione sia riuscita oltre lo stato registrato dal servizio.

## Permessi e audit

Nuove capacità proposte: `tickets.read/create/update/assign/transition/link_devices`, `tickets.comment.public`, `tickets.note.internal`, `tickets.attachments.upload/download`, `tickets.sla.manage`, `tickets.automation.manage`, `tickets.audit.read`, `kb.read/edit/publish`; portale `requests.create/read_own/comment_own`. Le assegnazioni combinaranno ambito globale/sede/gruppo/dispositivo e relazione richiedente/team, con default deny. Un ruolo tecnico esistente non riceve implicitamente nuove capacità; migrazione e matrice dei ruoli devono essere approvate nel progetto esecutivo.

La lettura del ticket non concede `view`, `remote.control`, `terminal`, `files`, `updates` o `administration`. Creazione e collegamento richiedono autorizzazione su tutti i dispositivi selezionati. Le risposte redigono i dati RMM non autorizzati; ogni azione verifica nuovamente ticket e dispositivo corrente. Gli utenti finali non possono modificare assegnazioni, SLA o note interne. Filtri applicati prima di paginazione, conteggi, ricerca, export e notifiche evitano canali laterali.

Audit comune con actor umano/macchina/sistema, origine, motivo, timestamp UTC, ticket/device/site/session/job e correlation ID: creazione, transizioni, assegnazioni, allegati, cambi permesso, policy, escalation, automazioni e tentativi negati. Cronologia ticket e audit di sicurezza restano distinti. Retention, minimizzazione dati personali e cancellazione controllata devono preservare i vincoli storici senza conservare segreti.

## SLA e automazioni

SLA versionati per primo riscontro e risoluzione, calendari lavorativi, festività, fuso IANA e gestione ora legale. Ogni ticket conserva la versione applicata; nuove policy non riscrivono silenziosamente scadenze precedenti. Pause solo in stati ammessi, intervalli persistenti, comportamento di riapertura esplicito. Scadenze UTC ricalcolabili dai dati persistiti, escalation idempotenti per ticket/regola/fase e recupero dopo riavvio; clock iniettabile per test.

Regole evento/condizioni/azioni con versioni, simulazione, limiti e audit. Azioni iniziali ammesse: assegnare, categorizzare, impostare priorità, notificare, escalare e proporre intake. Correlazione, cooldown e limite alla profondità evitano loop di allarmi/email. Recovery allarme aggiunge evidenza; chiusura automatica solo con policy esplicitamente configurata e verificata.

Le regole non eseguono shell, avviano sessioni o installano aggiornamenti. Una futura azione RMM mutante deve passare dal servizio competente, con approvazione umana specifica e ricontrollo permessi; non può usare un'identità sistema per ampliare i privilegi del richiedente.

## Predisposizione AI

Porta futura per riepiloghi, classificazione, bozze di risposta e ricerca KB. Nessun provider, modello o chiave viene configurato ora. Funzioni opt-in, budget e retention configurabili; solo dati consentiti all'utente, minimizzati e senza credenziali, con ambiti KB rispettati.

Email, ticket e allegati sono input non fidati, anche quando contengono istruzioni rivolte al modello. L'assistente produce proposte e non accede a shell, token o adapter RMM. Eventuali approvazioni future vincolano attore, dispositivo, azione, hash dei parametri e scadenza; autorizzazione server ricontrollata all'esecuzione, nessuna approvazione generica riutilizzabile. Audit registra provenienza/versione e decisione umana evitando copie indiscriminate dei dati sensibili.

## Superficie API proposta, non disponibile

- `/api/service-desk/tickets`: ricerca paginata, dettaglio, creazione, transizioni, assegnazioni, collegamenti, commenti, allegati e interventi; mutazioni cookie protette da CSRF, ETag/If-Match e idempotenza dove necessario.
- `/api/service-desk/policies`, `/automations`, `/knowledge`: amministrazione autorizzata e versionata; nessun endpoint per script arbitrari.
- `/api/portal/requests` e `/api/portal/knowledge`: DTO dedicati al richiedente, mai serializzazione diretta delle entità interne.
- `/api/agent/service-desk/intakes`: identità macchina separata, nessuna impersonificazione della sessione browser. Connettore email come worker; eventuali webhook futuri richiedono firma e replay protection proprie.
- Le azioni RMM restano negli endpoint RMM esistenti; il contesto ticket è correlazione audit, non una nuova autorizzazione.

Schema dettagliato: [modello dati proposto](service-desk-data-model.md). Ordine e dipendenze: [roadmap](roadmap.md#percorso-futuro-omnidesk-service-desk).

## Criteri di verifica futuri

Non sono test eseguiti: isolamento sede/richiedente/team; ticket visibile senza privilegi RMM; nessuna fuga di note interne tramite ricerca/export/email/allegati; vincolo almeno un dispositivo e promozione intake concorrente; conflitti ETag; deduplicazione email/allarmi; replay agente; quarantena/download; calendari DST, pause, riaperture ed escalation una sola volta dopo crash; audit senza bearer; backup/restore DB/blob; prompt injection incapace di attivare comandi. A questi si aggiungono test API PostgreSQL, frontend, browser portale e integrazioni reali dei connettori prima di dichiararli operativi.

## Ingresso Windows tramite Tray — estensione proposta

La funzione utente “Richiedi assistenza” appartiene a **OmniDesk Tray**, processo non elevato distinto dal servizio Agent. La Tray comunica su named pipe locale con ACL per logon e validazione; il servizio associa la richiesta al proprio dispositivo, il server verifica sede e identità umana. Su computer condivisi la chiave macchina non permette di leggere ticket di altri utenti. Screenshot acquisiti dalla Tray soltanto dopo scelta, anteprima e consenso esplicito, poi sottoposti alle policy allegati. Nessuna operazione IPC consente arresto servizio o comandi arbitrari.

Il futuro MSI unico installerà entrambi i componenti; il MSI attuale contiene solo Agent. Vedi [specifica Windows e consenso remoto](windows-agent-tray.md) e [dipendenze WT/SD](roadmap.md#percorso-futuro-windows-agent-e-tray). Questi flussi sono progettati, non implementati.
