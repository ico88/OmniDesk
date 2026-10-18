namespace OmniDesk.Api.Data;
public sealed class EnrollmentToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = "";
    public Guid? GroupId { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class DeviceCredential
{
    public Guid DeviceId { get; set; }
    public string PublicKeyPem { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class DeviceAssertion
{
    public Guid Id { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class DeviceMetrics
{
    public Guid DeviceId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string Payload { get; set; } = "{}";
}
public sealed class DeviceInventory
{
    public Guid DeviceId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string Payload { get; set; } = "{}";
}
