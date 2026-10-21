using System.Text.Json;

namespace OmniDesk.Agent.Windows;

internal sealed record AgentHealthReceipt(string Version, DateTimeOffset HeartbeatAt);

internal static class AgentUpdateHealth
{
    private static readonly string ReceiptPath = Path.Combine(AgentIdentity.Folder, "agent-health.json");
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal static void RecordHeartbeat()
    {
        var version = typeof(AgentUpdateHealth).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        AgentIdentity.WriteTrusted(ReceiptPath, JsonSerializer.Serialize(new AgentHealthReceipt(version, DateTimeOffset.UtcNow)));
    }

    internal static AgentHealthReceipt? Read()
    {
        if (!File.Exists(ReceiptPath)) return null;
        AgentIdentity.AssertTrusted(new DirectoryInfo(AgentIdentity.Folder));
        AgentIdentity.AssertTrusted(new FileInfo(ReceiptPath));
        return JsonSerializer.Deserialize<AgentHealthReceipt>(File.ReadAllText(ReceiptPath), Json);
    }
}
