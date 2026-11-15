-- OmniDesk Remote: schema di riferimento per la revisione.
-- STATO: progettato, non applicato, non testato.
-- La migrazione reale va generata con EF Core (convenzione OmniDesk: nomi
-- PascalCase tra virgolette, chiavi Guid generate dall'applicazione,
-- DateTimeOffset -> timestamptz) e confrontata con questo file.
-- Tabelle esistenti referenziate: "Devices", "DeviceGroups", "AspNetUsers",
-- "SupportTickets", "RemoteSessions".

-- 1. RemoteSessions (esistente): nuove colonne e vincoli -----------------

ALTER TABLE "RemoteSessions"
  ADD COLUMN "Engine" text NOT NULL DEFAULT 'meshcentral',
  ADD COLUMN "Mode" text NOT NULL DEFAULT 'unattended',
  ADD COLUMN "ConsentState" text NOT NULL DEFAULT 'not_required',
  ADD COLUMN "ConsentDecidedAt" timestamptz NULL,
  ADD COLUMN "ConsentWindowsUser" text NULL,
  ADD COLUMN "StartedAt" timestamptz NULL,
  ADD COLUMN "LastHeartbeatAt" timestamptz NULL,
  ADD COLUMN "EndReason" text NULL,
  ADD COLUMN "TicketId" uuid NULL,
  ADD COLUMN "InvitationId" uuid NULL,
  ADD COLUMN "PolicyId" uuid NULL,
  ADD COLUMN "Comment" text NULL;

ALTER TABLE "RemoteSessions"
  ADD CONSTRAINT "CK_RemoteSession_Engine"
    CHECK ("Engine" IN ('meshcentral','rustdesk','native')),
  ADD CONSTRAINT "CK_RemoteSession_Mode"
    CHECK ("Mode" IN ('unattended','attended','invitation')),
  ADD CONSTRAINT "CK_RemoteSession_ConsentState"
    CHECK ("ConsentState" IN ('not_required','pending','granted','declined','expired')),
  ADD CONSTRAINT "CK_RemoteSession_State"
    CHECK ("State" IN ('pending','consent_pending','connecting','active','ended',
                       'declined','expired','failed','revocation_pending','revoked')),
  ADD CONSTRAINT "CK_RemoteSession_EndAfterStart"
    CHECK ("EndedAt" IS NULL OR "EndedAt" >= "CreatedAt"),
  ADD CONSTRAINT "CK_RemoteSession_CommentLength"
    CHECK ("Comment" IS NULL OR length("Comment") <= 2000),
  ADD CONSTRAINT "FK_RemoteSession_Ticket"
    FOREIGN KEY ("TicketId") REFERENCES "SupportTickets"("Id") ON DELETE SET NULL;
-- Verificare prima i valori di "State" già presenti in produzione
-- (pending, active, revocation_pending, revoked, ended...) per non far
-- fallire il vincolo; eventuali valori diversi vanno mappati nella migrazione.

-- Un solo controllo attivo per dispositivo; gli altri tecnici entrano come partecipanti.
CREATE UNIQUE INDEX "UX_RemoteSession_OneActiveControl"
  ON "RemoteSessions" ("DeviceId")
  WHERE "State" IN ('connecting','active') AND "Capability" = 'remote.control';
CREATE INDEX "IX_RemoteSession_Device_Created" ON "RemoteSessions" ("DeviceId", "CreatedAt" DESC);
CREATE INDEX "IX_RemoteSession_Actor_Created" ON "RemoteSessions" ("ActorId", "CreatedAt" DESC);
CREATE INDEX "IX_RemoteSession_OpenStates" ON "RemoteSessions" ("State")
  WHERE "State" IN ('pending','consent_pending','connecting','active','revocation_pending');
CREATE INDEX "IX_RemoteSession_Ticket" ON "RemoteSessions" ("TicketId");
CREATE INDEX "IX_RemoteSession_Invitation" ON "RemoteSessions" ("InvitationId");
CREATE INDEX "IX_RemoteSession_Policy" ON "RemoteSessions" ("PolicyId");

-- 2. Policy ---------------------------------------------------------------

CREATE TABLE "RemotePolicies" (
  "Id" uuid PRIMARY KEY,
  "Name" text NOT NULL,
  "Version" bigint NOT NULL DEFAULT 1,           -- incrementata a ogni modifica; il guard la confronta
  "IsDefault" boolean NOT NULL DEFAULT false,
  "RequireConsent" boolean NOT NULL DEFAULT true,
  "AllowUnattended" boolean NOT NULL DEFAULT false,
  "ConsentTimeoutSeconds" integer NOT NULL DEFAULT 60,
  "Clipboard" text NOT NULL DEFAULT 'off',
  "FileTransfer" boolean NOT NULL DEFAULT false,
  "MaxFileBytes" bigint NOT NULL DEFAULT 104857600,
  "AllowBlackScreen" boolean NOT NULL DEFAULT false,
  "AllowDisableInput" boolean NOT NULL DEFAULT false,
  "AllowReboot" boolean NOT NULL DEFAULT true,
  "LockOnEnd" text NOT NULL DEFAULT 'auto',
  "AutoRecord" boolean NOT NULL DEFAULT false,
  "RecordingRequiresTechnicianConfirm" boolean NOT NULL DEFAULT true,
  "MaxSessionMinutes" integer NOT NULL DEFAULT 240,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_RemotePolicy_Clipboard" CHECK ("Clipboard" IN ('off','to_remote','from_remote','both')),
  CONSTRAINT "CK_RemotePolicy_LockOnEnd" CHECK ("LockOnEnd" IN ('always','never','auto')),
  CONSTRAINT "CK_RemotePolicy_ConsentTimeout" CHECK ("ConsentTimeoutSeconds" BETWEEN 15 AND 600),
  CONSTRAINT "CK_RemotePolicy_MaxFile" CHECK ("MaxFileBytes" > 0),
  CONSTRAINT "CK_RemotePolicy_MaxSession" CHECK ("MaxSessionMinutes" BETWEEN 5 AND 1440),
  CONSTRAINT "CK_RemotePolicy_ConsentOrUnattended"
    CHECK (NOT ("AllowUnattended" = false AND "RequireConsent" = false))
);
CREATE UNIQUE INDEX "UX_RemotePolicy_Name" ON "RemotePolicies" (lower("Name"));
CREATE UNIQUE INDEX "UX_RemotePolicy_SingleDefault" ON "RemotePolicies" ("IsDefault") WHERE "IsDefault";

ALTER TABLE "RemoteSessions"
  ADD CONSTRAINT "FK_RemoteSession_Policy"
    FOREIGN KEY ("PolicyId") REFERENCES "RemotePolicies"("Id") ON DELETE SET NULL;

-- Precedenza: assegnazione al dispositivo > al gruppo del dispositivo > policy default.
-- OmniDesk ha un solo gruppo per dispositivo: nessuna ambiguità di ereditarietà.
CREATE TABLE "RemotePolicyAssignments" (
  "Id" uuid PRIMARY KEY,
  "PolicyId" uuid NOT NULL REFERENCES "RemotePolicies"("Id") ON DELETE CASCADE,
  "DeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE CASCADE,
  "GroupId" uuid NULL REFERENCES "DeviceGroups"("Id") ON DELETE CASCADE,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_RemotePolicyAssignment_OneScope" CHECK (num_nonnulls("DeviceId","GroupId") = 1)
);
CREATE UNIQUE INDEX "UX_RemotePolicyAssignment_Device" ON "RemotePolicyAssignments" ("DeviceId") WHERE "DeviceId" IS NOT NULL;
CREATE UNIQUE INDEX "UX_RemotePolicyAssignment_Group" ON "RemotePolicyAssignments" ("GroupId") WHERE "GroupId" IS NOT NULL;
CREATE INDEX "IX_RemotePolicyAssignment_Policy" ON "RemotePolicyAssignments" ("PolicyId");

-- 3. Inviti assistiti ------------------------------------------------------

CREATE TABLE "RemoteSupportInvitations" (
  "Id" uuid PRIMARY KEY,
  "Name" text NOT NULL,
  "CreatedBy" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "GroupId" uuid NULL REFERENCES "DeviceGroups"("Id") ON DELETE SET NULL, -- ambito su cui il tecnico ha remote.control
  "CodeHash" bytea NOT NULL,          -- HMAC-SHA256 con chiave server (Data Protection), mai il codice in chiaro
  "LinkTokenHash" bytea NOT NULL,
  "DeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE SET NULL,   -- valorizzato quando la Tray entra
  "JoinedAt" timestamptz NULL,
  "ConsumedAt" timestamptz NULL,      -- il tecnico ha creato la sessione
  "RevokedAt" timestamptz NULL,
  "ExpiresAt" timestamptz NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_RemoteInvitation_Expiry" CHECK ("ExpiresAt" > "CreatedAt"),
  CONSTRAINT "CK_RemoteInvitation_MaxLifetime" CHECK ("ExpiresAt" <= "CreatedAt" + interval '24 hours'),
  CONSTRAINT "CK_RemoteInvitation_NameLength" CHECK (length("Name") BETWEEN 1 AND 120)
);
CREATE UNIQUE INDEX "UX_RemoteInvitation_Code_Open" ON "RemoteSupportInvitations" ("CodeHash")
  WHERE "ConsumedAt" IS NULL AND "RevokedAt" IS NULL;
CREATE UNIQUE INDEX "UX_RemoteInvitation_Link" ON "RemoteSupportInvitations" ("LinkTokenHash");
CREATE INDEX "IX_RemoteInvitation_CreatedBy" ON "RemoteSupportInvitations" ("CreatedBy", "CreatedAt" DESC);
CREATE INDEX "IX_RemoteInvitation_Expires" ON "RemoteSupportInvitations" ("ExpiresAt");
CREATE INDEX "IX_RemoteInvitation_Device" ON "RemoteSupportInvitations" ("DeviceId");
CREATE INDEX "IX_RemoteInvitation_Group" ON "RemoteSupportInvitations" ("GroupId");

ALTER TABLE "RemoteSessions"
  ADD CONSTRAINT "FK_RemoteSession_Invitation"
    FOREIGN KEY ("InvitationId") REFERENCES "RemoteSupportInvitations"("Id") ON DELETE SET NULL;

-- Backoff esponenziale sui codici errati, per origine.
CREATE TABLE "RemoteCodeAttempts" (
  "Id" uuid PRIMARY KEY,
  "SourceHash" bytea NOT NULL,        -- hash dell'identità dispositivo o dell'IP, mai l'IP in chiaro
  "FailedCount" integer NOT NULL DEFAULT 0,
  "LockedUntil" timestamptz NULL,
  "LastFailedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_RemoteCodeAttempt_Count" CHECK ("FailedCount" >= 0)
);
CREATE UNIQUE INDEX "UX_RemoteCodeAttempt_Source" ON "RemoteCodeAttempts" ("SourceHash");
CREATE INDEX "IX_RemoteCodeAttempt_LastFailed" ON "RemoteCodeAttempts" ("LastFailedAt");

-- 4. Partecipanti ------------------------------------------------------------

CREATE TABLE "RemoteSessionParticipants" (
  "Id" uuid PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "RemoteSessions"("Id") ON DELETE CASCADE,
  "UserId" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "Role" text NOT NULL,
  "Capability" text NOT NULL,         -- remote.view o remote.control, ricontrollato dal guard
  "InvitedBy" text NULL REFERENCES "AspNetUsers"("Id") ON DELETE SET NULL,
  "JoinedAt" timestamptz NULL,
  "LeftAt" timestamptz NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_RemoteParticipant_Role" CHECK ("Role" IN ('owner','invited')),
  CONSTRAINT "CK_RemoteParticipant_Capability" CHECK ("Capability" IN ('remote.view','remote.control'))
);
CREATE UNIQUE INDEX "UX_RemoteParticipant_SessionUser" ON "RemoteSessionParticipants" ("SessionId", "UserId");
CREATE INDEX "IX_RemoteParticipant_User" ON "RemoteSessionParticipants" ("UserId");
CREATE INDEX "IX_RemoteParticipant_InvitedBy" ON "RemoteSessionParticipants" ("InvitedBy");

-- 5. Eventi in sessione (metadati, mai contenuti) --------------------------

CREATE TABLE "RemoteSessionEvents" (
  "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "RemoteSessions"("Id") ON DELETE CASCADE,
  "ClientEventId" uuid NULL,          -- idempotenza per eventi ripetuti dall'Agent
  "ActorId" text NULL REFERENCES "AspNetUsers"("Id") ON DELETE SET NULL,
  "Source" text NOT NULL,
  "Type" text NOT NULL,
  "Meta" jsonb NOT NULL DEFAULT '{}', -- es. {"monitor":2}; vietati testi appunti/chat/tasti
  "At" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_RemoteEvent_Source" CHECK ("Source" IN ('technician','host','tray','server')),
  CONSTRAINT "CK_RemoteEvent_Type" CHECK ("Type" IN (
    'consent_requested','consent_granted','consent_declined','connected','disconnected',
    'desktop_changed','monitor_switched','reboot','reboot_safe','reconnected','lock','logoff',
    'ctrl_alt_del','black_screen_on','black_screen_off','input_disabled','input_enabled',
    'clipboard_used','chat_used','file_transfer','screenshot','recording_started','recording_stopped',
    'participant_joined','participant_left','note_left','policy_violation','ended','revoked')),
  CONSTRAINT "CK_RemoteEvent_MetaSize" CHECK (pg_column_size("Meta") <= 2048)
);
CREATE UNIQUE INDEX "UX_RemoteEvent_Client" ON "RemoteSessionEvents" ("SessionId", "ClientEventId")
  WHERE "ClientEventId" IS NOT NULL;
CREATE INDEX "IX_RemoteEvent_Session_At" ON "RemoteSessionEvents" ("SessionId", "At");
CREATE INDEX "IX_RemoteEvent_Actor" ON "RemoteSessionEvents" ("ActorId");

-- 6. Trasferimenti file (solo audit: il contenuto va diretto browser<->host) --

CREATE TABLE "RemoteFileTransfers" (
  "Id" uuid PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "RemoteSessions"("Id") ON DELETE CASCADE,
  "ActorId" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "Direction" text NOT NULL,
  "FileName" text NOT NULL,
  "SizeBytes" bigint NOT NULL,
  "Sha256" bytea NULL,
  "State" text NOT NULL DEFAULT 'authorized',
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "CompletedAt" timestamptz NULL,
  CONSTRAINT "CK_RemoteFile_Direction" CHECK ("Direction" IN ('to_remote','from_remote')),
  CONSTRAINT "CK_RemoteFile_State" CHECK ("State" IN ('authorized','completed','failed','cancelled')),
  CONSTRAINT "CK_RemoteFile_Size" CHECK ("SizeBytes" >= 0),
  CONSTRAINT "CK_RemoteFile_NameLength" CHECK (length("FileName") BETWEEN 1 AND 260)
);
CREATE INDEX "IX_RemoteFile_Session" ON "RemoteFileTransfers" ("SessionId", "CreatedAt");
CREATE INDEX "IX_RemoteFile_Actor" ON "RemoteFileTransfers" ("ActorId");

-- 7. Registrazioni -------------------------------------------------------------

CREATE TABLE "RemoteRecordings" (
  "Id" uuid PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "RemoteSessions"("Id") ON DELETE RESTRICT, -- non perdere prove cancellando sessioni
  "StartedBy" text NULL REFERENCES "AspNetUsers"("Id") ON DELETE SET NULL,
  "Automatic" boolean NOT NULL,
  "StoragePath" text NOT NULL,        -- relativo al volume registrazioni, mai fornito dal client
  "ContentType" text NOT NULL DEFAULT 'video/webm',
  "SizeBytes" bigint NOT NULL DEFAULT 0,
  "Sha256" bytea NULL,
  "State" text NOT NULL DEFAULT 'uploading',
  "StartedAt" timestamptz NOT NULL DEFAULT now(),
  "EndedAt" timestamptz NULL,
  "DeleteAfter" timestamptz NULL,     -- da retention; NULL = conservare
  CONSTRAINT "CK_RemoteRecording_State" CHECK ("State" IN ('uploading','complete','incomplete','deleted')),
  CONSTRAINT "CK_RemoteRecording_Size" CHECK ("SizeBytes" >= 0)
);
CREATE INDEX "IX_RemoteRecording_Session" ON "RemoteRecordings" ("SessionId");
CREATE INDEX "IX_RemoteRecording_StartedBy" ON "RemoteRecordings" ("StartedBy");
CREATE INDEX "IX_RemoteRecording_DeleteAfter" ON "RemoteRecordings" ("DeleteAfter") WHERE "DeleteAfter" IS NOT NULL;

-- 8. Wake-on-LAN (fase should) ---------------------------------------------------

CREATE TABLE "RemoteWakeRequests" (
  "Id" uuid PRIMARY KEY,
  "TargetDeviceId" uuid NOT NULL REFERENCES "Devices"("Id") ON DELETE CASCADE,
  "RelayDeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE SET NULL, -- Agent online nella stessa sede
  "RequestedBy" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "State" text NOT NULL DEFAULT 'queued',
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "SentAt" timestamptz NULL,
  "TargetOnlineAt" timestamptz NULL,
  CONSTRAINT "CK_RemoteWake_State" CHECK ("State" IN ('queued','sent','online','timeout','no_relay')),
  CONSTRAINT "CK_RemoteWake_NotSelf" CHECK ("RelayDeviceId" IS NULL OR "RelayDeviceId" <> "TargetDeviceId")
);
CREATE INDEX "IX_RemoteWake_Target" ON "RemoteWakeRequests" ("TargetDeviceId", "CreatedAt" DESC);
CREATE INDEX "IX_RemoteWake_Relay_State" ON "RemoteWakeRequests" ("RelayDeviceId", "State");
CREATE INDEX "IX_RemoteWake_RequestedBy" ON "RemoteWakeRequests" ("RequestedBy");

-- 9. Permessi nuovi (seed) ----------------------------------------------------------
-- Da aggiungere a Permissions.All e a ForRole("Operator") nel codice, più seed
-- in "RolePermissions" per i ruoli esistenti:
--   remote.view, remote.unattended, remote.recordings.view (quest'ultimo solo Administrator)
