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
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = "tickets.view" });
            await db.SaveChangesAsync();
            var user = new AppUser { UserName = userName, Email = userName };
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            db.ScopeGrants.Add(new ScopeGrant { UserId = user.Id, RoleId = role.Id, SiteId = siteId });
            await db.SaveChangesAsync();
        }

        using var anonymous = factory.CreateClient(ClientOptions());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/support-sla/policies")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/support-sla/scopes")).StatusCode);

        using var manager = factory.CreateClient(ClientOptions());
        await Login(manager, userName, password);
        var scopes = await manager.GetFromJsonAsync<SlaScopes>("/api/admin/support-sla/scopes");
        Assert.NotNull(scopes);
        Assert.False(scopes.CanManageGlobal);
        Assert.Equal(siteId, Assert.Single(scopes.Sites).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/admin/support-sla/policies",
            new SlaPolicyCreate("Global", null, Definition()))).StatusCode);

        var create = await manager.PostAsJsonAsync("/api/admin/support-sla/policies", new SlaPolicyCreate("Site SLA", siteId, Definition()));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreatedPolicy>();
        Assert.NotNull(created);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/admin/support-sla/policies",
            new SlaPolicyCreate("Cross-site", Guid.NewGuid(), Definition()))).StatusCode);

        var version = await manager.PostAsJsonAsync($"/api/admin/support-sla/policies/{created.Id}/versions",
            new SlaPolicyVersionCreate(1, "Site SLA revised", Definition(120, 600, ["waiting_customer"])));
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync($"/api/admin/support-sla/policies/{created.Id}/versions",
            new SlaPolicyVersionCreate(1, "Stale", Definition()))).StatusCode);

        var listed = await manager.GetFromJsonAsync<PolicyListItem[]>("/api/admin/support-sla/policies");
        var policy = Assert.Single(listed!);
        Assert.Equal(siteId, policy.SiteId);
        Assert.Equal(2, policy.CurrentVersion);
        Assert.Equal("Site SLA", policy.Name);
        Assert.Equal([1, 2], policy.Versions.Select(item => item.Version).ToArray());
        Assert.Equal("Site SLA revised", policy.Versions.Single(item => item.Version == 2).Name);
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
            var overdueAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SupportTicketSlaTargets.Where(x => x.TicketId == fallbackTicket.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.DueAt, overdueAt));
            var breached = 0;
            while (await SlaBreachWorker.MarkNextBreach(db)) breached++;
            Assert.Equal(2, breached);
            var breachedTargets = await db.SupportTicketSlaTargets.AsNoTracking()
                .Where(x => x.TicketId == fallbackTicket.Id).ToArrayAsync();
            Assert.All(breachedTargets, target => Assert.NotNull(target.BreachedAt));
            Assert.Equal(2, await db.AuditEvents.AsNoTracking().CountAsync(x =>
                x.Action == "ticket.sla.target.breached" && x.Target == fallbackTicket.Id.ToString()));
            Assert.False(await SlaBreachWorker.MarkNextBreach(db));

            var pauseTicket = new SupportTicket
            {
                Id = Guid.NewGuid(), DeviceId = deviceId, SiteId = siteId, RequestId = Guid.NewGuid(),
                RequesterKey = new string('d', 64), Subject = "SLA pause fixture", Description = "Fixture",
                CreatedAt = createdAt, UpdatedAt = createdAt
            };
            db.SupportTickets.Add(pauseTicket);
            await TicketSlaTargetService.ApplyInitialTargets(db, pauseTicket);
            await db.SaveChangesAsync();
            var pauseAnchor = DateTimeOffset.UtcNow;
            var pauseAt = pauseAnchor.AddMinutes(-5);
            var dueBeforePause = pauseAnchor.AddMinutes(-2);
            await db.SupportTicketSlaTargets.Where(x => x.TicketId == pauseTicket.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.DueAt, dueBeforePause));
            Assert.Equal(2, await TicketSlaTargetService.ApplyPauseState(db, pauseTicket.Id, "waiting_customer",
                publisher.Id, pauseAt));
            await db.SaveChangesAsync();
            var latePauseTicket = new SupportTicket
            {
                Id = Guid.NewGuid(), DeviceId = deviceId, SiteId = siteId, RequestId = Guid.NewGuid(),
                RequesterKey = new string('f', 64), Subject = "Late SLA pause fixture", Description = "Fixture",
                CreatedAt = createdAt, UpdatedAt = createdAt
            };
            db.SupportTickets.Add(latePauseTicket);
            await TicketSlaTargetService.ApplyInitialTargets(db, latePauseTicket);
            await db.SaveChangesAsync();
            await db.SupportTicketSlaTargets.Where(x => x.TicketId == latePauseTicket.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.DueAt, pauseAnchor.AddMinutes(-6)));
            Assert.Equal(0, await db.SupportTicketSlaTargets.AsNoTracking().CountAsync(x => x.TicketId == latePauseTicket.Id && x.DueAt > pauseAt));
            Assert.Equal(0, await TicketSlaTargetService.ApplyPauseState(db, latePauseTicket.Id, "waiting_customer",
                publisher.Id, pauseAt));
            await db.SaveChangesAsync();
            var lateBreaches = 0;
            while (await SlaBreachWorker.MarkNextBreach(db)) lateBreaches++;
            Assert.Equal(2, lateBreaches);
            Assert.False(await SlaBreachWorker.MarkNextBreach(db));
            var resumeAt = pauseAnchor.AddMinutes(4);
            Assert.Equal(2, await TicketSlaTargetService.ApplyPauseState(db, pauseTicket.Id, "in_progress",
                publisher.Id, resumeAt));
            await db.SaveChangesAsync();
            var resumedTargets = await db.SupportTicketSlaTargets.AsNoTracking().Where(x => x.TicketId == pauseTicket.Id).ToArrayAsync();
            Assert.All(resumedTargets, target =>
            {
                Assert.Null(target.PausedAt);
                Assert.Equal(540, target.AccumulatedPauseSeconds);
                Assert.InRange((target.DueAt - dueBeforePause.AddMinutes(9)).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
            });
            await db.SupportTicketSlaTargets.Where(x => x.TicketId == pauseTicket.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            var resumedBreaches = 0;
            while (await SlaBreachWorker.MarkNextBreach(db)) resumedBreaches++;
            Assert.Equal(2, resumedBreaches);

            var reopenTicket = new SupportTicket
            {
                Id = Guid.NewGuid(), DeviceId = deviceId, SiteId = siteId, RequestId = Guid.NewGuid(),
                RequesterKey = new string('e', 64), Subject = "SLA reopen fixture", Description = "Fixture",
                CreatedAt = createdAt, UpdatedAt = createdAt
            };
            db.SupportTickets.Add(reopenTicket);
            await TicketSlaTargetService.ApplyInitialTargets(db, reopenTicket);
            await db.SaveChangesAsync();
            var closeAt = DateTimeOffset.UtcNow;
            Assert.Equal(1, await TicketSlaTargetService.CompleteTarget(db, reopenTicket.Id, "first_response", closeAt));
            Assert.Equal(1, await TicketSlaTargetService.CompleteTarget(db, reopenTicket.Id, "resolution", closeAt));
            Assert.True(await TicketSlaTargetService.StartResolutionCycle(db, reopenTicket.Id, closeAt.AddMinutes(1), publisher.Id));
            await db.SaveChangesAsync();
            var cycles = await db.SupportTicketSlaTargets.AsNoTracking().Where(x => x.TicketId == reopenTicket.Id && x.TargetType == "resolution")
                .OrderBy(x => x.Cycle).ToArrayAsync();
            Assert.Equal([1, 2], cycles.Select(x => x.Cycle).ToArray());
            Assert.NotNull(cycles[0].CompletedAt);
            Assert.InRange((cycles[0].CompletedAt!.Value - closeAt).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
            Assert.Null(cycles[1].CompletedAt);
            Assert.Equal(cycles[0].PolicyVersion, cycles[1].PolicyVersion);
            Assert.Equal(1, await TicketSlaTargetService.CompleteTarget(db, reopenTicket.Id, "resolution", closeAt.AddMinutes(2)));
            Assert.Equal(0, await TicketSlaTargetService.CompleteTarget(db, reopenTicket.Id, "resolution", closeAt.AddMinutes(3)));

            var alertTicket = new SupportTicket
            {
                Id = Guid.NewGuid(), DeviceId = deviceId, SiteId = siteId, RequestId = Guid.NewGuid(),
                RequesterKey = new string('c', 64), Subject = "SLA notification fixture", Description = "Fixture",
                AssigneeId = publisher.Id, CreatedAt = createdAt, UpdatedAt = createdAt
            };
            db.SupportTickets.Add(alertTicket);
            await TicketSlaTargetService.ApplyInitialTargets(db, alertTicket);
            await db.SaveChangesAsync();
            await db.SupportTicketSlaTargets.Where(x => x.TicketId == alertTicket.Id && x.TargetType == "first_response")
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.DueAt, overdueAt));
            Assert.True(await SlaBreachWorker.MarkNextBreach(db));
            var breachEvent = await db.CoreOutboxEvents.AsNoTracking()
                .SingleAsync(x => x.EventType == "ticket.sla.breached" && x.AggregateId == alertTicket.Id);
            await TicketNotificationWorker.ProjectNotifications(db, new PermissionService(db), breachEvent,
                DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
            Assert.Contains(await db.UserNotifications.AsNoTracking().Where(x => x.EventId == breachEvent.Id).ToArrayAsync(),
                notification => notification.UserId == publisher.Id && notification.Kind == "ticket.sla.breached");
            await tx.RollbackAsync();
        }
    }

    private static SlaPolicyDefinition Definition(int firstResponse = 60, int resolution = 480, string[]? pauseStates = null) => new(
        firstResponse,
        resolution,
        new SlaCalendarInput("UTC", new Dictionary<DayOfWeek, BusinessTimeRange[]>
        {
            [DayOfWeek.Monday] = [new BusinessTimeRange(new TimeOnly(9, 0), new TimeOnly(17, 0))]
        }, [])) { PauseStates = pauseStates ?? [] };

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
    private sealed record SlaScopes(bool CanManageGlobal, SlaSite[] Sites);
    private sealed record SlaSite(Guid Id, string Name);
    private sealed record PolicyListItem(Guid Id, string Name, Guid? SiteId, string ScopeKey, int CurrentVersion, long Version, PolicyVersionItem[] Versions);
    private sealed record PolicyVersionItem(int Version, string Name, Guid? SiteId, SlaPolicyDefinition Definition, DateTimeOffset PublishedAt);
}
