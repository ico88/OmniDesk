namespace OmniDesk.Contracts;

public sealed record RemoteAgentConfiguration(Guid Id, string Settings, DateTimeOffset AuthorizedUntil);
public sealed record RemoteNodeReport(Guid ConfigurationId, string NodeId);
public static class RemoteAgentPackage
{
    public const string Sha256 = "cbf10a37fc01f74f2473aeca0a4b391dca6f9d0a8d0300aa7181dbce7bacc51e";
    public const string ServiceName = "OmniDeskRemote";
    public const string ExecutableName = "MeshService64.exe";
}
