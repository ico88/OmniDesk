using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniDesk.Contracts;

public sealed record TrayRequest(int Version, Guid RequestId, string Method, SupportCreate? Ticket = null);
public sealed record TrayStatus(int Version, Guid RequestId, string State, DateTimeOffset? LastHeartbeat, bool Enrolled);

public static class TrayProtocol
{
    public const int Version = 1;
    public const int MaxPayload = 4096;
    public const int MaxSupportPayload = 750000;
    public const string CreateTicketMethod = "CreateTicket";
    public const string ListTicketsMethod = "ListTickets";
    public const string StatusMethod = "GetConnectionStatus";
    public static string PipeName(int sessionId) => sessionId > 0 ? $"OmniDesk.Tray.v1.{sessionId}" : throw new ArgumentOutOfRangeException(nameof(sessionId));
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    public static bool Valid(TrayRequest request) => request.Version == Version && request.RequestId != Guid.Empty && ((request.Method is StatusMethod or ListTicketsMethod && request.Ticket is null) ||
        (request.Method == CreateTicketMethod && request.Ticket is not null));
    public static bool Valid(TrayStatus status, Guid requestId) => status.Version == Version && status.RequestId == requestId &&
        status.State is "starting" or "not_enrolled" or "connecting" or "online" or "offline" or "degraded" or "auth_rejected";

    public static async Task Write<T>(Stream stream, T value, CancellationToken ct, int maxPayload = MaxPayload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length < 1 || bytes.Length > maxPayload) throw new InvalidDataException("IPC payload size rejected.");
        var length = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken ct, int maxPayload = MaxPayload)
    {
        var length = new byte[4]; await stream.ReadExactlyAsync(length, ct);
        var size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size < 1 || size > maxPayload) throw new InvalidDataException("IPC payload size rejected.");
        var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, ct);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("IPC object required.");
        static void CheckProperties(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in value.EnumerateObject())
                {
                    if (!names.Add(field.Name)) throw new InvalidDataException("Duplicate IPC property.");
                    CheckProperties(field.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) CheckProperties(item);
        }
        CheckProperties(json.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, Options) ?? throw new InvalidDataException("Missing IPC object.");
    }
}
