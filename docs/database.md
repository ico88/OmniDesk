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

DeviceEnrollment: EnrollmentTokens (hash univoco, gruppo FK, scadenza/consumo/revoca), DeviceCredentials (PK/FK DeviceId, chiave pubblica e revoca), DeviceAssertions (jti PK e scadenza indicizzata), DeviceMetrics e DeviceInventories (PK/FK DeviceId, receivedAt server, payload JSONB ultimo snapshot). MeshSessions: MeshDeviceMappings (DeviceId PK/FK, NodeId univoco), RemoteSessions (device FK, actor, capability, state, publicId non bearer, scadenza; URL bearer non salvato). UpdateJobs: device FK, RequestId univoco per device, indice parziale univoco su device per queued/leased, payload/result JSONB e lease. UserDashboardLayouts: UserId PK/FK, widgets JSONB. Token Identity cifrati via ProtectedUserStore senza modifica del tipo colonna; la chiave DataProtection deve essere preservata con i backup.

## Migrazione UserLifecycle

Aggiunge `AspNetUsers.IsDisabled boolean NOT NULL DEFAULT false`: conserva gli account precedenti attivi, senza cambiare hash/password/ruoli o concedere nuovi permessi. La sospensione e le mutazioni ruoli aggiornano security stamp e audit in transazione. Lo stato remoto `revocation_pending` usa la colonna testuale esistente, senza nuova tabella. La migrazione va applicata prima dell'API aggiornata; backup e prova restore in [backup-restore.md](backup-restore.md).

## Schema futuro Service Desk — non implementato


Il [modello dati proposto](service-desk-data-model.md) definisce uno schema PostgreSQL `service_desk` separato con ticket, dispositivi collegati, code, workflow versionati, commenti pubblici/note interne, allegati privati, cronologia, interventi, intake/email, SLA/calendari, automazioni, notifiche e Knowledge Base. AI e approvazioni specifiche restano estensioni future senza esecuzione automatica di comandi.

Prerequisiti del core ancora assenti: Sites e associazione dispositivi/sede, concessioni per sede, allarmi persistenti e outbox eventi. Gruppi e sedi sono distinti. Ogni ticket effettivo richiede una sede e almeno un dispositivo; gli intake incompleti non sono ticket. Il vincolo multi-riga richiederà transazioni e constraint differibili da verificare, non un semplice CHECK. Nessuna migrazione EF Core o tabella Service Desk è stata creata: lo schema eseguibile esistente rimane invariato.
