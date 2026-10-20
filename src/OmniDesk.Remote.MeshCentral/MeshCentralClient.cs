using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
namespace OmniDesk.Remote.MeshCentral;
public sealed record MeshSession(string PublicId, string Url, DateTimeOffset ExpiresAt);
public sealed record MeshCentralOptions(string? Origin, string? Username, string? PasswordFile, int SessionMinutes = 30);
public interface IMeshCentralClient
{
    bool Configured { get; }
    Task<MeshSession> Create(string nodeId, string capability, string actor, bool consentRequired, CancellationToken cancellationToken);
    Task Disconnect(string nodeId, string publicId, string guestName, CancellationToken cancellationToken);
    Task Revoke(string nodeId, string publicId, CancellationToken cancellationToken);
}
public sealed partial class MeshCentralClient(MeshCentralOptions options) : IMeshCentralClient
{
    public TimeSpan SessionLifetime => TimeSpan.FromMinutes(options.SessionMinutes);
    public bool Configured => Uri.TryCreate(options.Origin, UriKind.Absolute, out var origin) && origin.Scheme == "https" &&
        origin.AbsolutePath == "/" && string.IsNullOrEmpty(origin.UserInfo) && string.IsNullOrEmpty(origin.Query) &&
        string.IsNullOrEmpty(origin.Fragment) && !string.IsNullOrWhiteSpace(options.Username) && File.Exists(options.PasswordFile) &&
        options.SessionMinutes is > 0 and <= 30;
    public async Task<MeshSession> Create(string nodeId, string capability, string actor, bool consentRequired, CancellationToken cancellationToken)
    {
        // Exactly one protocol. Consent policy comes only from the authorized server; no administrative UI or broad account handed to browser.
        var protocol = capability switch { "remote.control" => 2, "terminal" => 1, "files" => 4, _ => throw new ArgumentException("Unsupported remote capability") };
        var consent = SessionConsent(capability, consentRequired);
        var expires = DateTimeOffset.UtcNow + SessionLifetime;
        var reply = await Command(new Dictionary<string, object> {
            ["action"] = "createDeviceShareLink", ["nodeid"] = nodeId, ["guestname"] = actor[..Math.Min(actor.Length, 80)],
            ["p"] = protocol, ["consent"] = consent, ["start"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["end"] = expires.ToUnixTimeSeconds(), ["viewOnly"] = false
        }, cancellationToken);
        if (!reply.TryGetProperty("result", out var result) || result.GetString() != "OK") throw new InvalidOperationException("MeshCentral refused session creation");
        var url = reply.GetProperty("url").GetString()!; var origin = new Uri(options.Origin!);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var destination) || destination.Scheme != "https" ||
            destination.Host != origin.Host || destination.Port != origin.Port || !string.IsNullOrEmpty(destination.UserInfo) || destination.AbsolutePath != "/sharing")
            throw new InvalidOperationException("MeshCentral returned an unexpected session origin");
        return new(reply.GetProperty("publicid").GetString()!, url, expires);
    }
    internal static int SessionConsent(string capability, bool required) =>
        // Desktop always requests the visible connection bar (64), independently of consent.
        (capability switch { "remote.control" => 65, "terminal" => 2, "files" => 4, _ => throw new ArgumentException("Unsupported remote capability") }) |
        (required ? capability switch { "remote.control" => 8, "terminal" => 16, _ => 32 } : 0);
    public async Task Revoke(string nodeId, string publicId, CancellationToken cancellationToken)
    {
        var reply = await Command(new Dictionary<string, object> { ["action"] = "removeDeviceShare", ["nodeid"] = nodeId, ["publicid"] = publicId }, cancellationToken);
        if (!reply.TryGetProperty("result", out var result) ||
            (result.GetString() != "OK" && result.GetString() != "Invalid device share identifier."))
            throw new InvalidOperationException("MeshCentral refused session revocation");
    }
    public async Task Disconnect(string nodeId, string publicId, string guestName, CancellationToken cancellationToken)
    {
        // MeshCentral's desktop endsession command targets a share by its owner and guest name.
        // Prefer the server record; MeshCentral purges expired shares, so use the stored OmniDesk
        // guest name and configured service identity when the record is already gone.
        var reply = await Command(new Dictionary<string, object> { ["action"] = "deviceShares", ["nodeid"] = nodeId }, cancellationToken);
        var identity = MeshDisconnectIdentity.Resolve(reply.GetProperty("deviceShares"), publicId, options.Username ?? "", guestName);

        await Command(new Dictionary<string, object> {
            ["action"] = "endDesktopMultiplex", ["nodeid"] = nodeId,
            ["xuserid"] = identity.OwnerId, ["guestname"] = identity.GuestName
        }, cancellationToken, waitForResponse: false);
    }
    private async Task<JsonElement> Command(Dictionary<string, object> command, CancellationToken cancellationToken, Action<JsonElement>? serverInfo = null, bool waitForResponse = true)
    {
        if (!Configured) throw new InvalidOperationException("MeshCentral is not configured");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var socket = new ClientWebSocket();
        var password = (await File.ReadAllTextAsync(options.PasswordFile!, timeout.Token)).Trim();
        socket.Options.SetRequestHeader("x-meshauth", Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Username!)) + "," + Convert.ToBase64String(Encoding.UTF8.GetBytes(password)));
        var origin = new UriBuilder(options.Origin!) { Scheme = "wss", Path = "/control.ashx" };
        await socket.ConnectAsync(origin.Uri, timeout.Token); // Platform certificate validation stays enabled.
        var responseId = Guid.NewGuid().ToString(); command["responseid"] = responseId; command["tag"] = responseId;
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(command), WebSocketMessageType.Text, true, timeout.Token);
        if (!waitForResponse) { using var empty = JsonDocument.Parse("{}"); return empty.RootElement.Clone(); }
        var buffer = new byte[8192];
        while (true)
        {
            using var bytes = new MemoryStream(); WebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(buffer, timeout.Token);
                if (part.MessageType != WebSocketMessageType.Text) throw new InvalidOperationException("MeshCentral closed connection");
                bytes.Write(buffer, 0, part.Count); if (bytes.Length > 1024 * 1024) throw new InvalidOperationException("MeshCentral response too large");
            } while (!part.EndOfMessage);
            using var json = JsonDocument.Parse(bytes.ToArray());
            if (json.RootElement.TryGetProperty("action",out var action) && action.GetString() == "serverinfo" && json.RootElement.TryGetProperty("serverinfo",out var info)) serverInfo?.Invoke(info.Clone());
            if (command["action"].ToString() == "meshes" && json.RootElement.TryGetProperty("action",out var kind) && kind.GetString() == "meshes" &&
                json.RootElement.TryGetProperty("tag",out var tag) && tag.GetString() == responseId) return json.RootElement.Clone();
            if (command["action"].ToString() == "deviceShares" && json.RootElement.TryGetProperty("action", out var sharesAction) &&
                sharesAction.GetString() == "deviceShares" && json.RootElement.TryGetProperty("nodeid", out var sharesNode) &&
                sharesNode.GetString() == command["nodeid"].ToString()) return json.RootElement.Clone();
            if (json.RootElement.TryGetProperty("responseid", out var id) && id.GetString() == responseId)
            {
                // MeshCentral first reports the removed document (possibly null), then sends the
                // final result after its database removal callback has run.
                if (command["action"].ToString() == "removeDeviceShare" &&
                    (!json.RootElement.TryGetProperty("result", out var removeResult) || removeResult.ValueKind != JsonValueKind.String)) continue;
                return json.RootElement.Clone();
            }
        }
    }
}
