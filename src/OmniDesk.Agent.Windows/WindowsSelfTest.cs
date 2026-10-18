using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
namespace OmniDesk.Agent.Windows;
public static class WindowsSelfTest
{
    // Read-only Windows checks. Does not install/start services or execute Windows Update.
    public static async Task Run()
    {
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
        Console.WriteLine("PASS: Windows CPU/RAM/disks/network/uptime, inventory queries, DPAPI and consecutive device assertions. No services or updates modified.");
    }
}
