using OmniDesk.Contracts;

namespace OmniDesk.Agent.Windows;

public sealed class AgentRuntimeState
{
    private readonly object sync = new();
    private string state = "starting";
    private Guid? device;
    private DateTimeOffset? lastHeartbeat;
    public void Identity(Guid? id)
    {
        lock (sync) { if (device != id) lastHeartbeat = null; device = id; state = id is null ? "not_enrolled" : "connecting"; }
    }
    public void Heartbeat() { lock (sync) { lastHeartbeat = DateTimeOffset.UtcNow; state = "online"; } }
    public void Failure(string value) { lock (sync) state = value == "offline" && lastHeartbeat > DateTimeOffset.UtcNow.AddMinutes(-3) ? "degraded" : value; }
    public TrayStatus Snapshot(Guid requestId)
    {
        lock (sync)
        {
            var current = (state is "online" or "degraded") && lastHeartbeat < DateTimeOffset.UtcNow.AddMinutes(-3) ? "offline" : state;
            return new(TrayProtocol.Version, requestId, current, lastHeartbeat, device is not null);
        }
    }
}
