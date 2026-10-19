using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Remote.MeshCentral;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeMesh Mesh { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(s => {
        // Integration scenarios invoke these workers explicitly so their retries do not race the assertions.
        var workers = new[] { typeof(AssertionCleanup), typeof(RemoteSessionGuard), typeof(RemoteProvisioningGuard) };
        foreach (var descriptor in s.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType is not null && workers.Contains(x.ImplementationType)).ToArray()) s.Remove(descriptor);
        s.AddSingleton<IMeshCentralClient>(Mesh);
    }).ConfigureAppConfiguration((_, c) =>
        c.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Security:TotpEnabled"] = "true",
            ["ConnectionStrings:Database"] = Environment.GetEnvironmentVariable("TEST_DATABASE")
                ?? throw new InvalidOperationException("TEST_DATABASE must point to an isolated PostgreSQL test database")
        }));
}
public sealed class SecurityTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory factory;
    private static readonly string Password = "Fixture!9" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    private string adminId = "", scopedId = "", viewerId = "";
    private readonly Guid group = Guid.NewGuid(), device = Guid.NewGuid(), other = Guid.NewGuid();
    public SecurityTests(ApiFactory factory) => this.factory = factory;
    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        async Task<string> Create(string email, string? role)
        {
            var user = new AppUser { Email = email, UserName = email };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Code + ": " + error.Description)));
            if (role is not null) Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            return user.Id;
        }
        adminId = await Create("admin@example.test", "Administrator");
        scopedId = await Create("scoped@example.test", null);
        viewerId = await Create("viewer@example.test", "Viewer");
        db.DeviceGroups.Add(new DeviceGroup { Id = group, Name = "Test group" });
        var site = Guid.Parse("11111111-1111-4111-8111-111111111111");
        db.Devices.AddRange(new Device { Id = device, Name = "Authorized", GroupId = group, SiteId = site }, new Device { Id = other, Name = "Other", SiteId = site });
        await db.SaveChangesAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task Csrf(HttpClient client)
    {
        var value = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", value.GetProperty("token").GetString());
    }
    private static async Task Login(HttpClient client, string email)
    {
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        await Csrf(client);
    }
    // One end-to-end scenario deliberately uses a single fresh database and tests real PostgreSQL constraints.
    [Fact]
    public async Task Authentication_authorization_scope_revocation_branding_and_audit()
    {
        using var anon = Client();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/api/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = Password })).StatusCode);
        await Csrf(anon);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = "wrong" })).StatusCode);
        using var admin = Client();
        await Login(admin, "admin@example.test");
        Assert.Contains(admin.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN"), x => !string.IsNullOrEmpty(x));
        var devices = await admin.GetFromJsonAsync<Device[]>("/api/devices");
        Assert.Equal(2, devices!.Length);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/users", new { email = "new@example.test", password = Password, role = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/users", new { email = "bad@example.test", password = "weak", role = "Viewer" })).StatusCode);
        using var viewer = Client();
        await Login(viewer, "viewer@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/admin/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/admin/devices/{device}/agent-update", new { requestId = Guid.NewGuid() })).StatusCode);
        var branding = new { name = "Test brand", logoUrl = "", primaryColor = "#123456", theme = "light" };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/branding", branding)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/branding", new { name = "Unsafe", logoUrl = "javascript:alert(1)", primaryColor = "#123456", theme = "light" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/branding", branding)).StatusCode);
        var saved = await viewer.GetFromJsonAsync<Branding>("/api/branding");
        Assert.Equal("Test brand", saved!.Name);
        using var scoped = Client();
        await Login(scoped, "scoped@example.test");
        Assert.Empty((await scoped.GetFromJsonAsync<Device[]>("/api/devices"))!);
        var grant = new { userId = scopedId, role = "Viewer", groupId = group, deviceId = (Guid?)null };
        var created = await admin.PostAsJsonAsync("/api/admin/grants", grant);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/grants", grant)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Viewer", groupId = group, deviceId = device })).StatusCode);
        Assert.Equal(device, Assert.Single((await scoped.GetFromJsonAsync<Device[]>("/api/devices"))!).Id);
        using (var scope = factory.Services.CreateScope())
        {
            var p = scope.ServiceProvider.GetRequiredService<PermissionService>();
            Assert.True(await p.Allows(scopedId, "view", device));
            foreach (var capability in new[] { "remote.control", "terminal", "files", "updates", "administration" })
                Assert.False(await p.Allows(scopedId, capability, device));
            Assert.False(await p.Allows(scopedId, "view", other));
            Assert.False(await p.Allows(scopedId, "view"));
        }
        var grantId = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/grants/{grantId}")).StatusCode);
        Assert.Empty((await scoped.GetFromJsonAsync<Device[]>("/api/devices"))!);
        var siteId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var newSiteResponse = await admin.PostAsJsonAsync("/api/admin/sites", new { name = "Integration branch" });
        Assert.Equal(HttpStatusCode.Created, newSiteResponse.StatusCode);
        var newSiteId = (await newSiteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Contains(await db.CoreOutboxEvents.ToListAsync(), x => x.EventType == "site.created" && x.AggregateId == newSiteId);
        }
        var siteGrant = await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Viewer", siteId });
        Assert.Equal(HttpStatusCode.Created, siteGrant.StatusCode);
        Assert.Equal(2, (await scoped.GetFromJsonAsync<Device[]>("/api/devices"))!.Length);
        using (var scope = factory.Services.CreateScope())
        {
            var p = scope.ServiceProvider.GetRequiredService<PermissionService>();
            Assert.True(await p.Allows(scopedId, "view", other));
            Assert.False(await p.Allows(scopedId, "remote.control", other));
        }
        var siteGrantId = (await siteGrant.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/grants/{siteGrantId}")).StatusCode);
        Assert.Empty((await scoped.GetFromJsonAsync<Device[]>("/api/devices"))!);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Operator", deviceId = device, groupId = (Guid?)null })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var p = scope.ServiceProvider.GetRequiredService<PermissionService>();
            Assert.True(await p.Allows(scopedId, "remote.control", device));
            Assert.False(await p.Allows(scopedId, "remote.control", other));
            Assert.False(await p.Allows(scopedId, "administration", device));
        }
        var scopedAdmin = await admin.PostAsJsonAsync("/api/admin/grants", new { userId = scopedId, role = "Administrator", deviceId = device, groupId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, scopedAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.PostAsJsonAsync($"/api/admin/devices/{device}/agent-update", new { requestId = Guid.NewGuid() })).StatusCode);
        var scopedAdminId = (await scopedAdmin.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/grants/{scopedAdminId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/admin/roles", new { name = "Escape", permissions = new[] { "administration" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Invalid", permissions = new[] { "unknown" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/roles", new { name = "FilesOnly", permissions = new[] { "files" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/grants", new { userId = viewerId, role = "FilesOnly", deviceId = other, groupId = (Guid?)null })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var p = scope.ServiceProvider.GetRequiredService<PermissionService>();
            Assert.True(await p.Allows(viewerId, "files", other));
            Assert.False(await p.Allows(viewerId, "files", device));
            Assert.False(await p.Allows(viewerId, "terminal", other));
            Assert.False(await p.Allows(viewerId, "remote.control", other));
        }
        var events = await admin.GetFromJsonAsync<AuditEvent[]>("/api/admin/audit");
        Assert.Contains(events!, x => x.Action == "branding.update");
        Assert.Contains(events!, x => x.Action == "grant.revoke");
        Assert.Contains(events!, x => x.Action == "auth.login" && x.Outcome == "denied");
        await DownloadScenario.Run(factory, admin, viewer);
        var viewerSummary=await viewer.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/support/summary");
        Assert.Equal(0,viewerSummary.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await scoped.PutAsJsonAsync("/api/auth/profile", new { displayName = new string('x', 81) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scoped.PutAsJsonAsync("/api/auth/profile", new { displayName = "Federico Operatore" })).StatusCode);
        await RemoteScenario.Run(admin, viewer, scoped, device, factory.Mesh);
        await AgentScenario.Run(factory, admin, scoped, scopedId);
        await UserLifecycleScenario.Run(factory, admin, viewer, Password, device);
        await AccountScenario.Run(factory, admin, viewer, Password);
        // Logout is CSRF-protected and invalidates the current browser cookie.
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/auth/logout", null)).StatusCode);
        await Csrf(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/auth/me")).StatusCode);
        // Existing TOTP-enabled accounts must never bypass the unfinished challenge flow.
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(viewerId);
            Assert.True((await users.SetTwoFactorEnabledAsync(user!, true)).Succeeded);
        }
        using var totp = Client();
        await Csrf(totp);
        Assert.Equal(HttpStatusCode.Unauthorized, (await totp.PostAsJsonAsync("/api/auth/login", new { email = "viewer@example.test", password = Password + "New!" })).StatusCode);
        // Persistent Identity lockout after five failed attempts.
        using var locked = Client();
        await Csrf(locked);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await locked.PostAsJsonAsync("/api/auth/login", new { email = "scoped@example.test", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await locked.PostAsJsonAsync("/api/auth/login", new { email = "scoped@example.test", password = Password })).StatusCode);
    }
}
