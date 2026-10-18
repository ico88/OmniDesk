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
