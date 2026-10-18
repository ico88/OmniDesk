namespace OmniDesk.Api.Data;
public sealed class UpdateJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Guid RequestId { get; set; }
    public string RequestedBy { get; set; } = "";
    public string Kind { get; set; } = "";
    public string State { get; set; } = "queued";
    public string Payload { get; set; } = "[]";
    public string? Result { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
