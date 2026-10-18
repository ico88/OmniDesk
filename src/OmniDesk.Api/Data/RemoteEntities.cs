namespace OmniDesk.Api.Data;
public sealed class MeshDeviceMapping { public Guid DeviceId { get; set; } public string NodeId { get; set; } = ""; }
public sealed class RemoteSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public string ActorId { get; set; } = "";
    public string Capability { get; set; } = "";
    public string State { get; set; } = "pending";
    public string? PublicId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}
