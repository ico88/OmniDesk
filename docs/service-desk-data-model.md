# Service Desk — schema PostgreSQL proposto

**Il Service Desk è incrementale:** ticket, conversazione, code, membership, routing, priorità, assegnazione e Knowledge Base interna esistono nel core OmniDesk; non è ancora stato creato lo schema PostgreSQL separato `service_desk`. Workflow configurabili, partecipanti, allegati generici, SLA, email e automazioni restano proposte per le fasi successive. UUID, timestamp UTC, versioni per concorrenza ottimistica e FK esplicite; JSONB è riservato a snapshot/eventi versionati, non sostituisce relazioni e vincoli.

## Prerequisiti core — stato

| Entità/estensione | Responsabilità |
| --- | --- |
| Sites; Devices.SiteId | **Implementato:** sedi archiviabili, FK dispositivo, enrollment per sede, sede storica sui ticket e backfill dei record esistenti a “Sede da assegnare”. Le sedi restano distinte da DeviceGroups. Migrazione e API verificate su database vuoto; upgrade di un database popolato resta da verificare |
| Concessioni per sede | **Implementato:** `ScopeGrants.SiteId` con vincolo PostgreSQL che ammette un solo ambito; permessi di sede non si trasformano in permessi gruppo/device. Testata visibilità e separazione del permesso `view` |
| AlertOccurrences / AlertEvents | Identità stabile di occorrenza, device, regola/versione, apertura/recupero e correlazione; gli snapshot metriche esistenti non sono allarmi |
| Outbox core | **Parziale implementato:** tabella `CoreOutboxEvents`, scritture transazionali e worker durevole `ticket-notifications.v1`; proietta notifiche ticket in modo idempotente con lock concorrente, retry/backoff e dead-letter. Le notifiche sono persistite in `UserNotifications`. Restano copertura/consumer per tutti gli eventi di dominio e retention |

## Tabelle del modulo

| Entità proposta | Campi e vincoli principali |
| --- | --- |
| Tickets | **Core implementato:** `SupportTickets` contiene numero Identity univoco, device/sede storica, requester macchina e `RequesterUserId` nullable per l'identità web, subject/description, state, priority (`low/normal/high/urgent`, default `normal`), category controllata (`general/hardware/software/network/account/other`, default `general`), `QueueId` e `AssigneeId` nullable, resolution/screenshot PNG limitato, binding nullable a `WorkflowId`/`WorkflowVersion`, version e date. Screenshot memorizzato nella colonna binaria esistente solo dopo consenso e validazione; l'identità web è cancellabile senza eliminare la storia ticket. FK e indici dedicati. Il binding nullo identifica i ticket precedenti o la policy predefinita |
| Queues | **Core implementato:** `SupportQueues` globali o associate a una sede, nome normalizzato univoco nell'ambito, versione e attiva/disattiva; membership account sostituita in modo versionato. Assegnazione ticket richiede `tickets.manage`, gli assegnatari devono appartenere alla coda, senza cambiare visibilità per device/sede. `SupportQueueRoutingRules` applica categoria globale con override di sede ai nuovi ticket e non può indirizzare una coda di un'altra sede. **Proposto:** target SLA |
| TicketDevices | PK (TicketId, DeviceId), LinkedAt/By, snapshot nome/sede al collegamento. Almeno una riga per ticket effettivo |
| TicketParticipants | PK ticket/utente/relazione, richiedente o follower; partecipazione non equivale a concessione RMM |
| PortalDeviceAccesses | **Implementato nel core:** relazione esplicita (UserId, DeviceId) concessa da administration e registrata con GrantedAt/GrantedBy. FK account/dispositivo, revoca esplicita e audit. Non concede capability RMM. Le letture del portale richiedono account attivo, link presente, sede attiva e credenziale dispositivo valida |
| QueueMembers / QueueRoutingRules | Membri Identity con FK e unicità coda/utente; la membership limita gli assegnatari proposti, ma non concede accesso ai ticket. Regole univoche per categoria/sede selezionano la coda dei nuovi ticket, con prevalenza della sede rispetto al fallback globale |
| Categories / Priorities | Categorie core piatte controllate e priorità low/normal/high/urgent implementate; gerarchia, priorità P1–P4 e matrice impatto/urgenza versionabile restano proposte |
| SupportWorkflows / SupportWorkflowVersions | **Implementato nel core:** bozza globale o di sede con optimistic concurrency; snapshot pubblicati immutabili; prevalenza della sede sul globale; ticket nuovi fissati alla versione attiva e ticket legacy sulla policy predefinita. Stati/transizioni validati, chiusura terminale e requisiti di risoluzione verificati server-side. Configurazione amministrativa globale con audit |
| SupportInterventionSummaries | **Implementato nel core:** TicketId, DeviceId, ArtifactType/ArtifactId già associati al ticket, capability, autore nullable, riepilogo, esito controllato, seguito opzionale e RecordedAt server-side. Insert-only; FK ticket/device/utente, indici per ticket e artefatto. API rilegge i permessi ticket/device e capability in ogni richiesta. Testo escluso da audit e outbox |
| TicketComments | TicketId, AuthorId/origine, Visibility public/internal, Body sanitizzato, CreatedAt, revisione/tombstone; cambi visibilità espliciti e auditati |
| Attachments | **Proposto, distinto dallo screenshot PNG ristretto esistente:** TicketId, CommentId nullable, UploaderId/origine, StorageKey opaco univoco, nome originale, MIME, Size, Hash, ScanState, Visibility, CreatedAt. Nessun contenuto binario o URL pubblico nel DB |
| TicketHistory | Evento append-only, ticket, attore/origine, azione, campi modificati minimizzati, motivo, At, EventId/correlation ID; non sostituisce AuditEvents |
| TicketRelations | TicketId, RelatedTicketId, tipo duplicate/parent/blocks, CHECK niente autoriferimento, unicità del collegamento; cicli verificati per relazioni gerarchiche |
| Interventions (concettuale) | Correlazione a RemoteSession/UpdateJob già registrati e timestamp autorevole dall'artefatto. Il tempo registrato non equivale alla durata del lavoro dell'operatore; i riepiloghi strutturati sono conservati in `SupportInterventionSummaries` |
| Intakes | Source, ExternalKey, ReceivedAt originale, VerifiedActorId nullable, SourceDeviceId nullable, ProposedSiteId, PayloadRef minimizzato, State, QualificationReason, PromotedTicketId. Unicità (Source, ExternalKey) quando disponibile |
| Mailboxes / EmailMessages / MailThreadBindings | Mailbox ID, riferimento segreto esterno, message ID/hash, intake/ticket, token thread hashato e scadenza, identità verificata. Nessuna password mailbox in chiaro |
| TicketAlertLinks | TicketId, AlertOccurrenceId, AutomationVersionId; unicità per occorrenza/policy di correlazione, collegamento idempotente |
| SlaPolicies / SlaPolicyVersions | Criteri di applicazione, obiettivi primo riscontro/risoluzione, pause e riapertura; versione pubblicata immutabile |
| BusinessCalendars / CalendarVersions / Holidays | Fuso IANA, fasce e festività versionate; calendario storico conservato per riprodurre i calcoli |
| TicketSlaTargets / SlaPauseIntervals | Ticket, tipo obiettivo, policy/calendario versione, start/due UTC, tempo consumato, pause start/end e motivo; vincoli intervalli validi e target univoco per ciclo |
| EscalationEvents | Target, ciclo, regola/versione, fase, dueAt, stato; chiave idempotente univoca per evitare duplicazioni al riavvio |
| AutomationRules / AutomationVersions / AutomationRuns | Eventi/condizioni/azioni allowlist, versione immutabile, origine/correlazione, esito e limiti; chiave evento/regola/versione per deduplica |
| NotificationOutbox / NotificationDeliveries | **Parziale nel core:** consumer `ticket-notifications.v1`, consegna idempotente per evento/utente, retry/backoff, dead-letter e proiezione `UserNotifications`; canali esterni, template versionati, lease esplicite e retention restano proposti |
| Outbox / Inbox / WorkerJobs | EventId univoco, tipo/versione, payload, stato/scadenza lease, tentativi e correlazione; inbox univoca per consumer/evento; quarantena errori dopo retry limitati |
| KnowledgeArticles / KnowledgeArticleRevisions | Implementati: contenuti/versione e snapshot immutabili lato API; SiteId o GroupId esclusivo per scope, entrambi null per globale; autore/data e backfill della versione corrente. `ArticleTicketLinks` e categorie restano proposti, non implementati |
| AiSuggestions (fase futura) | Ticket, autore richiedente, tipo proposta, contenuto minimizzato, provenienza/versione modello, stato accepted/rejected, scadenza; nessun comando eseguibile |
| ActionApprovals (solo futura estensione) | Richiedente/approvatore, ticket/device, azione, hash parametri, scadenza, stato e uso singolo; non è una coda di shell e non concede permessi |

## Invarianti e ciclo di vita

Stato schema effettivo al 2026-10-19: i messaggi di conversazione risiedono in `SupportMessages`; la migrazione `TicketInternalNotes` aggiunge `Visibility` (`public` per tutti i record preesistenti, `internal` per le note nuove). Il modello concettuale `TicketComments` resta una proposta per quando si implementeranno revisioni, tombstone e allegati associati ai commenti.

- `Tickets.SiteId` è NOT NULL. Il vincolo almeno un dispositivo non è ottenibile con un semplice CHECK sulla riga ticket: creazione, ricollegamento e promozione intake saranno transazionali, con verifica applicativa e constraint trigger differibile PostgreSQL da progettare/testare. Lo stesso vale per coerenza sede/device durante il collegamento. Non si rilasciano API che permettano ticket privi di associazioni.
- Promozione intake una sola volta: lock/versione e unicità su PromotedTicketId, scrittura ticket/dispositivi/storico/outbox nella stessa transazione. ReceivedAt non viene azzerato.
- TicketDevices non consente cancellazione dell'ultimo collegamento. Spostare un dispositivo non modifica retroattivamente la sede storica del ticket; nuovi collegamenti richiedono coerenza con la sede corrente. Eventuali riassociazioni storiche richiedono workflow esplicito.
- Sessioni/job possono essere eliminati solo secondo retention concordata: mantenere identificativo storico e snapshot non sensibile nell'intervento. Nessuna cancellazione cascade di ticket/audit quando un utente o dispositivo è rimosso; usare archiviazione e riferimenti storici/pseudonimi con cancellazione controllata dei dati personali.
- Allegati e note hanno identica classificazione di visibilità. Il riferimento CommentId, quando presente, deve appartenere allo stesso ticket. Le chiavi storage e i token thread non rappresentano un'autorizzazione al download o al ticket.
- Stato e assegnazioni cambiano insieme a storico e outbox, con Version/ETag. Versioni pubblicate di workflow, SLA, calendari e automazioni sono immutabili.
- Audit condiviso conserva correlazioni al modulo tramite identificatori; nessuna FK circolare obbligatoria che impedisca migrazioni indipendenti. FK di lettura verso core versionate e contratti di retention espliciti sono una dipendenza accettata del monolite modulare.

## Indici, ricerca e operazioni

Indici composti su sede/stato/priorità/coda/assegnatario e date; TicketDevices(DeviceId, TicketId), commenti e cronologia per ticket/time/Id; indici parziali per worker pronti e lease scadute. Unicità sulle chiavi di idempotenza degli ingressi, eventi e notifiche. Paginazione a cursore con ordinamento stabile e filtri autorizzativi prima di conteggi e risultati.

La ricerca KB attuale interroga titolo, descrizione e testo articolo sul server, filtra prima i risultati con grant globali/sede/gruppo e restituisce al massimo 50 metadati senza il corpo. La ricerca full-text PostgreSQL con GIN, ricerca ticket e proiezioni dedicate al portale restano proposte; note interne e articoli fuori ambito non devono comparire in suggerimenti/snippet. Nessun indice globale è restituito al portale senza filtri. Politiche di retention per email/allegati/eventi da definire in SD0; backup PostgreSQL, blob privati e chiavi applicative ripristinati e verificati insieme prima della produzione.

Per ogni migrazione futura: prova database vuoto, upgrade da dati M1–M5, backfill sedi, rollback operativo tramite backup e test concorrenza. Queste verifiche sono pianificate, non eseguite.
