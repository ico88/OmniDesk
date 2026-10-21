using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var adapters = new[] { new NetworkAdapterItem("Wi-Fi", "Wireless80211", "AABBCCDDEEFF", ["192.168.1.50", "fe80::1"]), new NetworkAdapterItem("Ethernet", "Ethernet", "001122334455", ["10.0.0.20"]) };
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        var smartDisks = new[] { new SmartDiskItem("Fixture NVMe", "no_failure_predicted") };
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics with { NetworkAdapters = adapters, SmartDisks = smartDisks })).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics with { SmartDisks = [new("Fixture NVMe", "healthy")] })).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics with { NetworkAdapters = [new("Wi-Fi", "Wireless80211", "invalid", ["not-an-ip"])] })).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        var defender = new DefenderSnapshotRequest(DateTimeOffset.UtcNow, "available", true, true, false, "1.1", "1.2",
            DateTimeOffset.UtcNow.AddHours(-2), null, null, "unknown", true, false, [new("path", "C:\\OmniDesk\\Cache")]);
        var inventory = new InventoryRequest("Windows fixture", "X64", "Fixture", "Test", "SN-TEST-123", "Test CPU", 8, [new("Test app", "1.0", "Test")], [], defender);
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PutAsJsonAsync("/api/agent/inventory", inventory)).StatusCode);
        var defenderResponse = await admin.GetFromJsonAsync<JsonElement>($"/api/devices/{id}/security/defender");
        Assert.Equal("available", defenderResponse.GetProperty("snapshot").GetProperty("providerState").GetString());
        Assert.Equal("C:\\OmniDesk\\Cache", defenderResponse.GetProperty("snapshot").GetProperty("exclusions")[0].GetProperty("value").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync($"/api/devices/{id}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Viewer", groupId = group, deviceId = (Guid?)null })).StatusCode);
        var status = await scoped.GetFromJsonAsync<JsonElement>($"/api/devices/{id}/status");
        Assert.True(status.GetProperty("online").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("inventory").GetProperty("defender").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync($"/api/devices/{id}/security/defender")).StatusCode);
        var attention = await admin.GetFromJsonAsync<JsonElement[]>("/api/dashboard/device-attention");
        var attentionDevice = Assert.Single(attention!, item => item.GetProperty("id").GetGuid() == id);
        Assert.Contains(attentionDevice.GetProperty("defenderIssues").EnumerateArray(), issue => issue.GetString() == "Defender: Tamper Protection disattivata");
        Assert.Contains(attentionDevice.GetProperty("defenderIssues").EnumerateArray(), issue => issue.GetString() == "Aggiornamenti Windows non verificati");
        var scopedAttention = await scoped.GetFromJsonAsync<JsonElement[]>("/api/dashboard/device-attention");
        Assert.DoesNotContain(scopedAttention!, item => item.GetProperty("defenderIssues").EnumerateArray()
            .Any(issue => issue.GetString()?.StartsWith("Defender:", StringComparison.Ordinal) == true));
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PutAsJsonAsync("/api/agent/inventory", inventory with { Defender = null })).StatusCode);
        defenderResponse = await admin.GetFromJsonAsync<JsonElement>($"/api/devices/{id}/security/defender");
        Assert.Equal("available", defenderResponse.GetProperty("snapshot").GetProperty("providerState").GetString());
        var statusAdapters = status.GetProperty("metrics").GetProperty("networkAdapters").EnumerateArray().ToArray();
        Assert.Equal(2, statusAdapters.Length);
        Assert.Equal("AABBCCDDEEFF", statusAdapters[0].GetProperty("macAddress").GetString());
        Assert.Equal("192.168.1.50", statusAdapters[0].GetProperty("ipAddresses")[0].GetString());
        var listed = (await scoped.GetFromJsonAsync<JsonElement[]>("/api/devices"))!.Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.True(listed.GetProperty("online").GetBoolean());
        Assert.False(listed.GetProperty("revoked").GetBoolean());
        Assert.True(listed.GetProperty("lastSeen").ValueKind != JsonValueKind.Null);
        var presenceWorker = new OmniDesk.Api.Maintenance.DevicePresenceWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OmniDesk.Api.Maintenance.DevicePresenceOptions()),
            NullLogger<OmniDesk.Api.Maintenance.DevicePresenceWorker>.Instance);
        Guid hiddenDeviceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hiddenDevice = await db.Devices.SingleAsync(x => x.Name == "Other");
            hiddenDeviceId = hiddenDevice.Id;
            db.DeviceCredentials.Add(new DeviceCredential { DeviceId = hiddenDeviceId, PublicKeyPem = "fixture-only" });
            db.DeviceMetrics.Add(new DeviceMetrics { DeviceId = hiddenDeviceId, ReceivedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await presenceWorker.SweepOnceAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var metricsRow = await db.DeviceMetrics.SingleAsync(x => x.DeviceId == id);
            metricsRow.ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
            var hiddenMetrics = await db.DeviceMetrics.SingleAsync(x => x.DeviceId == hiddenDeviceId);
            hiddenMetrics.ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
            await db.SaveChangesAsync();
        }
        await presenceWorker.SweepOnceAsync();
        await presenceWorker.SweepOnceAsync();
        var deviceAlerts = await scoped.GetFromJsonAsync<JsonElement>("/api/notifications/devices");
        Assert.Contains(deviceAlerts.GetProperty("offlineDevices").EnumerateArray(), alert => alert.GetProperty("deviceId").GetGuid() == id);
        Assert.DoesNotContain(deviceAlerts.GetProperty("offlineDevices").EnumerateArray(), alert => alert.GetProperty("deviceId").GetGuid() == hiddenDeviceId);
        Guid offlineNotificationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var offlineEvent = await db.CoreOutboxEvents.SingleAsync(evt => evt.EventType == "device.presence.offline" && evt.AggregateId == id);
            var recipient = await db.Users.SingleAsync(user => user.Id == scopedId);
            recipient.Email = "scoped@example.test";
            recipient.EmailConfirmed = true;
            recipient.EmailNotificationsEnabled = true;
            recipient.EmailNotificationsEnabledAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            Assert.True(await db.Users.AnyAsync(candidate => candidate.Id == scopedId && candidate.EmailNotificationsEnabled &&
                candidate.EmailNotificationsEnabledAt != null && candidate.Email != null), "The persisted opt-in must satisfy the email queue policy.");
            await OmniDesk.Api.Security.TicketNotificationWorker.ProjectNotifications(db, new OmniDesk.Api.Security.PermissionService(db),
                offlineEvent, DateTimeOffset.UtcNow, CancellationToken.None, outboundEmailEnabled: true);
            await db.SaveChangesAsync();
            offlineNotificationId = await db.UserNotifications.Where(n => n.EventId == offlineEvent.Id && n.UserId == scopedId)
                .Select(n => n.Id).SingleAsync();
            Assert.True(await db.EmailNotificationDeliveries.AnyAsync(delivery => delivery.NotificationId == offlineNotificationId),
                $"Offline email projection was not queued (enabled={recipient.EmailNotificationsEnabled}, consent={recipient.EmailNotificationsEnabledAt}, email={recipient.Email ?? "<null>"}).");
        }
        var persistedAlerts = await scoped.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Contains(persistedAlerts.GetProperty("notifications").EnumerateArray(), alert =>
            alert.GetProperty("kind").GetString() == "device.offline" && alert.GetProperty("deviceId").GetGuid() == id);
        Assert.DoesNotContain(persistedAlerts.GetProperty("notifications").EnumerateArray(), alert =>
            alert.TryGetProperty("deviceId", out var target) && target.ValueKind == JsonValueKind.String && target.GetGuid() == hiddenDeviceId);
        var offlineEmailWorker = new OmniDesk.Api.ServiceDesk.EmailNotificationWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OmniDesk.Api.ServiceDesk.EmailNotificationWorker>.Instance,
            Options.Create(new OmniDesk.Api.ServiceDesk.OutboundEmailOptions
            {
                Enabled = true, Host = "smtp.example.test", Port = 587, FromAddress = "help@example.test",
                Username = "omni", PasswordFile = "/tmp/smtp-test-secret", DashboardUrl = "https://desk.example.test"
            }));
        for (var i = 0; i < 100 && !factory.OutboundEmail.Sent.Any(email => email.Subject.Contains("Dispositivo non raggiungibile", StringComparison.Ordinal)) &&
             await offlineEmailWorker.ProcessNext(CancellationToken.None); i++) { }
        var offlineEmail = Assert.Single(factory.OutboundEmail.Sent, email => email.Subject.Contains("Dispositivo non raggiungibile", StringComparison.Ordinal));
        Assert.Contains("Dispositivo non raggiungibile", offlineEmail.Subject);
        Assert.Contains("Windows fixture", offlineEmail.TextBody);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DevicePresence.Remove(await db.DevicePresence.SingleAsync(x => x.DeviceId == hiddenDeviceId));
            db.DeviceMetrics.Remove(await db.DeviceMetrics.SingleAsync(x => x.DeviceId == hiddenDeviceId));
            db.DeviceCredentials.Remove(await db.DeviceCredentials.SingleAsync(x => x.DeviceId == hiddenDeviceId));
            await db.SaveChangesAsync();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Target == id.ToString() && e.Action == "device.presence.offline"));
        }
        listed = (await scoped.GetFromJsonAsync<JsonElement[]>("/api/devices"))!.Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.False(listed.GetProperty("online").GetBoolean());
        Assert.True(listed.GetProperty("lastSeen").ValueKind != JsonValueKind.Null);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        await presenceWorker.SweepOnceAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Target == id.ToString() && e.Action == "device.presence.offline"));
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Target == id.ToString() && e.Action == "device.presence.online"));
        }
        listed = (await scoped.GetFromJsonAsync<JsonElement[]>("/api/devices"))!.Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.True(listed.GetProperty("online").GetBoolean());
        Assert.Equal(42.5, status.GetProperty("metrics").GetProperty("cpuPercent").GetDouble());
        Assert.Equal("no_failure_predicted", status.GetProperty("metrics").GetProperty("smartDisks")[0].GetProperty("status").GetString());
        Assert.Equal("SN-TEST-123", status.GetProperty("inventory").GetProperty("serialNumber").GetString());
        Assert.Equal("Test app", status.GetProperty("inventory").GetProperty("software")[0].GetProperty("name").GetString());
        await RemoteProvisioningScenario.Run(factory, admin, scoped, agent, () => Assertion(key), id);
        await SupportScenario.Run(factory, admin, scoped, agent, scopedId, () => Assertion(key), id, group);
        await PortalScenario.Run(factory, admin, id);
        await UpdateScenario.Run(admin, scoped, agent, scopedId, () => Assertion(key), id, group);
        await RemoteAgentUpdateScenario.Run(factory, admin, scoped, agent, () => Assertion(key), id);
        await DeviceManagementScenario.Run(factory, admin, scoped, scopedId, agent, () => Assertion(key), id, group);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/devices/{id}/revoke", null)).StatusCode);
        var revokedDiagnostics = await admin.PostAsJsonAsync($"/api/devices/{id}/diagnostics/processes", new DiagnosticRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, revokedDiagnostics.StatusCode);
        Assert.Equal("agent_credential_unavailable", (await revokedDiagnostics.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var revokedServices = await admin.PostAsJsonAsync($"/api/devices/{id}/services/actions", new ServiceControlRequest(Guid.NewGuid(), "Spooler", "stop"));
        Assert.Equal(HttpStatusCode.Conflict, revokedServices.StatusCode);
        Assert.Equal("agent_credential_unavailable", (await revokedServices.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var revokedProcesses = await admin.PostAsJsonAsync($"/api/devices/{id}/processes/actions", new ProcessControlRequest(Guid.NewGuid(), 12345, "notepad.exe", DateTimeOffset.UtcNow.AddSeconds(-10)));
        Assert.Equal(HttpStatusCode.Conflict, revokedProcesses.StatusCode);
        Assert.Equal("agent_credential_unavailable", (await revokedProcesses.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync("/api/agent/metrics", metrics)).StatusCode);
        status = await scoped.GetFromJsonAsync<JsonElement>($"/api/devices/{id}/status");
        listed = (await scoped.GetFromJsonAsync<JsonElement[]>("/api/devices"))!.Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.True(listed.GetProperty("revoked").GetBoolean());
        Assert.False(listed.GetProperty("online").GetBoolean());
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Assertion(key));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/agent/support/" + new string('A',64))).StatusCode);
        Assert.True(status.GetProperty("revoked").GetBoolean()); Assert.False(status.GetProperty("online").GetBoolean());
    }
}
