using System.Security.Cryptography;
using System.Diagnostics;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OmniDesk.Contracts;
namespace OmniDesk.Agent.Windows;
public static class WindowsSelfTest
{
    // Read-only Windows checks. Does not install/start services or execute Windows Update.
    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        if (!ProcessControlPolicy.IsControllable(12345, "notepad.exe", now) ||
            ProcessControlPolicy.IsControllable(4, "notepad.exe", now) ||
            ProcessControlPolicy.IsControllable(12345, "lsass.exe", now) ||
            ProcessControlPolicy.IsControllable(12345, "MeshService64.exe", now) ||
            ProcessControlPolicy.IsControllable(12345, "OmniDesk.Agent.Windows.exe", now) ||
            ProcessControlPolicy.IsControllable(12345, "notepad.exe", default))
            throw new InvalidOperationException("Process control safety policy failed");
        var currentProcess = DiagnosticCollector.Collect("diagnostics.processes").Processes?.SingleOrDefault(x => x.ProcessId == Environment.ProcessId);
        if (currentProcess is null || currentProcess.CanTerminate)
            throw new InvalidOperationException("Process inventory did not protect the running OmniDesk agent");
        var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        using (var child = Process.Start(new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" } })
               ?? throw new InvalidOperationException("Unable to start isolated process-control self-test fixture"))
        {
            var startedAt = new DateTimeOffset(child.StartTime.ToUniversalTime());
            var fixture = new UpdateLease(Guid.NewGuid(), Guid.NewGuid(), "process.kill", [], DateTimeOffset.UtcNow.AddMinutes(1),
                ProcessControl: new ProcessControlPayload(child.Id, child.ProcessName + ".exe", startedAt));
            var result = UpdateWorker.ExecuteProcessControl(fixture);
            if (!result.Succeeded || !child.WaitForExit(5000)) throw new InvalidOperationException("Process control did not terminate the matching test child");
        }
        using (var windowsIdentity = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            var logonSid = OmniDesk.Windows.Ipc.TokenLogonSid.Read(windowsIdentity.AccessToken);
            if (windowsIdentity.Groups?.Contains(logonSid) == true)
                throw new InvalidOperationException("Expected Groups to filter the logon SID.");
            Console.WriteLine("PASS: native token logon SID read; filtered WindowsIdentity.Groups is not used for session authorization.");
        }
        var collector = new WindowsCollector(); _ = collector.Metrics(); await Task.Delay(1200);
        var metrics = collector.Metrics(); var inventory = collector.Inventory();
        if (metrics.CpuPercent is null || !double.IsFinite(metrics.CpuPercent.Value) || metrics.CpuPercent < 0 || metrics.CpuPercent > 100 ||
            metrics.TotalMemoryBytes <= 0 || metrics.AvailableMemoryBytes > metrics.TotalMemoryBytes || metrics.UptimeSeconds < 0 ||
            string.IsNullOrWhiteSpace(inventory.OperatingSystem) || inventory.LogicalProcessors < 1 || metrics.NetworkAdapters is null ||
            metrics.NetworkAdapters.Length > 32 || metrics.NetworkAdapters.Any(adapter =>
                string.IsNullOrWhiteSpace(adapter.Name) || adapter.Name.Length > 256 || string.IsNullOrWhiteSpace(adapter.Type) || adapter.Type.Length > 64 ||
                adapter.MacAddress is null || adapter.MacAddress.Length > 32 || adapter.MacAddress.Any(ch => !Uri.IsHexDigit(ch)) ||
                adapter.IpAddresses is null || adapter.IpAddresses.Length > 32 || adapter.IpAddresses.Any(address =>
                    address is null || address.Length > 64 || !System.Net.IPAddress.TryParse(address, out _))))
            throw new InvalidOperationException("Invalid Windows collector result");
        using var rsa = RSA.Create(3072);
        var identity = AgentIdentity.Create("https://localhost", Guid.NewGuid(), rsa);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = identity.Assertion();
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters {
                IssuerSigningKey = new RsaSecurityKey(rsa), ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidIssuer = identity.DeviceId.ToString(), ValidAudience = "omnidesk-agent", ValidateLifetime = true
            });
            if (!result.IsValid) throw new InvalidOperationException("DPAPI/JWT Windows round-trip failed");
        }
        var directory = Path.Combine(Path.GetTempPath(), "omnidesk-acl-" + Guid.NewGuid().ToString("N"));
        try
        {
            AgentIdentity.SecureDirectory(directory); var file = Path.Combine(directory, "fixture.json");
            AgentIdentity.WriteTrusted(file, "{}"); AgentIdentity.AssertTrusted(new DirectoryInfo(directory)); AgentIdentity.AssertTrusted(new FileInfo(file));
            var info = new DirectoryInfo(directory); var acl = System.IO.FileSystemAclExtensions.GetAccessControl(info);
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null), System.Security.AccessControl.FileSystemRights.Read, System.Security.AccessControl.AccessControlType.Allow));
            System.IO.FileSystemAclExtensions.SetAccessControl(info, acl);
            var denied = false; try { AgentIdentity.AssertTrusted(info); } catch (UnauthorizedAccessException) { denied = true; }
            if (!denied) throw new InvalidOperationException("Unsafe agent ACL was accepted");
            AgentIdentity.SecureDirectory(directory);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive:true); }
        Console.WriteLine("PASS: process termination policy and PID/name/start-time matching; Windows CPU/RAM/disks/network/uptime, inventory queries, DPAPI, consecutive device assertions and trusted file ACLs. Only a disposable PowerShell test process was terminated; no services or updates modified.");
    }
}
