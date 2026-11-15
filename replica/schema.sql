-- OmniDesk (nuovo, solo connessione remota): schema di riferimento.
-- STATO: progettato. Verificato solo applicandolo su PostgreSQL 16 di prova
-- con le tabelle ASP.NET Identity ridotte a stub. La migrazione reale va
-- generata con EF Core e confrontata con questo file.
-- Convenzione: nomi PascalCase tra virgolette (Npgsql/EF), chiavi uuid
-- generate dall'applicazione, tempi timestamptz in UTC.
-- Tabelle Identity attese (create dalla migrazione Identity): "AspNetUsers", "AspNetRoles".

-- Permessi ------------------------------------------------------------------

CREATE TABLE "RolePermissions" (
  "RoleId" text NOT NULL REFERENCES "AspNetRoles"("Id") ON DELETE CASCADE,
  "Permission" text NOT NULL,
  PRIMARY KEY ("RoleId", "Permission"),
  CONSTRAINT "CK_RolePermission_Known" CHECK ("Permission" IN (
    'devices.view','remote.view','remote.control','remote.unattended',
    'files','recordings.view','administration'))
);

CREATE TABLE "DeviceGroups" (
  "Id" uuid PRIMARY KEY,
  "Name" text NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_DeviceGroup_Name" CHECK (length("Name") BETWEEN 1 AND 120)
);
CREATE UNIQUE INDEX "UX_DeviceGroup_Name" ON "DeviceGroups" (lower("Name"));

CREATE TABLE "Devices" (
  "Id" uuid PRIMARY KEY,
  "Name" text NOT NULL,
  "GroupId" uuid NOT NULL REFERENCES "DeviceGroups"("Id") ON DELETE RESTRICT, -- svuotare il gruppo prima di eliminarlo
  "MachineName" text NOT NULL,
  "OsVersion" text NULL,
  "AgentVersion" text NULL,
  "MacAddresses" text[] NOT NULL DEFAULT '{}',   -- per Wake-on-LAN
  "RevokedAt" timestamptz NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_Device_Name" CHECK (length("Name") BETWEEN 1 AND 120)
);
CREATE INDEX "IX_Device_Group" ON "Devices" ("GroupId");
CREATE INDEX "IX_Device_Name" ON "Devices" (lower("Name"));

CREATE TABLE "ScopeGrants" (
  "Id" uuid PRIMARY KEY,
  "UserId" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "RoleId" text NOT NULL REFERENCES "AspNetRoles"("Id") ON DELETE CASCADE,
  "GroupId" uuid NULL REFERENCES "DeviceGroups"("Id") ON DELETE CASCADE,
  "DeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE CASCADE,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_ScopeGrant_OneScope" CHECK (num_nonnulls("GroupId","DeviceId") = 1)
);
-- I ruoli globali usano AspNetUserRoles di Identity; qui solo ambiti group/device.
CREATE UNIQUE INDEX "UX_ScopeGrant_Group" ON "ScopeGrants" ("UserId","RoleId","GroupId") WHERE "GroupId" IS NOT NULL;
CREATE UNIQUE INDEX "UX_ScopeGrant_Device" ON "ScopeGrants" ("UserId","RoleId","DeviceId") WHERE "DeviceId" IS NOT NULL;
CREATE INDEX "IX_ScopeGrant_Role" ON "ScopeGrants" ("RoleId");
CREATE INDEX "IX_ScopeGrant_Group" ON "ScopeGrants" ("GroupId");
CREATE INDEX "IX_ScopeGrant_Device" ON "ScopeGrants" ("DeviceId");

-- Registrazione e identità dispositivo ------------------------------------------

CREATE TABLE "EnrollmentTokens" (
  "Id" uuid PRIMARY KEY,
  "TokenHash" bytea NOT NULL,                     -- SHA-256 di un token casuale ad alta entropia
  "GroupId" uuid NOT NULL REFERENCES "DeviceGroups"("Id") ON DELETE CASCADE,
  "CreatedBy" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "ExpiresAt" timestamptz NOT NULL,
  "ConsumedAt" timestamptz NULL,
  "ConsumedByDeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE SET NULL,
  "RevokedAt" timestamptz NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_EnrollmentToken_Expiry" CHECK ("ExpiresAt" > "CreatedAt"
                                                AND "ExpiresAt" <= "CreatedAt" + interval '7 days')
);
CREATE UNIQUE INDEX "UX_EnrollmentToken_Hash" ON "EnrollmentTokens" ("TokenHash");
CREATE INDEX "IX_EnrollmentToken_Group" ON "EnrollmentTokens" ("GroupId");
CREATE INDEX "IX_EnrollmentToken_CreatedBy" ON "EnrollmentTokens" ("CreatedBy");
CREATE INDEX "IX_EnrollmentToken_Device" ON "EnrollmentTokens" ("ConsumedByDeviceId");

CREATE TABLE "DeviceKeys" (
  "Id" uuid PRIMARY KEY,
  "DeviceId" uuid NOT NULL REFERENCES "Devices"("Id") ON DELETE CASCADE,
  "Algorithm" text NOT NULL DEFAULT 'ES256',
  "PublicKey" bytea NOT NULL,                     -- SubjectPublicKeyInfo DER; la chiave privata resta sul PC
  "Thumbprint" bytea NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "RevokedAt" timestamptz NULL,
  CONSTRAINT "CK_DeviceKey_Algorithm" CHECK ("Algorithm" IN ('ES256'))
);
CREATE UNIQUE INDEX "UX_DeviceKey_Thumbprint" ON "DeviceKeys" ("Thumbprint");
CREATE UNIQUE INDEX "UX_DeviceKey_OneActive" ON "DeviceKeys" ("DeviceId") WHERE "RevokedAt" IS NULL;

CREATE TABLE "DevicePresence" (
  "DeviceId" uuid PRIMARY KEY REFERENCES "Devices"("Id") ON DELETE CASCADE,
  "IsOnline" boolean NOT NULL DEFAULT false,
  "LastSeenAt" timestamptz NULL,
  "ConsoleUser" text NULL,                        -- utente Windows collegato, se presente
  "ChangedAt" timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX "IX_DevicePresence_Online" ON "DevicePresence" ("IsOnline");

-- Policy -----------------------------------------------------------------------

CREATE TABLE "Policies" (
  "Id" uuid PRIMARY KEY,
  "Name" text NOT NULL,
  "Version" bigint NOT NULL DEFAULT 1,            -- incrementata a ogni modifica, il guardiano la confronta
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
  "AllowCtrlAltDel" boolean NOT NULL DEFAULT true,
  "LockOnEnd" text NOT NULL DEFAULT 'auto',
  "Record" text NOT NULL DEFAULT 'off',
  "MaxSessionMinutes" integer NOT NULL DEFAULT 240,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_Policy_Clipboard" CHECK ("Clipboard" IN ('off','to_remote','from_remote','both')),
  CONSTRAINT "CK_Policy_LockOnEnd" CHECK ("LockOnEnd" IN ('always','never','auto')),
  CONSTRAINT "CK_Policy_Record" CHECK ("Record" IN ('off','optional','forced')),
  CONSTRAINT "CK_Policy_ConsentTimeout" CHECK ("ConsentTimeoutSeconds" BETWEEN 15 AND 600),
  CONSTRAINT "CK_Policy_MaxFile" CHECK ("MaxFileBytes" > 0),
  CONSTRAINT "CK_Policy_MaxSession" CHECK ("MaxSessionMinutes" BETWEEN 5 AND 1440),
  CONSTRAINT "CK_Policy_ConsentOrUnattended" CHECK ("RequireConsent" OR "AllowUnattended")
);
CREATE UNIQUE INDEX "UX_Policy_Name" ON "Policies" (lower("Name"));
CREATE UNIQUE INDEX "UX_Policy_SingleDefault" ON "Policies" ("IsDefault") WHERE "IsDefault";

-- Precedenza: dispositivo > gruppo del dispositivo > policy default.
CREATE TABLE "PolicyAssignments" (
  "Id" uuid PRIMARY KEY,
  "PolicyId" uuid NOT NULL REFERENCES "Policies"("Id") ON DELETE CASCADE,
  "DeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE CASCADE,
  "GroupId" uuid NULL REFERENCES "DeviceGroups"("Id") ON DELETE CASCADE,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_PolicyAssignment_OneScope" CHECK (num_nonnulls("DeviceId","GroupId") = 1)
);
CREATE UNIQUE INDEX "UX_PolicyAssignment_Device" ON "PolicyAssignments" ("DeviceId") WHERE "DeviceId" IS NOT NULL;
CREATE UNIQUE INDEX "UX_PolicyAssignment_Group" ON "PolicyAssignments" ("GroupId") WHERE "GroupId" IS NOT NULL;
CREATE INDEX "IX_PolicyAssignment_Policy" ON "PolicyAssignments" ("PolicyId");

-- Assistenza con codice ----------------------------------------------------------

CREATE TABLE "Invitations" (
  "Id" uuid PRIMARY KEY,
  "Name" text NOT NULL,
  "CreatedBy" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "GroupId" uuid NOT NULL REFERENCES "DeviceGroups"("Id") ON DELETE CASCADE,
  "CodeHash" bytea NOT NULL,                      -- HMAC-SHA256 con chiave server, mai il codice in chiaro
  "LinkTokenHash" bytea NOT NULL,
  "DeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE SET NULL,
  "JoinedAt" timestamptz NULL,
  "ConsumedAt" timestamptz NULL,
  "RevokedAt" timestamptz NULL,
  "ExpiresAt" timestamptz NOT NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_Invitation_Expiry" CHECK ("ExpiresAt" > "CreatedAt"
                                           AND "ExpiresAt" <= "CreatedAt" + interval '24 hours'),
  CONSTRAINT "CK_Invitation_Name" CHECK (length("Name") BETWEEN 1 AND 120)
);
CREATE UNIQUE INDEX "UX_Invitation_Code_Open" ON "Invitations" ("CodeHash")
  WHERE "ConsumedAt" IS NULL AND "RevokedAt" IS NULL;
CREATE UNIQUE INDEX "UX_Invitation_Link" ON "Invitations" ("LinkTokenHash");
CREATE INDEX "IX_Invitation_CreatedBy" ON "Invitations" ("CreatedBy", "CreatedAt" DESC);
CREATE INDEX "IX_Invitation_Group" ON "Invitations" ("GroupId");
CREATE INDEX "IX_Invitation_Device" ON "Invitations" ("DeviceId");
CREATE INDEX "IX_Invitation_Expires" ON "Invitations" ("ExpiresAt");

CREATE TABLE "CodeAttempts" (
  "SourceHash" bytea PRIMARY KEY,                 -- hash dell'identità dispositivo o dell'origine
  "FailedCount" integer NOT NULL DEFAULT 0,
  "LockedUntil" timestamptz NULL,
  "LastFailedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_CodeAttempt_Count" CHECK ("FailedCount" >= 0)
);
CREATE INDEX "IX_CodeAttempt_LastFailed" ON "CodeAttempts" ("LastFailedAt");

-- Sessioni --------------------------------------------------------------------------

CREATE TABLE "Sessions" (
  "Id" uuid PRIMARY KEY,
  "DeviceId" uuid NOT NULL REFERENCES "Devices"("Id") ON DELETE RESTRICT, -- lo storico non si perde
  "OwnerId" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE RESTRICT,
  "Capability" text NOT NULL,
  "Mode" text NOT NULL,
  "State" text NOT NULL DEFAULT 'pending',
  "ConsentState" text NOT NULL DEFAULT 'not_required',
  "ConsentWindowsUser" text NULL,
  "ConsentDecidedAt" timestamptz NULL,
  "PolicyId" uuid NULL REFERENCES "Policies"("Id") ON DELETE SET NULL,
  "PolicyVersion" bigint NOT NULL,
  "InvitationId" uuid NULL REFERENCES "Invitations"("Id") ON DELETE SET NULL,
  "Recorded" boolean NOT NULL DEFAULT false,
  "Comment" text NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "StartedAt" timestamptz NULL,
  "LastHeartbeatAt" timestamptz NULL,
  "ExpiresAt" timestamptz NOT NULL,
  "EndedAt" timestamptz NULL,
  "EndReason" text NULL,
  "UpdatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_Session_Capability" CHECK ("Capability" IN ('remote.view','remote.control')),
  CONSTRAINT "CK_Session_Mode" CHECK ("Mode" IN ('unattended','attended','invitation')),
  CONSTRAINT "CK_Session_State" CHECK ("State" IN (
    'pending','consent_pending','connecting','active','reboot_pending','ended',
    'declined','expired','failed','revocation_pending','revoked')),
  CONSTRAINT "CK_Session_ConsentState" CHECK ("ConsentState" IN ('not_required','pending','granted','declined','expired')),
  CONSTRAINT "CK_Session_EndReason" CHECK ("EndReason" IS NULL OR "EndReason" IN (
    'technician','remote_user','host_lost','viewer_lost','timeout','max_duration',
    'revoked','declined','consent_timeout','error')),
  CONSTRAINT "CK_Session_EndedHasReason" CHECK (("EndedAt" IS NULL) = ("EndReason" IS NULL)),
  CONSTRAINT "CK_Session_Times" CHECK ("ExpiresAt" > "CreatedAt" AND ("EndedAt" IS NULL OR "EndedAt" >= "CreatedAt")),
  CONSTRAINT "CK_Session_InvitationMode" CHECK (("Mode" = 'invitation') = ("InvitationId" IS NOT NULL)),
  CONSTRAINT "CK_Session_Comment" CHECK ("Comment" IS NULL OR length("Comment") <= 2000)
);
CREATE UNIQUE INDEX "UX_Session_OneActiveControl" ON "Sessions" ("DeviceId")
  WHERE "State" IN ('connecting','active','reboot_pending') AND "Capability" = 'remote.control';
CREATE INDEX "IX_Session_Device_Created" ON "Sessions" ("DeviceId", "CreatedAt" DESC);
CREATE INDEX "IX_Session_Owner_Created" ON "Sessions" ("OwnerId", "CreatedAt" DESC);
CREATE INDEX "IX_Session_Created" ON "Sessions" ("CreatedAt" DESC);
CREATE INDEX "IX_Session_Open" ON "Sessions" ("State")
  WHERE "State" IN ('pending','consent_pending','connecting','active','reboot_pending','revocation_pending');
CREATE INDEX "IX_Session_Policy" ON "Sessions" ("PolicyId");
CREATE INDEX "IX_Session_Invitation" ON "Sessions" ("InvitationId");

CREATE TABLE "SessionTickets" (
  "Id" uuid PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "Sessions"("Id") ON DELETE CASCADE,
  "TicketHash" bytea NOT NULL,
  "Channel" text NOT NULL,
  "Side" text NOT NULL,
  "UserId" text NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,  -- lato viewer
  "ExpiresAt" timestamptz NOT NULL,
  "ConsumedAt" timestamptz NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_SessionTicket_Channel" CHECK ("Channel" IN ('session','files')),
  CONSTRAINT "CK_SessionTicket_Side" CHECK ("Side" IN ('host','viewer')),
  CONSTRAINT "CK_SessionTicket_ViewerHasUser" CHECK (("Side" = 'viewer') = ("UserId" IS NOT NULL)),
  CONSTRAINT "CK_SessionTicket_ShortLived" CHECK ("ExpiresAt" > "CreatedAt"
                                                  AND "ExpiresAt" <= "CreatedAt" + interval '2 minutes')
);
CREATE UNIQUE INDEX "UX_SessionTicket_Hash" ON "SessionTickets" ("TicketHash");
CREATE INDEX "IX_SessionTicket_Session" ON "SessionTickets" ("SessionId");
CREATE INDEX "IX_SessionTicket_User" ON "SessionTickets" ("UserId");
CREATE INDEX "IX_SessionTicket_Expires" ON "SessionTickets" ("ExpiresAt");

CREATE TABLE "SessionParticipants" (
  "Id" uuid PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "Sessions"("Id") ON DELETE CASCADE,
  "UserId" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "Role" text NOT NULL,
  "Capability" text NOT NULL,
  "InvitedBy" text NULL REFERENCES "AspNetUsers"("Id") ON DELETE SET NULL,
  "JoinedAt" timestamptz NULL,
  "LeftAt" timestamptz NULL,
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_Participant_Role" CHECK ("Role" IN ('owner','invited')),
  CONSTRAINT "CK_Participant_Capability" CHECK ("Capability" IN ('remote.view','remote.control'))
);
CREATE UNIQUE INDEX "UX_Participant_SessionUser" ON "SessionParticipants" ("SessionId", "UserId");
CREATE UNIQUE INDEX "UX_Participant_OneOwner" ON "SessionParticipants" ("SessionId") WHERE "Role" = 'owner';
CREATE INDEX "IX_Participant_User" ON "SessionParticipants" ("UserId");
CREATE INDEX "IX_Participant_InvitedBy" ON "SessionParticipants" ("InvitedBy");

CREATE TABLE "SessionEvents" (
  "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "Sessions"("Id") ON DELETE CASCADE,
  "ClientEventId" uuid NULL,                      -- idempotenza per messaggi ripetuti dal PC
  "ActorId" text NULL REFERENCES "AspNetUsers"("Id") ON DELETE SET NULL,
  "Source" text NOT NULL,
  "Type" text NOT NULL,
  "Meta" jsonb NOT NULL DEFAULT '{}',             -- mai testo di appunti, chat o tasti
  "At" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "CK_SessionEvent_Source" CHECK ("Source" IN ('technician','host','assist','server')),
  CONSTRAINT "CK_SessionEvent_Type" CHECK ("Type" IN (
    'consent_requested','consent_granted','consent_declined','connected','disconnected',
    'desktop_changed','monitor_switched','reboot','reboot_safe','reconnected','lock','logoff',
    'ctrl_alt_del','black_screen_on','black_screen_off','input_disabled','input_enabled',
    'clipboard_used','chat_used','file_transfer','screenshot','recording_started','recording_stopped',
    'participant_joined','participant_left','note_left','policy_denied','ended','revoked')),
  CONSTRAINT "CK_SessionEvent_MetaSize" CHECK (pg_column_size("Meta") <= 2048)
);
CREATE UNIQUE INDEX "UX_SessionEvent_Client" ON "SessionEvents" ("SessionId", "ClientEventId") WHERE "ClientEventId" IS NOT NULL;
CREATE INDEX "IX_SessionEvent_Session_At" ON "SessionEvents" ("SessionId", "At");
CREATE INDEX "IX_SessionEvent_Actor" ON "SessionEvents" ("ActorId");

CREATE TABLE "FileTransfers" (
  "Id" uuid PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "Sessions"("Id") ON DELETE CASCADE,
  "ActorId" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "Direction" text NOT NULL,
  "FileName" text NOT NULL,
  "SizeBytes" bigint NOT NULL,
  "Sha256" bytea NULL,
  "State" text NOT NULL DEFAULT 'authorized',
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "CompletedAt" timestamptz NULL,
  CONSTRAINT "CK_FileTransfer_Direction" CHECK ("Direction" IN ('to_remote','from_remote')),
  CONSTRAINT "CK_FileTransfer_State" CHECK ("State" IN ('authorized','in_progress','completed','failed','cancelled')),
  CONSTRAINT "CK_FileTransfer_Size" CHECK ("SizeBytes" >= 0),
  CONSTRAINT "CK_FileTransfer_Name" CHECK (length("FileName") BETWEEN 1 AND 260)
);
CREATE INDEX "IX_FileTransfer_Session" ON "FileTransfers" ("SessionId", "CreatedAt");
CREATE INDEX "IX_FileTransfer_Actor" ON "FileTransfers" ("ActorId");

CREATE TABLE "Recordings" (
  "Id" uuid PRIMARY KEY,
  "SessionId" uuid NOT NULL REFERENCES "Sessions"("Id") ON DELETE RESTRICT,
  "Forced" boolean NOT NULL,
  "StoragePath" text NOT NULL,                    -- relativo al volume registrazioni, generato dal server
  "Format" text NOT NULL DEFAULT 'odrec-1',
  "SizeBytes" bigint NOT NULL DEFAULT 0,
  "Sha256" bytea NULL,
  "State" text NOT NULL DEFAULT 'writing',
  "StartedAt" timestamptz NOT NULL DEFAULT now(),
  "EndedAt" timestamptz NULL,
  "DeleteAfter" timestamptz NULL,
  CONSTRAINT "CK_Recording_State" CHECK ("State" IN ('writing','complete','incomplete','deleted')),
  CONSTRAINT "CK_Recording_Size" CHECK ("SizeBytes" >= 0),
  CONSTRAINT "CK_Recording_CompleteHasHash" CHECK ("State" <> 'complete' OR "Sha256" IS NOT NULL)
);
CREATE UNIQUE INDEX "UX_Recording_Path" ON "Recordings" ("StoragePath");
CREATE INDEX "IX_Recording_Session" ON "Recordings" ("SessionId");
CREATE INDEX "IX_Recording_DeleteAfter" ON "Recordings" ("DeleteAfter") WHERE "DeleteAfter" IS NOT NULL;

-- Wake-on-LAN e audit ------------------------------------------------------------------

CREATE TABLE "WakeRequests" (
  "Id" uuid PRIMARY KEY,
  "TargetDeviceId" uuid NOT NULL REFERENCES "Devices"("Id") ON DELETE CASCADE,
  "RelayDeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE SET NULL,
  "RequestedBy" text NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
  "State" text NOT NULL DEFAULT 'queued',
  "CreatedAt" timestamptz NOT NULL DEFAULT now(),
  "SentAt" timestamptz NULL,
  "TargetOnlineAt" timestamptz NULL,
  CONSTRAINT "CK_Wake_State" CHECK ("State" IN ('queued','sent','online','timeout','no_relay')),
  CONSTRAINT "CK_Wake_NotSelf" CHECK ("RelayDeviceId" IS NULL OR "RelayDeviceId" <> "TargetDeviceId")
);
CREATE INDEX "IX_Wake_Target" ON "WakeRequests" ("TargetDeviceId", "CreatedAt" DESC);
CREATE INDEX "IX_Wake_Relay_State" ON "WakeRequests" ("RelayDeviceId", "State");
CREATE INDEX "IX_Wake_RequestedBy" ON "WakeRequests" ("RequestedBy");

CREATE TABLE "AuditEvents" (
  "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "At" timestamptz NOT NULL DEFAULT now(),
  "ActorId" text NULL REFERENCES "AspNetUsers"("Id") ON DELETE SET NULL,
  "ActorDeviceId" uuid NULL REFERENCES "Devices"("Id") ON DELETE SET NULL,
  "Action" text NOT NULL,                         -- es. session.create, policy.update, device.revoke
  "Target" text NOT NULL,
  "Outcome" text NOT NULL,
  "Detail" jsonb NOT NULL DEFAULT '{}',           -- mai segreti, token, codici o password
  CONSTRAINT "CK_Audit_Outcome" CHECK ("Outcome" IN ('success','denied','failed')),
  CONSTRAINT "CK_Audit_DetailSize" CHECK (pg_column_size("Detail") <= 4096)
);
CREATE INDEX "IX_Audit_At" ON "AuditEvents" ("At" DESC);
CREATE INDEX "IX_Audit_Actor" ON "AuditEvents" ("ActorId", "At" DESC);
CREATE INDEX "IX_Audit_ActorDevice" ON "AuditEvents" ("ActorDeviceId");
CREATE INDEX "IX_Audit_Action" ON "AuditEvents" ("Action", "At" DESC);
