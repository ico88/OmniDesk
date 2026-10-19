namespace OmniDesk.Api.Data;
public sealed class MeshDeviceMapping { public Guid DeviceId { get; set; } public string NodeId { get; set; } = ""; }
public sealed class RemoteSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public string NodeId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Capability { get; set; } = "";
    public string State { get; set; } = "pending";
    public bool ConsentRequired { get; set; } = true;
    public long AccessPolicyVersion { get; set; }
    public string? PublicId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class RemoteProvisioning
{
    public Guid DeviceId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProtectedSettings { get; set; } = "";
    public string ProtectedMeshId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Enabled { get; set; } = true;
    public bool GroupRemoved { get; set; }
    public bool UnattendedAllowed { get; set; }
    public long AccessPolicyVersion { get; set; }
}
