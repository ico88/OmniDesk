using System.Text.Json;

namespace OmniDesk.Remote.MeshCentral;

public sealed record MeshDisconnectIdentity(string OwnerId, string GuestName)
{
    public static MeshDisconnectIdentity Resolve(JsonElement shares, string publicId, string serviceUsername, string guestName)
    {
        if (shares.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("MeshCentral did not return device shares");
        var matches = shares.EnumerateArray().Where(x =>
            x.ValueKind == JsonValueKind.Object && x.TryGetProperty("publicid", out var id) && id.GetString() == publicId).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("MeshCentral returned duplicate share identities");
        var share = matches.SingleOrDefault();
        var ownerId = share.ValueKind == JsonValueKind.Object && share.TryGetProperty("userid", out var userId)
            ? userId.GetString()
            : null;
        var resolvedGuestName = share.ValueKind == JsonValueKind.Object && share.TryGetProperty("guestName", out var shareGuestName)
            ? shareGuestName.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(ownerId))
            ownerId = !string.IsNullOrWhiteSpace(serviceUsername) && !serviceUsername.Contains('/') ? $"user//{serviceUsername}" : null;
        if (string.IsNullOrWhiteSpace(resolvedGuestName)) resolvedGuestName = guestName;
        if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(resolvedGuestName))
            throw new InvalidOperationException("MeshCentral session identity is unavailable");
        return new(ownerId, resolvedGuestName);
    }
}
