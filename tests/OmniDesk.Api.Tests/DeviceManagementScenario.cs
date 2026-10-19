using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniDesk.Api.Data;
using OmniDesk.Contracts;
using Xunit;

namespace OmniDesk.Api.Tests;

public static class DeviceManagementScenario
{
    public static async Task Run(ApiFactory factory, HttpClient admin, HttpClient scoped, string scopedId, HttpClient agent, Func<string> assertion, Guid device, Guid originalGroup)
    {
        var group = (await (await admin.PostAsJsonAsync("/api/admin/groups", new { name = "Moved devices fixture" })).Content.ReadFromJsonAsync<DeviceGroup>())!.Id;
        object Edit(string name, Guid? target, string expectedName, Guid? expectedGroup) => new { name, groupId = target, expectedName, expectedGroupId = expectedGroup };
        var original = await admin.GetFromJsonAsync<JsonElement>($"/api/devices/{device}/status");
        var originalName = original.GetProperty("name").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PutAsJsonAsync($"/api/admin/devices/{device}", Edit("Denied", group, originalName, originalGroup))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsync($"/api/admin/devices/{device}/revoke", null)).StatusCode);
        var csrf = admin.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        try { Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}", Edit("Denied", group, originalName, originalGroup))).StatusCode); }
        finally { admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf); }
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}", Edit(" ", group, originalName, originalGroup))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}", Edit("Unknown group", Guid.NewGuid(), originalName, originalGroup))).StatusCode);
        async Task<Guid> GrantOperator()
        {
            var grant = await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Operator", groupId = originalGroup, deviceId = (Guid?)null });
            Assert.Equal(HttpStatusCode.Created, grant.StatusCode);
            return (await grant.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        var operatorGrant = await GrantOperator();
        var queued = await scoped.PostAsJsonAsync($"/api/devices/{device}/updates/scan", new UpdateJobRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, queued.StatusCode);
        var queuedId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}/mesh", new { nodeId = "node//managementfixture" })).StatusCode);
        var remote = await scoped.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "remote.control" });
        Assert.Equal(HttpStatusCode.Created, remote.StatusCode);
        var remoteId = (await remote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}", Edit("Moved endpoint", group, originalName, originalGroup))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync($"/api/devices/{device}/status")).StatusCode);
        Assert.DoesNotContain((await scoped.GetFromJsonAsync<Device[]>("/api/devices"))!, x => x.Id == device);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("cancelled", (await db.UpdateJobs.FindAsync(queuedId))!.State);
            Assert.Contains((await db.RemoteSessions.FindAsync(remoteId))!.State, new[] { "revocation_pending", "revoked" });
        }
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}", Edit("Stale edit", originalGroup, originalName, originalGroup))).StatusCode);
        var edits = await Task.WhenAll(
            admin.PutAsJsonAsync($"/api/admin/devices/{device}", Edit("Concurrent one", group, "Moved endpoint", group)),
            admin.PutAsJsonAsync($"/api/admin/devices/{device}", Edit("Concurrent two", group, "Moved endpoint", group)));
        Assert.Single(edits, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(edits, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Viewer", groupId = (Guid?)null, deviceId = device })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scoped.GetAsync($"/api/devices/{device}/status")).StatusCode);
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/devices/{device}/status");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/devices/{device}", Edit(originalName, originalGroup, current.GetProperty("name").GetString()!, group))).StatusCode);
        queued = await scoped.PostAsJsonAsync($"/api/devices/{device}/updates/scan", new UpdateJobRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, queued.StatusCode);
        queuedId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/grants/{operatorGrant}")).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsync("/api/agent/jobs/claim", null)).StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal("cancelled", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().UpdateJobs.FindAsync(queuedId))!.State);
        queued = await admin.PostAsJsonAsync($"/api/devices/{device}/updates/scan", new UpdateJobRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, queued.StatusCode);
        queuedId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/devices/{device}/revoke", null)).StatusCode);
        DateTimeOffset? revokedAt;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            revokedAt = (await db.DeviceCredentials.FindAsync(device))!.RevokedAt;
            Assert.Equal("cancelled", (await db.UpdateJobs.FindAsync(queuedId))!.State);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/devices/{device}/revoke", null)).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assertion());
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsync("/api/agent/jobs/claim", null)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(revokedAt, (await db.DeviceCredentials.FindAsync(device))!.RevokedAt);
            var actions = await db.AuditEvents.Select(e => e.Action).Distinct().ToListAsync();
            foreach (var action in new[] { "device.metadata.change", "device.group.change", "update.cancel.authorization", "update.cancel.device_revoked" }) Assert.Contains(action, actions);
        }
        var raceDevice = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Devices.Add(new Device { Id = raceDevice, Name = "Queue/revoke race fixture" });
            db.DeviceCredentials.Add(new DeviceCredential { DeviceId = raceDevice, PublicKeyPem = (await db.DeviceCredentials.FindAsync(device))!.PublicKeyPem });
            await db.SaveChangesAsync();
        }
        var race = await Task.WhenAll(
            admin.PostAsJsonAsync($"/api/devices/{raceDevice}/updates/scan", new UpdateJobRequest(Guid.NewGuid())),
            admin.PostAsync($"/api/admin/devices/{raceDevice}/revoke", null));
        Assert.Contains(race[0].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        Assert.Equal(HttpStatusCode.NoContent, race[1].StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull((await db.DeviceCredentials.FindAsync(raceDevice))!.RevokedAt);
            Assert.False(await db.UpdateJobs.AnyAsync(x => x.DeviceId == raceDevice && x.State == "queued"));
        }
    }
}
