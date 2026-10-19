# Schema database

La migrazione EF Core iniziale è la fonte eseguibile dello schema.

| Entità | Campi e vincoli |
| --- | --- |
| AspNetUsers / Roles / UserRoles / Claims / Logins / Tokens / RoleClaims | Identity; password hash, security stamp, lockout e predisposizione TOTP |
| DeviceGroups | UUID, Name |
| Devices | UUID, Name, GroupId FK nullable; nessun endpoint enrollment in M1 |
| RolePermissions | (RoleId, Permission) PK; FK Identity role |
| ScopeGrants | UUID, UserId FK, RoleId FK, GroupId oppure DeviceId FK; CHECK XOR, indici univoci per ambito |
| Branding | Id=1, Name, LogoUrl, PrimaryColor, Theme; singleton persistente |
| AuditEvents | bigint identity, At UTC, ActorId nullable testuale, Action, Target, Outcome; indice At; attore storico non cancellato con utente |

Autorizzazione: grant globale tramite Identity UserRoles, grant locale tramite ScopeGrants. Viewer=view, Operator=view/remote.control/terminal/files/updates, Administrator=tutte inclusa administration. Ruoli personalizzati creati tramite API combinano liberamente i sei permessi, senza implicazioni automatiche. Solo amministratori globali gestiscono utenti, concessioni e branding. Gli utenti possono leggere solo dispositivi autorizzati. Nessun device demo preinserito.

Future migrazioni: EnrollmentTokens (hash, expires, used, revoked), DeviceCredentials (chiave pubblica, revoca, rotazione), Metrics (device/time, retention), HardwareInventory, SoftwareInventory, UpdateJobs, RemoteSessions, DashboardLayouts per utente. Telemetria ad alto volume separabile dal DB transazionale. TOTP secret da proteggere con cifratura applicativa e chiavi esterne prima di attivazione.

Eseguire migrazioni con `docker compose run --rm migrate` prima di avviare API. L'API non modifica automaticamente lo schema. Backup coerente di PostgreSQL + volume DataProtection e Caddy; testare restore prima di aggiornare.

## Migrazioni M2–M5

DeviceEnrollment: EnrollmentTokens (hash univoco, gruppo FK, scadenza/consumo/revoca), DeviceCredentials (PK/FK DeviceId, chiave pubblica e revoca), DeviceAssertions (jti PK e scadenza indicizzata), DeviceMetrics e DeviceInventories (PK/FK DeviceId, receivedAt server, payload JSONB ultimo snapshot). MeshSessions: MeshDeviceMappings (DeviceId PK/FK, NodeId univoco), RemoteSessions (device FK, actor, capability, state, publicId non bearer, scadenza; URL bearer non salvato). UpdateJobs: device FK, RequestId univoco per device, indice parziale univoco su device per queued/leased, payload/result JSONB e lease. `Sites` e `SiteId` su Devices, EnrollmentTokens e SupportTickets; `ScopeGrants.SiteId` con check su un solo ambito. `CoreOutboxEvents` conserva tipo/versione, aggregate, JSONB minimizzato, correlation ID, retry/lease e stato pubblicazione; consumer e retention ancora da implementare. UserDashboardLayouts: UserId PK/FK, widgets JSONB. Token Identity cifrati via ProtectedUserStore senza modifica del tipo colonna; la chiave DataProtection deve essere preservata con i backup.
`SupportTickets` include inoltre numero Identity univoco leggibile, priorità (default `normal`), categoria controllata (default `general`), `QueueId` nullable con FK Restrict e `AssigneeId` nullable con FK Identity Restrict, indici per sede/stato/priorità/data, sede/categoria/stato/data, coda/stato/data e assegnatario. `SupportQueues` usa nome normalizzato univoco nell'ambito globale o di sede, versione ottimistica e disattivazione morbida; la FK Restrict verso `Sites` mantiene la sede in uso. Le code preesistenti sono globali. `SupportQueueMembers` ha PK composta coda/utente e non partecipa ai grant di visibilità. `SupportQueueRoutingRules` associa categoria e sede opzionale a una coda; indici parziali univoci separano regole globali e site-specific.

`RolePermissions` include `processes.manage` solo per Administrator predefinito; ruoli custom possono ricevere il permesso tramite gestione ruoli e grant scoped. Gli Operator/Viewer esistenti non ricevono la capability con la migrazione.

## Migrazione UserLifecycle

Aggiunge `AspNetUsers.IsDisabled boolean NOT NULL DEFAULT false`: conserva gli account precedenti attivi, senza cambiare hash/password/ruoli o concedere nuovi permessi. La sospensione e le mutazioni ruoli aggiornano security stamp e audit in transazione. Lo stato remoto `revocation_pending` usa la colonna testuale esistente, senza nuova tabella. La migrazione va applicata prima dell'API aggiornata; backup e prova restore in [backup-restore.md](backup-restore.md).

## Service Desk — implementazione incrementale nel core API


Il [modello dati proposto](service-desk-data-model.md) descrive l'evoluzione Service Desk, mentre lo schema corrente usa migrazioni versionate nello stesso database PostgreSQL dell'API. Sono implementati ticket, dispositivi collegati, code, workflow versionati, commenti pubblici/note interne, allegati privati, cronologia, interventi, notifiche, Knowledge Base e policy/target SLA iniziali. Intake email, automazioni complete, timer SLA, allarmi e viste SLA restano incrementi futuri. AI e approvazioni specifiche non eseguono comandi automaticamente.

Sedi, associazioni e concessioni per sede, ticket core e outbox sono implementati con migrazioni EF; priorità e assegnatario hanno la migrazione `TicketAssignmentAndPriority`. Gruppi e sedi sono distinti. Restano da implementare allarmi persistenti, pause/completamento dei timer SLA e gli altri incrementi indicati nel modello. Il ticket multi-dispositivo è già supportato con snapshot e verifiche autorizzative per ogni dispositivo.
