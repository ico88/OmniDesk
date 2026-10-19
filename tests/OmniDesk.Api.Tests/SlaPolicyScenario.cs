using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniDesk.Api.Data;
using OmniDesk.Api.Endpoints;
using OmniDesk.Api.Security;
using OmniDesk.Api.ServiceDesk;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class SlaPolicyScenario(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Sla_policies_require_scoped_permission_and_publish_immutable_versions()
    {
        var siteId = Guid.NewGuid();
        var roleName = "SLA manager " + Guid.NewGuid().ToString("N");
        var userName = "sla-" + Guid.NewGuid().ToString("N") + "@example.test";
        const string password = "SlaPolicy!9-test-password";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TestDatabaseMigration.ApplyAsync(db);
            db.Sites.Add(new Site { Id = siteId, Name = "SLA fixture " + siteId.ToString("N") });
            await db.SaveChangesAsync();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(roleName);
            Assert.True((await roles.CreateAsync(role)).Succeeded);
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = "tickets.sla.manage" });
            await db.SaveChangesAsync();
            var user = new AppUser { UserName = userName, Email = userName };
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            db.ScopeGrants.Add(new ScopeGrant { UserId = user.Id, RoleId = role.Id, SiteId = siteId });
            await db.SaveChangesAsync();
        }

        using var anonymous = factory.CreateClient(ClientOptions());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/support-sla/policies")).StatusCode);

        using var manager = factory.CreateClient(ClientOptions());
        await Login(manager, userName, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/admin/support-sla/policies",
            new SlaPolicyCreate("Global", null, Definition()))).StatusCode);

        var create = await manager.PostAsJsonAsync("/api/admin/support-sla/policies", new SlaPolicyCreate("Site SLA", siteId, Definition()));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreatedPolicy>();
        Assert.NotNull(created);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/admin/support-sla/policies",
            new SlaPolicyCreate("Cross-site", Guid.NewGuid(), Definition()))).StatusCode);

        var version = await manager.PostAsJsonAsync($"/api/admin/support-sla/policies/{created.Id}/versions",
            new SlaPolicyVersionCreate(1, "Site SLA revised", Definition(120, 600)));
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync($"/api/admin/support-sla/policies/{created.Id}/versions",
            new SlaPolicyVersionCreate(1, "Stale", Definition()))).StatusCode);

        var listed = await manager.GetFromJsonAsync<PolicyListItem[]>("/api/admin/support-sla/policies");
        var policy = Assert.Single(listed!);
        Assert.Equal(siteId, policy.SiteId);
        Assert.Equal(2, policy.CurrentVersion);
        Assert.Equal([1, 2], policy.Versions.Select(item => item.Version).ToArray());
        Assert.DoesNotContain("tickets.sla.manage", Permissions.ForRole("Operator"));

        var deviceId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var publisher = await db.Users.SingleAsync(x => x.UserName == userName);
            var globalPolicy = new SupportSlaPolicy { ScopeKey = "global", Name = "Global fallback", CurrentVersion = 1 };
            db.SupportSlaPolicies.Add(globalPolicy);
            db.SupportSlaPolicyVersions.Add(new SupportSlaPolicyVersion
            {
                PolicyId = globalPolicy.Id, Version = 1, Name = globalPolicy.Name,
                DefinitionJson = JsonSerializer.Serialize(Definition(30, 120), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PublishedBy = publisher.Id
            });
            db.Devices.Add(new Device { Id = deviceId, Name = "SLA target fixture", SiteId = siteId });
            var ticket = new SupportTicket
            {
                Id = ticketId, DeviceId = deviceId, SiteId = siteId, RequestId = Guid.NewGuid(),
                RequesterKey = new string('a', 64), Subject = "SLA target fixture", Description = "Fixture",
                CreatedAt = createdAt, UpdatedAt = createdAt
            };
            db.SupportTickets.Add(ticket);
            await TicketSlaTargetService.ApplyInitialTargets(db, ticket);
            await db.SaveChangesAsync();
            var targets = await db.SupportTicketSlaTargets.AsNoTracking().Where(x => x.TicketId == ticketId)
                .OrderBy(x => x.TargetType).ToArrayAsync();
            Assert.Equal(2, targets.Length);
            Assert.All(targets, target => Assert.Equal(2, target.PolicyVersion));
            Assert.All(targets, target => Assert.Equal(createdAt, target.StartedAt));
            Assert.Equal(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
                targets.Single(x => x.TargetType == "first_response").DueAt);
            Assert.Equal(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
                targets.Single(x => x.TargetType == "resolution").DueAt);
            var respondedAt = new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero);
            Assert.Equal(1, await TicketSlaTargetService.CompleteTarget(db, ticketId, "first_response", respondedAt));
            Assert.Equal(0, await TicketSlaTargetService.CompleteTarget(db, ticketId, "first_response", respondedAt.AddMinutes(1)));
            var responseTarget = await db.SupportTicketSlaTargets.AsNoTracking()
                .SingleAsync(x => x.TicketId == ticketId && x.TargetType == "first_response");
            var resolutionTarget = await db.SupportTicketSlaTargets.AsNoTracking()
                .SingleAsync(x => x.TicketId == ticketId && x.TargetType == "resolution");
            Assert.Equal(respondedAt, responseTarget.CompletedAt);
            Assert.Null(resolutionTarget.CompletedAt);
            var resolvedAt = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero);
            Assert.Equal(1, await TicketSlaTargetService.CompleteTarget(db, ticketId, "resolution", resolvedAt));
            resolutionTarget = await db.SupportTicketSlaTargets.AsNoTracking()
                .SingleAsync(x => x.TicketId == ticketId && x.TargetType == "resolution");
            Assert.Equal(resolvedAt, resolutionTarget.CompletedAt);

            var fallbackSiteId = Guid.NewGuid();
            var fallbackDeviceId = Guid.NewGuid();
            var fallbackTicket = new SupportTicket
            {
                Id = Guid.NewGuid(), DeviceId = fallbackDeviceId, SiteId = fallbackSiteId, RequestId = Guid.NewGuid(),
                RequesterKey = new string('b', 64), Subject = "Global SLA fallback fixture", Description = "Fixture",
                CreatedAt = createdAt, UpdatedAt = createdAt
            };
            db.Sites.Add(new Site { Id = fallbackSiteId, Name = "SLA fallback fixture" });
            db.Devices.Add(new Device { Id = fallbackDeviceId, Name = "SLA fallback device", SiteId = fallbackSiteId });
            db.SupportTickets.Add(fallbackTicket);
            await TicketSlaTargetService.ApplyInitialTargets(db, fallbackTicket);
            await db.SaveChangesAsync();
            var fallbackTargets = await db.SupportTicketSlaTargets.AsNoTracking()
                .Where(x => x.TicketId == fallbackTicket.Id).ToArrayAsync();
            Assert.Equal(2, fallbackTargets.Length);
            Assert.All(fallbackTargets, target => Assert.Equal(globalPolicy.Id, target.PolicyId));
            Assert.All(fallbackTargets, target => Assert.Equal(1, target.PolicyVersion));
            await tx.RollbackAsync();
        }
    }

    private static SlaPolicyDefinition Definition(int firstResponse = 60, int resolution = 480) => new(
        firstResponse,
        resolution,
        new SlaCalendarInput("UTC", new Dictionary<DayOfWeek, BusinessTimeRange[]>
        {
            [DayOfWeek.Monday] = [new BusinessTimeRange(new TimeOnly(9, 0), new TimeOnly(17, 0))]
        }, []));

    private static WebApplicationFactoryClientOptions ClientOptions() => new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false };

    private static async Task Login(HttpClient client, string email, string password)
    {
        var csrf = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        csrf = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private sealed record CreatedPolicy(Guid Id, string ScopeKey, int CurrentVersion, long Version);
    private sealed record PolicyListItem(Guid Id, Guid? SiteId, string ScopeKey, int CurrentVersion, long Version, PolicyVersionItem[] Versions);
    private sealed record PolicyVersionItem(int Version, string Name, Guid? SiteId, SlaPolicyDefinition Definition, DateTimeOffset PublishedAt);
}
