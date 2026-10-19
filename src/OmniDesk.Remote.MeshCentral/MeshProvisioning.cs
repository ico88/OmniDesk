using System.Text.Json;
using System.Text.RegularExpressions;
namespace OmniDesk.Remote.MeshCentral;

public sealed record MeshAgentProvisioning(string MeshId, string Settings);
public interface IMeshProvisioningClient
{
    bool Configured { get; }
    Task<MeshAgentProvisioning> Provision(Guid deviceId, CancellationToken ct);
    Task<bool> ContainsNode(string meshId, string nodeId, CancellationToken ct);
}
public sealed partial class MeshCentralClient : IMeshProvisioningClient
{
    public async Task<MeshAgentProvisioning> Provision(Guid deviceId, CancellationToken ct)
    {
        JsonElement info = default;
        var name = "OmniDesk:" + deviceId.ToString("N");
        var groups = await Command(new() { ["action"] = "meshes" }, ct, value => info = value);
        var matches = groups.GetProperty("meshes").EnumerateArray().Where(g => g.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Duplicate managed group");
        string meshId;
        if (matches.Length == 1)
        {
            meshId = matches[0].GetProperty("_id").GetString()!;
            // Enforce consent even when retrying after a partial provisioning operation.
            var edited = await Command(new() { ["action"] = "editmesh", ["meshid"] = meshId, ["consent"] = 63 }, ct);
            if (!edited.TryGetProperty("result",out var result) || !string.Equals(result.GetString(),"ok",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Group policy rejected");
        }
        else
        {
            var created = await Command(new() { ["action"] = "createmesh", ["meshname"] = name, ["meshtype"] = 2, ["consent"] = 63 }, ct);
            if (!created.TryGetProperty("result",out var result) || !string.Equals(result.GetString(),"ok",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Group provisioning rejected");
            meshId = created.GetProperty("meshid").GetString()!;
        }
        if (!Regex.IsMatch(meshId,@"^mesh//[A-Za-z0-9@$+=_-]{64}$")) throw new InvalidOperationException("Unexpected group identifier");
        var mesh = Convert.FromBase64String(meshId[6..].Replace('@','+').Replace('$','/'));
        var certificate = Convert.FromBase64String(info.GetProperty("agentCertHash").GetString()!.Replace('@','+').Replace('$','/'));
        if (mesh.Length != 48 || certificate.Length != 48) throw new InvalidOperationException("Unexpected engine identity");
        var origin = new UriBuilder(options.Origin!) { Scheme = "wss", Path = "/agent.ashx" }.Uri;
        var settings = $"MeshName={name}\r\nMeshType=2\r\nMeshID=0x{Convert.ToHexString(mesh)}\r\nServerID={Convert.ToHexString(certificate)}\r\nMeshServer={origin.AbsoluteUri}\r\nmeshServiceName=OmniDeskRemote\r\ndisableUpdate=1\r\nTag={deviceId:N}\r\n";
        return new(meshId,settings);
    }
    public async Task<bool> ContainsNode(string meshId, string nodeId, CancellationToken ct)
    {
        var reply = await Command(new() { ["action"] = "nodes", ["meshid"] = meshId },ct);
        if (!reply.TryGetProperty("nodes",out var groups) || !groups.TryGetProperty(meshId,out var nodes)) return false;
        var list = nodes.EnumerateArray().ToArray();
        return list.Length == 1 && list[0].GetProperty("_id").GetString() == nodeId &&
            list[0].TryGetProperty("meshid",out var parent) && parent.GetString() == meshId &&
            list[0].TryGetProperty("conn",out var connection) && (connection.GetInt32() & 1) == 1;
    }
}
