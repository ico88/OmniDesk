using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OmniDesk.Api.Data;
using OmniDesk.Contracts;
using Xunit;

namespace OmniDesk.Api.Tests;
public static class AgentScenario
{
    public static async Task Run(ApiFactory factory, HttpClient admin, HttpClient scoped, string scopedId)
    {
        HttpClient Client() => factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var anonymous = Client(); using var key = RSA.Create(3072); using var wrongKey = RSA.Create(2048);
        var groupResponse = await admin.PostAsJsonAsync("/api/admin/groups", new { name = "Enrollment test" });
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var group = (await groupResponse.Content.ReadFromJsonAsync<DeviceGroup>())!.Id;
        async Task<(Guid Id, string Token)> Issue()
        {
            var response = await admin.PostAsJsonAsync("/api/admin/enrollment-tokens", new { groupId = group, expiresInMinutes = 15 });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); return (body.GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
        }
        var token = await Issue();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.EnrollmentTokens.FindAsync(token.Id);
            Assert.NotEqual(token.Token, stored!.TokenHash); Assert.Equal(64, stored.TokenHash.Length);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync("/api/admin/enrollment-tokens", new { expiresInMinutes = 5 })).StatusCode);
        var enrollment = new EnrollmentRequest(token.Token, "Windows fixture", key.ExportSubjectPublicKeyInfoPem());
        var enrolled = await anonymous.PostAsJsonAsync("/api/agent/enroll", enrollment);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        var id = (await enrolled.Content.ReadFromJsonAsync<EnrollmentResponse>())!.DeviceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evt = await db.CoreOutboxEvents.SingleAsync(x => x.EventType == "device.enrolled" && x.AggregateId == id);
            Assert.Contains(id.ToString(), evt.Payload);
            Assert.DoesNotContain(key.ExportSubjectPublicKeyInfoPem(), evt.Payload);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/agent/enroll", enrollment)).StatusCode);
        var revokedToken = await Issue();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/enrollment-tokens/{revokedToken.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/agent/enroll", enrollment with { Token = revokedToken.Token })).StatusCode);
        var expired = await Issue();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EnrollmentTokens.FindAsync(expired.Id); row!.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/agent/enroll", enrollment with { Token = expired.Token })).StatusCode);
        // Two independent clients race to consume one token: exactly one successful enrollment.
        var race = await Issue(); using var racer = Client();
        var raced = await Task.WhenAll(anonymous.PostAsJsonAsync("/api/agent/enroll", enrollment with { Token = race.Token }), racer.PostAsJsonAsync("/api/agent/enroll", enrollment with { Token = race.Token }));
        Assert.Single(raced, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(raced, x => x.StatusCode == HttpStatusCode.Unauthorized);
        using var agent = Client();
        string Assertion(RSA rsa, DateTime? issued = null, DateTime? expires = null)
        {
            var now = issued ?? DateTime.UtcNow;
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
                Issuer = id.ToString(), Audience = "omnidesk-agent", IssuedAt = now, NotBefore = now.AddSeconds(-5), Expires = expires ?? now.AddSeconds(60),
                Claims = new Dictionary<string, object> { ["sub"] = id.ToString(), ["jti"] = Guid.NewGuid().ToString() },
                SigningCredentials = new(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
            });
        }
        var metrics = new MetricsRequest(42.5, 16_000_000_000, 8_000_000_000, 100, 200, 3600, [new("C:\\", 100000, 50000)]);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(wrongKey));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-9)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key, expires: DateTime.UtcNow.AddHours(1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics with { AvailableMemoryBytes = long.MaxValue })).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PutAsJsonAsync("/api/agent/inventory", new InventoryRequest("Windows fixture", "X64", "Fixture", "Test", null, "Test CPU", 8, [new("Test app", "1.0", "Test")], []))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync($"/api/devices/{id}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Viewer", groupId = group, deviceId = (Guid?)null })).StatusCode);
        var status = await scoped.GetFromJsonAsync<JsonElement>($"/api/devices/{id}/status");
        Assert.True(status.GetProperty("online").GetBoolean());
        Assert.Equal(42.5, status.GetProperty("metrics").GetProperty("cpuPercent").GetDouble());
        Assert.Equal("Test app", status.GetProperty("inventory").GetProperty("software")[0].GetProperty("name").GetString());
        await RemoteProvisioningScenario.Run(factory, admin, scoped, agent, () => Assertion(key), id);
        await SupportScenario.Run(factory, admin, scoped, agent, () => Assertion(key), id);
        await UpdateScenario.Run(admin, scoped, agent, scopedId, () => Assertion(key), id, group);
        await RemoteAgentUpdateScenario.Run(factory, admin, scoped, agent, () => Assertion(key), id);
        await DeviceManagementScenario.Run(factory, admin, scoped, scopedId, agent, () => Assertion(key), id, group);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/devices/{id}/revoke", null)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        status = await scoped.GetFromJsonAsync<JsonElement>($"/api/devices/{id}/status");
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/agent/support/" + new string('A',64))).StatusCode);
        Assert.True(status.GetProperty("revoked").GetBoolean()); Assert.False(status.GetProperty("online").GetBoolean());
    }
}
