namespace OmniDesk.Api.Data;

public sealed class SupportQueue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SiteId { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportQueueMember
{
    public Guid QueueId { get; set; }
    public string UserId { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? AddedBy { get; set; }
}

public sealed class SupportQueueRoutingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SiteId { get; set; }
    public string Category { get; set; } = "general";
    public Guid QueueId { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportWorkflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SiteId { get; set; }
    public string ScopeKey { get; set; } = "global";
    public string Name { get; set; } = "";
    public string DraftDefinitionJson { get; set; } = "";
    public long Version { get; set; } = 1;
    public int ActiveVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportWorkflowVersion
{
    public Guid WorkflowId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public Guid? SiteId { get; set; }
    public string DefinitionJson { get; set; } = "";
    public string PublishedBy { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportSlaPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SiteId { get; set; }
    public string ScopeKey { get; set; } = "global";
    public string Name { get; set; } = "";
    public int CurrentVersion { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportSlaPolicyVersion
{
    public Guid PolicyId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public Guid? SiteId { get; set; }
    public string DefinitionJson { get; set; } = "";
    public string PublishedBy { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportTicketSlaTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public string TargetType { get; set; } = "first_response";
    public Guid PolicyId { get; set; }
    public int PolicyVersion { get; set; }
    public int Cycle { get; set; } = 1;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? BreachedAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public long AccumulatedPauseSeconds { get; set; }
}

public sealed class SupportTicketSlaEscalation
{
    public Guid TargetId { get; set; }
    public int StepMinutes { get; set; }
    public DateTimeOffset EscalatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Number { get; set; }
    public Guid RequestId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid SiteId { get; set; }
    public string RequesterKey { get; set; } = "";
    public string? RequesterUserId { get; set; }
    public string Origin { get; set; } = "device";
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";
    public string State { get; set; } = "open";
    public string Priority { get; set; } = "p3";
    public string Category { get; set; } = "general";
    public Guid? QueueId { get; set; }
    public string? AssigneeId { get; set; }
    public string? Resolution { get; set; }
    public Guid? WorkflowId { get; set; }
    public int? WorkflowVersion { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[]? Screenshot { get; set; }
}

public sealed class PortalDeviceAccess
{
    public string UserId { get; set; } = "";
    public Guid DeviceId { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? GrantedBy { get; set; }
}

public sealed class SupportMessage
{
    public long Id { get; set; }
    public Guid TicketId { get; set; }
    public string? ActorId { get; set; }
    public string Kind { get; set; } = "reply";
    public string Visibility { get; set; } = "public";
    public string Body { get; set; } = "";
    public string? FromState { get; set; }
    public string? ToState { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsImported { get; set; }
}

public sealed class SupportTicketDevice
{
    public Guid TicketId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LinkedBy { get; set; }
    public string DeviceNameSnapshot { get; set; } = "";
    public Guid SiteIdSnapshot { get; set; }
    public string SiteNameSnapshot { get; set; } = "";
}

public sealed class SupportTicketParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public string UserId { get; set; } = "";
    public string? AddedBy { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportEmailIntake
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MailboxAddress { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string SenderAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string BodyText { get; set; } = "";
    public string PayloadSha256 { get; set; } = "";
    public DateTimeOffset MessageReceivedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string State { get; set; } = "pending";
    public DateTimeOffset? QualifiedAt { get; set; }
    public string? QualifiedBy { get; set; }
    public Guid? PromotedTicketId { get; set; }
}

public sealed class SupportAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public string? UploaderId { get; set; }
    public string StorageKey { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string MediaType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string Visibility { get; set; } = "public";
    public string ScanState { get; set; } = "quarantined";
    public int ScanAttempts { get; set; }
    public DateTimeOffset NextScanAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScanLeaseUntil { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
    public string? ScannerVersion { get; set; }
    public string? LastScanErrorType { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportInterventionSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid DeviceId { get; set; }
    public string ArtifactType { get; set; } = "";
    public Guid ArtifactId { get; set; }
    public string Capability { get; set; } = "";
    public string? AuthorId { get; set; }
    public string Summary { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string? FollowUp { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CoreOutboxDelivery
{
    public Guid EventId { get; set; }
    public string ConsumerName { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? DeadLetteredAt { get; set; }
    public string? LastErrorType { get; set; }
}

public sealed class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string UserId { get; set; } = "";
    public Guid TicketId { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
