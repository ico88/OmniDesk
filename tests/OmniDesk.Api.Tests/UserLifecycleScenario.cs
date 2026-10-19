using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using Xunit;

namespace OmniDesk.Api.Tests;

public static class UserLifecycleScenario
{
    public static async Task Run(ApiFactory factory, HttpClient admin, HttpClient viewer, string password, Guid device)
    {
        using var host = factory.WithWebHostBuilder(_ => { });
        HttpClient Client() => host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        async Task<HttpStatusCode> Login(HttpClient client, string email)
        {
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
            if (response.IsSuccessStatusCode)
            {
                csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
                client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
                client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            }
            return response.StatusCode;
        }
        var created = await admin.PostAsJsonAsync("/api/admin/users", new { email = "lifecycle@example.test", password, role = "Operator" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var adminMe = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var adminId = adminMe.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/status", new { isDisabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/roles", new { roles = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/admin/users/{id}/status", new { isDisabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/admin/users/{id}/roles", new { roles = new[] { "Administrator" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/admin/users/{id}/revoke-sessions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/users/{id}/roles", new { roles = new[] { "unknown" } })).StatusCode);
        using var current = Client(); using var oldCookie = Client();
        Assert.Equal(HttpStatusCode.NoContent, await Login(current, "lifecycle@example.test"));
        Assert.Equal(HttpStatusCode.NoContent, await Login(oldCookie, "lifecycle@example.test"));
        using (var noCsrf = Client())
        {
            Assert.Equal(HttpStatusCode.NoContent, await Login(noCsrf, "admin@example.test"));
            noCsrf.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            Assert.Equal(HttpStatusCode.BadRequest, (await noCsrf.PutAsJsonAsync($"/api/admin/users/{id}/status", new { isDisabled = true })).StatusCode);
        }
        var remote = await current.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "remote.control" });
        Assert.Equal(HttpStatusCode.Created, remote.StatusCode);
        var remoteId = (await remote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/users/{id}/status", new { isDisabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await current.GetAsync("/api/auth/me")).StatusCode);
        using (var attempt = Client()) Assert.Equal(HttpStatusCode.Unauthorized, await Login(attempt, "lifecycle@example.test"));
        using (var scope = factory.Services.CreateScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<PermissionService>().Allows(id, "remote.control", device));
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<PermissionService>().VisibleDevices(id).ToListAsync());
            Assert.Equal("revocation_pending", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RemoteSessions.FindAsync(remoteId))!.State);
        }
        var guard = new RemoteSessionGuard(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<RemoteSessionGuard>.Instance);
        factory.Mesh.FailRevocation = true;
        await guard.Sweep(CancellationToken.None);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal("revocation_pending", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RemoteSessions.FindAsync(remoteId))!.State);
        factory.Mesh.FailRevocation = false;
        await guard.Sweep(CancellationToken.None);
        using (var scope = factory.Services.CreateScope())
        {
            var ended = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RemoteSessions.FindAsync(remoteId))!;
            Assert.Equal("revoked", ended.State);
            Assert.NotNull(ended.EndedAt);
        }
        Assert.Contains("node//differentnode", factory.Mesh.RevokedNodes);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/users/{id}/status", new { isDisabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldCookie.GetAsync("/api/auth/me")).StatusCode);
        using var enabled = Client();
        Assert.Equal(HttpStatusCode.NoContent, await Login(enabled, "lifecycle@example.test"));
        var stillAuthorized = await enabled.PostAsJsonAsync($"/api/devices/{device}/remote-sessions", new { capability = "terminal" });
        Assert.Equal(HttpStatusCode.Created, stillAuthorized.StatusCode);
        var authorizedId = (await stillAuthorized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await guard.Sweep(CancellationToken.None);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal("active", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RemoteSessions.FindAsync(authorizedId))!.State);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/users/{id}/roles", new { roles = new[] { "Viewer" } })).StatusCode);
        await guard.Sweep(CancellationToken.None);
        using (var scope = factory.Services.CreateScope())
        {
            var ended = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RemoteSessions.FindAsync(authorizedId))!;
            Assert.Equal("revoked", ended.State);
            Assert.NotNull(ended.EndedAt);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await enabled.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await Login(enabled, "lifecycle@example.test"));
        var me = await enabled.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("view", Assert.Single(me.GetProperty("globalPermissions").EnumerateArray()).GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{id}/revoke-sessions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await enabled.GetAsync("/api/devices")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var events = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents.Where(e => e.Target == id).Select(e => e.Action).ToListAsync();
            foreach (var action in new[] { "user.disable", "user.enable", "user.roles.change", "user.sessions.revoke" }) Assert.Contains(action, events);
        }
    }
}
