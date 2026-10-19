# Service Desk — schema PostgreSQL proposto

**Il Service Desk è incrementale:** ticket e conversazione esistono nel core OmniDesk; non è ancora stato creato lo schema PostgreSQL separato `service_desk`. Sedi, permessi per sede, snapshot ticket, outbox, priorità e assegnazione hanno migrazioni core. Code, workflow configurabili, partecipanti, allegati generici, SLA, email, automazioni e knowledge base restano proposte per le fasi successive. UUID, timestamp UTC, versioni per concorrenza ottimistica e FK esplicite; JSONB è riservato a snapshot/eventi versionati, non sostituisce relazioni e vincoli.

## Prerequisiti core — stato

| Entità/estensione | Responsabilità |
| --- | --- |
| Sites; Devices.SiteId | **Implementato:** sedi archiviabili, FK dispositivo, enrollment per sede, sede storica sui ticket e backfill dei record esistenti a “Sede da assegnare”. Le sedi restano distinte da DeviceGroups. Migrazione e API verificate su database vuoto; upgrade di un database popolato resta da verificare |
| Concessioni per sede | **Implementato:** `ScopeGrants.SiteId` con vincolo PostgreSQL che ammette un solo ambito; permessi di sede non si trasformano in permessi gruppo/device. Testata visibilità e separazione del permesso `view` |
| AlertOccurrences / AlertEvents | Identità stabile di occorrenza, device, regola/versione, apertura/recupero e correlazione; gli snapshot metriche esistenti non sono allarmi |
| Outbox core | **Fondazione implementata:** tabella `CoreOutboxEvents` e scritture transazionali per enrollment, sedi, metadata device e ticket. Payload curato senza contenuti ticket, tipo/versione e correlation ID. Da completare consumer, idempotenza di consegna, retry/backoff, dead-letter, retention e copertura di tutti gli eventi core |

## Tabelle del modulo

| Entità proposta | Campi e vincoli principali |
| --- | --- |
| Tickets | **Core implementato:** `SupportTickets` contiene numero Identity univoco, device/sede storica, requester macchina, subject/description, state, priority (`low/normal/high/urgent`, default `normal`), `AssigneeId` Identity nullable, resolution/screenshot, version e date. FK assignee Restrict e indici dedicati. **Proposto:** QueueId, workflow configurabile/versionato, CategoryId e ciclo Received/Resolved/Closed strutturato |
| TicketDevices | PK (TicketId, DeviceId), LinkedAt/By, snapshot nome/sede al collegamento. Almeno una riga per ticket effettivo |
| TicketParticipants | PK ticket/utente/relazione, richiedente o follower; partecipazione non equivale a concessione RMM |
| Queues / QueueMembers | Code/team e membri Identity; membership soggetta alle policy di ambito |
| Categories / Priorities | Gerarchia categoria opzionale, priorità P1–P4 e matrice impatto/urgenza versionabile |
| WorkflowVersions / States / Transitions | Stati, transizioni ammesse, requisiti di motivo/risoluzione e versione pubblicata immutabile |
| TicketComments | TicketId, AuthorId/origine, Visibility public/internal, Body sanitizzato, CreatedAt, revisione/tombstone; cambi visibilità espliciti e auditati |
| Attachments | TicketId, CommentId nullable, UploaderId/origine, StorageKey opaco univoco, nome originale, MIME, Size, Hash, ScanState, Visibility, CreatedAt. Nessun contenuto binario o URL pubblico nel DB |
| TicketHistory | Evento append-only, ticket, attore/origine, azione, campi modificati minimizzati, motivo, At, EventId/correlation ID; non sostituisce AuditEvents |
| TicketRelations | TicketId, RelatedTicketId, tipo duplicate/parent/blocks, CHECK niente autoriferimento, unicità del collegamento; cicli verificati per relazioni gerarchiche |
| Interventions | TicketId, DeviceId, operatore, tempi/durata, riepilogo/esito, RemoteSessionId o UpdateJobId nullable; dispositivo collegato al ticket, riferimenti senza credenziali |
| Intakes | Source, ExternalKey, ReceivedAt originale, VerifiedActorId nullable, SourceDeviceId nullable, ProposedSiteId, PayloadRef minimizzato, State, QualificationReason, PromotedTicketId. Unicità (Source, ExternalKey) quando disponibile |
| Mailboxes / EmailMessages / MailThreadBindings | Mailbox ID, riferimento segreto esterno, message ID/hash, intake/ticket, token thread hashato e scadenza, identità verificata. Nessuna password mailbox in chiaro |
| TicketAlertLinks | TicketId, AlertOccurrenceId, AutomationVersionId; unicità per occorrenza/policy di correlazione, collegamento idempotente |
| SlaPolicies / SlaPolicyVersions | Criteri di applicazione, obiettivi primo riscontro/risoluzione, pause e riapertura; versione pubblicata immutabile |
| BusinessCalendars / CalendarVersions / Holidays | Fuso IANA, fasce e festività versionate; calendario storico conservato per riprodurre i calcoli |
| TicketSlaTargets / SlaPauseIntervals | Ticket, tipo obiettivo, policy/calendario versione, start/due UTC, tempo consumato, pause start/end e motivo; vincoli intervalli validi e target univoco per ciclo |
| EscalationEvents | Target, ciclo, regola/versione, fase, dueAt, stato; chiave idempotente univoca per evitare duplicazioni al riavvio |
| AutomationRules / AutomationVersions / AutomationRuns | Eventi/condizioni/azioni allowlist, versione immutabile, origine/correlazione, esito e limiti; chiave evento/regola/versione per deduplica |
| NotificationOutbox / NotificationDeliveries | Ticket/evento, destinatario verificato, template/versione/visibilità, stato, tentativi, NextAttemptAt, LeaseUntil, errore minimizzato; idempotenza evento/destinatario/canale |
| Outbox / Inbox / WorkerJobs | EventId univoco, tipo/versione, payload, stato/scadenza lease, tentativi e correlazione; inbox univoca per consumer/evento; quarantena errori dopo retry limitati |
| KnowledgeArticles / ArticleVersions / ArticleScopes / ArticleTicketLinks | Bozza/revisione/pubblicazione, contenuti/versione, ambiti lettura, autore/revisore, collegamenti ticket senza esporre note interne |
| AiSuggestions (fase futura) | Ticket, autore richiedente, tipo proposta, contenuto minimizzato, provenienza/versione modello, stato accepted/rejected, scadenza; nessun comando eseguibile |
| ActionApprovals (solo futura estensione) | Richiedente/approvatore, ticket/device, azione, hash parametri, scadenza, stato e uso singolo; non è una coda di shell e non concede permessi |

## Invarianti e ciclo di vita

- `Tickets.SiteId` è NOT NULL. Il vincolo almeno un dispositivo non è ottenibile con un semplice CHECK sulla riga ticket: creazione, ricollegamento e promozione intake saranno transazionali, con verifica applicativa e constraint trigger differibile PostgreSQL da progettare/testare. Lo stesso vale per coerenza sede/device durante il collegamento. Non si rilasciano API che permettano ticket privi di associazioni.
- Promozione intake una sola volta: lock/versione e unicità su PromotedTicketId, scrittura ticket/dispositivi/storico/outbox nella stessa transazione. ReceivedAt non viene azzerato.
- TicketDevices non consente cancellazione dell'ultimo collegamento. Spostare un dispositivo non modifica retroattivamente la sede storica del ticket; nuovi collegamenti richiedono coerenza con la sede corrente. Eventuali riassociazioni storiche richiedono workflow esplicito.
- Sessioni/job possono essere eliminati solo secondo retention concordata: mantenere identificativo storico e snapshot non sensibile nell'intervento. Nessuna cancellazione cascade di ticket/audit quando un utente o dispositivo è rimosso; usare archiviazione e riferimenti storici/pseudonimi con cancellazione controllata dei dati personali.
- Allegati e note hanno identica classificazione di visibilità. Il riferimento CommentId, quando presente, deve appartenere allo stesso ticket. Le chiavi storage e i token thread non rappresentano un'autorizzazione al download o al ticket.
- Stato e assegnazioni cambiano insieme a storico e outbox, con Version/ETag. Versioni pubblicate di workflow, SLA, calendari e automazioni sono immutabili.
- Audit condiviso conserva correlazioni al modulo tramite identificatori; nessuna FK circolare obbligatoria che impedisca migrazioni indipendenti. FK di lettura verso core versionate e contratti di retention espliciti sono una dipendenza accettata del monolite modulare.

## Indici, ricerca e operazioni

Indici composti su sede/stato/priorità/coda/assegnatario e date; TicketDevices(DeviceId, TicketId), commenti e cronologia per ticket/time/Id; indici parziali per worker pronti e lease scadute. Unicità sulle chiavi di idempotenza degli ingressi, eventi e notifiche. Paginazione a cursore con ordinamento stabile e filtri autorizzativi prima di conteggi e risultati.

Ricerca PostgreSQL full-text con GIN proposta per ticket e KB; proiezioni pubbliche separate dalle note interne e controlli di ambito anche nei suggerimenti/snippet. Nessun indice globale restituito al portale senza filtri. Politiche di retention per email/allegati/eventi da definire in SD0; backup PostgreSQL, blob privati e chiavi applicative ripristinati e verificati insieme prima della produzione.

Per ogni migrazione futura: prova database vuoto, upgrade da dati M1–M5, backfill sedi, rollback operativo tramite backup e test concorrenza. Queste verifiche sono pianificate, non eseguite.
