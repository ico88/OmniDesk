using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;

public sealed record DeviceEditRequest(string Name, Guid? GroupId, string ExpectedName, Guid? ExpectedGroupId, Guid? SiteId = null, Guid? ExpectedSiteId = null);
public static class DeviceManagementEndpoints
{
    public static void MapDeviceManagementEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/devices").RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
            await ctx.HttpContext.RequestServices.GetRequiredService<PermissionService>().Allows(ctx.HttpContext.User.UserId(), "administration")
                ? await next(ctx) : Results.Forbid());
        admin.MapPut("/{id:guid}", async (Guid id, DeviceEditRequest r, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || string.IsNullOrWhiteSpace(r.ExpectedName) || r.ExpectedName.Length > 200) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await permissions.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (device is null) return Results.NotFound();
            if (r.GroupId is not null && !await db.DeviceGroups.AnyAsync(x => x.Id == r.GroupId)) return Results.BadRequest();
            var newSiteId = r.SiteId ?? device.SiteId;
            if (!await db.Sites.AnyAsync(x => x.Id == newSiteId && !x.IsArchived)) return Results.BadRequest();
            if (device.Name != r.ExpectedName || device.GroupId != r.ExpectedGroupId || (r.ExpectedSiteId is not null && device.SiteId != r.ExpectedSiteId))
                return Results.Conflict(new { error = "Dispositivo modificato da un altro operatore. Ricarica i dati prima di salvare." });
            var name = r.Name.Trim();
            if (device.Name == name && device.GroupId == r.GroupId && device.SiteId == newSiteId) return Results.NoContent();
            await db.Devices.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, name).SetProperty(x => x.GroupId, r.GroupId).SetProperty(x => x.SiteId, newSiteId));
            db.AuditEvents.Add(Event(c, "device.metadata.change", id));
            CoreOutbox.Add(db, "device.updated", "device", id, new
            {
                deviceId = id,
                oldName = device.Name,
                name,
                oldGroupId = device.GroupId,
                groupId = r.GroupId,
                oldSiteId = device.SiteId,
                siteId = newSiteId
            }, c.TraceIdentifier);
            if (device.GroupId != r.GroupId || device.SiteId != newSiteId)
            {
                // Close existing shares when moving an endpoint across an authorization boundary.
                await db.RemoteSessions.Where(x => x.DeviceId == id && x.State == "active").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "revocation_pending"));
                var queued = await db.UpdateJobs.AsNoTracking().Where(x => x.DeviceId == id && x.State == "queued").ToListAsync();
                foreach (var job in queued)
                {
                    var allowed = job.Kind.StartsWith("diagnostics.")
                        ? (job.Kind is "diagnostics.processes" or "diagnostics.services" or "diagnostics.events") && await permissions.Allows(job.RequestedBy, "diagnostics.view", id)
                        : job.Kind == "agent.update" ? (await permissions.Global(job.RequestedBy)).Contains("administration")
                        : await permissions.Allows(job.RequestedBy, "updates", id);
                    if (!allowed)
                    {
                        await db.UpdateJobs.Where(x => x.Id == job.Id && x.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow));
                        db.AuditEvents.Add(Event(c, job.Kind.StartsWith("diagnostics.") ? "diagnostics.cancel.authorization" : "update.cancel.authorization", job.Id));
                    }
                }
                db.AuditEvents.Add(Event(c, "device.group.change", id));
            }
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.NoContent();
        });
        admin.MapPost("/{id:guid}/revoke", async (Guid id, HttpContext c, AppDbContext db, PermissionService permissions) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await permissions.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id)) return Results.NotFound();
            var now = DateTimeOffset.UtcNow;
            var revoked = await db.DeviceCredentials.Where(x => x.DeviceId == id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
            var queuedIds = await db.UpdateJobs.Where(x => x.DeviceId == id && x.State == "queued").Select(x => x.Id).ToListAsync();
            await db.UpdateJobs.Where(x => x.DeviceId == id && x.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.CompletedAt, now));
            await db.RemoteSessions.Where(x => x.DeviceId == id && x.State == "active").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "revocation_pending"));
            foreach (var jobId in queuedIds) db.AuditEvents.Add(Event(c, "update.cancel.device_revoked", jobId));
            if (revoked > 0) db.AuditEvents.Add(Event(c, "device.revoke", id));
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.NoContent();
        });
    }
    private static AuditEvent Event(HttpContext c, string action, Guid id) => new() { ActorId = c.User.UserId(), Action = action, Target = id.ToString() };
}
