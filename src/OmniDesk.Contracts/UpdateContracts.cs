namespace OmniDesk.Contracts;
public sealed record UpdateSelection(Guid UpdateId, int Revision);
public sealed record UpdateJobRequest(Guid RequestId, UpdateSelection[]? Updates = null);
public sealed record AgentUpdatePackage(string Version, string Sha256, long Size);
public sealed record AgentUpdateRequest(Guid RequestId);
public sealed record UpdateLease(Guid Id, Guid LeaseId, string Kind, UpdateSelection[] Updates, DateTimeOffset LeaseExpiresAt,
    AgentUpdatePackage? AgentUpdate = null, ServiceControlPayload? ServiceControl = null);
public sealed record UpdateItem(Guid UpdateId, int Revision, string Title, string[] Kb, bool Downloaded, int? ResultCode = null);
public sealed record UpdateResultRequest(Guid LeaseId, bool Succeeded, bool RebootRequired, UpdateItem[] Updates, string? Error = null, string? DiagnosticJson = null);
public sealed record DiagnosticRequest(Guid RequestId);
public sealed record ServiceControlPayload(string ServiceName, string Action);
public sealed record ServiceControlRequest(Guid RequestId, string ServiceName, string Action);
public static class ServiceControlPolicy
{
    private static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "OmniDeskAgent", "OmniDeskRemote", "RpcSs", "RpcEptMapper", "DcomLaunch", "EventLog",
        "SamSs", "LSM", "PlugPlay", "Power", "WinDefend", "WdNisSvc", "SecurityHealthService",
        "MpsSvc", "BFE", "wscsvc", "LanmanWorkstation", "LanmanServer", "Dhcp", "Dnscache",
        "NlaSvc", "nsi", "TermService"
    };

    public static bool IsValidServiceName(string? name) =>
        name is { Length: > 0 and <= 256 } && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

    public static bool IsValidAction(string? action) => action is "start" or "stop" or "restart";
    public static bool IsControllable(string? name) => IsValidServiceName(name) && !ProtectedServices.Contains(name!);
}
public sealed record DiagnosticLease(Guid JobId, Guid LeaseId, string Kind);
public sealed record ProcessDiagnostic(int ProcessId, string Name);
public sealed record ServiceDiagnostic(string Name, string DisplayName, string State, string StartType);
public sealed record EventDiagnostic(string Log, DateTime TimeCreated, int Id, string Level, string Source, string Message);
public sealed record DiagnosticResult(string Kind, DateTimeOffset CollectedAt, ProcessDiagnostic[]? Processes = null, ServiceDiagnostic[]? Services = null, EventDiagnostic[]? Events = null);
