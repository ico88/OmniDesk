using System.Net.Http;

namespace OmniDesk.Agent.Windows;

internal sealed record RemoteAgentFailure(string Code, string Guidance)
{
    public static RemoteAgentFailure Classify(Exception error) => error switch
    {
        UnauthorizedAccessException => new("binary_access_denied", "The remote engine failed its ACL or hash check, or Windows denied access. Check Windows Security Protection history and repair the signed, verified Agent package if needed."),
        FileNotFoundException => new("binary_missing", "The remote engine file or service is missing. Check Windows Security Protection history and repair the verified Agent package."),
        HttpRequestException => new("server_unreachable", "The Agent could not reach the OmniDesk server. Check the configured server URL and network connectivity."),
        TimeoutException or TaskCanceledException => new("server_timeout", "The remote engine request timed out. Check connectivity to the OmniDesk server and MeshCentral."),
        System.ComponentModel.Win32Exception { NativeErrorCode: 1060 } => new("service_missing", "The Windows remote engine service is not installed. Repair the verified Agent package."),
        _ => new("provisioning_unavailable", "Remote provisioning did not complete. Check the OmniDesk Agent Application event log and Windows service state.")
    };
}
