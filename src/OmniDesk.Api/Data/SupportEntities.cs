namespace OmniDesk.Api.Data;

public sealed class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid DeviceId { get; set; }
    public string RequesterKey { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";
    public string State { get; set; } = "open";
    public string? Resolution { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[]? Screenshot { get; set; }
}

public sealed class SupportMessage
{
    public long Id { get; set; }
    public Guid TicketId { get; set; }
    public string? ActorId { get; set; }
    public string Kind { get; set; } = "reply";
    public string Body { get; set; } = "";
    public string? FromState { get; set; }
    public string? ToState { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsImported { get; set; }
}
