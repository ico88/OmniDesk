using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniDesk.Api.Data;
using OmniDesk.Contracts;
using Xunit;

namespace OmniDesk.Api.Tests;

public static class RemoteAgentUpdateScenario
{
    public static async Task Run(ApiFactory factory, HttpClient admin, HttpClient scoped, HttpClient agent, Func<string> assertion, Guid device)
    {
        var directory = Path.Combine(Path.GetTempPath(), "omnidesk-agent-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configuration = factory.Services.GetRequiredService<IConfiguration>(); var previous = configuration["Downloads:Directory"];
        try
        {
            var msi = Convert.FromHexString("D0CF11E0A1B11AE1").Concat("isolated MSI fixture"u8.ToArray()).ToArray();
            const string version = "0.8.2";
            var packagePath = Path.Combine(directory, $"OmniDesk.Windows.LAN-{version}.zip");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            await using (var output = archive.CreateEntry("OmniDesk.Agent.msi").Open()) await output.WriteAsync(msi);
            var zip = await File.ReadAllBytesAsync(packagePath); var zipHash = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
            var msiHash = Convert.ToHexString(SHA256.HashData(msi)).ToLowerInvariant();
            await File.WriteAllTextAsync(Path.Combine(directory, "windows-release.json"), JsonSerializer.Serialize(new
            { version, commit = new string('a', 40), sha256 = zipHash, size = zip.Length, fileName = Path.GetFileName(packagePath), remoteEngineIncluded = false, msiSha256 = msiHash, msiSize = msi.Length }));
            configuration["Downloads:Directory"] = directory;
            async Task SetAgentVersion(string version)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var metric = await db.DeviceMetrics.FindAsync(device); var old = JsonSerializer.Deserialize<MetricsRequest>(metric!.Payload)!;
                metric.Payload = JsonSerializer.Serialize(old with { AgentVersion = version }); await db.SaveChangesAsync();
            }
            await SetAgentVersion("0.8.0");
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/devices/{device}/agent-update", new AgentUpdateRequest(Guid.NewGuid()))).StatusCode);
            await SetAgentVersion("0.8.1");
            Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync($"/api/admin/devices/{device}/agent-update", new AgentUpdateRequest(Guid.NewGuid()))).StatusCode);
            var queued = await admin.PostAsJsonAsync($"/api/admin/devices/{device}/agent-update", new AgentUpdateRequest(Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Created, queued.StatusCode);
            agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
            var claimed = await agent.PostAsync("/api/agent/jobs/claim", null); Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
            var lease = (await claimed.Content.ReadFromJsonAsync<UpdateLease>())!;
            Assert.Equal("agent.update", lease.Kind); Assert.Equal(version, lease.AgentUpdate!.Version); Assert.Equal(msiHash, lease.AgentUpdate.Sha256);
            agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
            var downloaded = await agent.GetAsync($"/api/agent/jobs/{lease.Id}/package?leaseId={lease.LeaseId}");
            Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode); Assert.Equal(msi, await downloaded.Content.ReadAsByteArrayAsync());
            agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
            Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"/api/agent/jobs/{lease.Id}/package?leaseId={Guid.NewGuid()}")).StatusCode);
            agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
            Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync($"/api/agent/jobs/{lease.Id}/result", new UpdateResultRequest(
                lease.LeaseId, false, false, [], "fixture complete", FailureCategory: "invented_category", TargetVersion: version))).StatusCode);
            agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
            Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync($"/api/agent/jobs/{lease.Id}/result", new UpdateResultRequest(
                lease.LeaseId, false, false, [], "fixture complete", FailureCategory: "download_failed", TargetVersion: new string('9', 65)))).StatusCode);
            agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
            Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync($"/api/agent/jobs/{lease.Id}/result", new UpdateResultRequest(
                lease.LeaseId, false, false, [], "fixture complete", FailureCategory: "download_failed", TargetVersion: version, RollbackState: "not_started"))).StatusCode);
        }
        finally
        {
            configuration["Downloads:Directory"] = previous;
            Directory.Delete(directory, true);
        }
    }
}
