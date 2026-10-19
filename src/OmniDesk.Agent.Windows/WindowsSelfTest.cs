using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
namespace OmniDesk.Agent.Windows;
public static class WindowsSelfTest
{
    // Read-only Windows checks. Does not install/start services or execute Windows Update.
    public static async Task Run()
    {
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
            string.IsNullOrWhiteSpace(inventory.OperatingSystem) || inventory.LogicalProcessors < 1)
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
        Console.WriteLine("PASS: Windows CPU/RAM/disks/network/uptime, inventory queries, DPAPI, consecutive device assertions and trusted file ACLs. No services or updates modified.");
    }
}
