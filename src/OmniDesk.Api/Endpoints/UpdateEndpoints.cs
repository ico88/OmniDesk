using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Contracts;
namespace OmniDesk.Api.Endpoints;
public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        var management = app.MapGroup("/api/devices/{id:guid}/updates").RequireAuthorization().AddEndpointFilter(async (c, next) => {
            var id = Guid.Parse(c.HttpContext.Request.RouteValues["id"]!.ToString()!);
            return await c.HttpContext.RequestServices.GetRequiredService<PermissionService>().Allows(c.HttpContext.User.UserId(), "updates", id) ? await next(c) : Results.Forbid();
        });
        management.MapGet("", async (Guid id, AppDbContext db) => Results.Ok(await db.UpdateJobs.AsNoTracking().Where(x => x.DeviceId == id).OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new { x.Id, x.Kind, x.State, x.CreatedAt, x.CompletedAt, x.Result }).ToListAsync()));
        management.MapPost("/{kind}", async (Guid id, string kind, UpdateJobRequest r, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (kind is not ("scan" or "install") || r.RequestId == Guid.Empty ||
                (kind == "install" && (r.Updates is null || r.Updates.Length is < 1 or > 200 || r.Updates.Any(x => x is null || x.UpdateId == Guid.Empty || x.Revision < 0) || r.Updates.Select(x => x.UpdateId).Distinct().Count() != r.Updates.Length)) ||
                (kind == "scan" && r.Updates is { Length: > 0 })) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await permissions.Allows(c.User.UserId(), "updates", id)) return Results.Forbid();
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return Results.Conflict();
            var existing = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id && x.RequestId == r.RequestId);
            if (existing is not null)
                return existing.Kind == kind && JsonSerializer.Deserialize<UpdateSelection[]>(existing.Payload)!.SequenceEqual(r.Updates ?? [])
                    ? Results.Ok(new { existing.Id, existing.State }) : Results.Conflict();
            var job = new UpdateJob { DeviceId = id, RequestId = r.RequestId, RequestedBy = c.User.UserId(), Kind = kind, Payload = JsonSerializer.Serialize(r.Updates ?? []) };
            db.UpdateJobs.Add(job); db.AuditEvents.Add(Event(c.User.UserId(), "update.queue." + kind, job.Id));
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(); }
            await tx.CommitAsync();
            return Results.Created($"/api/devices/{id}/updates/{job.Id}", new { job.Id, job.State });
        });
        management.MapDelete("/{jobId:guid}", async (Guid id, Guid jobId, HttpContext c, AppDbContext db, PermissionService permissions) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await permissions.Allows(c.User.UserId(), "updates", id)) return Results.Forbid();
            var changed = await db.UpdateJobs.Where(x => x.Id == jobId && x.DeviceId == id && x.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow));
            if (changed != 1) return Results.Conflict();
            db.AuditEvents.Add(Event(c.User.UserId(), "update.cancel", jobId)); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        var agent = app.MapGroup("/api/agent/jobs").WithMetadata(new MachineRequest()).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = DeviceAuthentication.SchemeName });
        agent.MapPost("/claim", async (HttpContext c, AppDbContext db, PermissionService permissions) => {
            var id = Guid.Parse(c.User.UserId()); var now = DateTimeOffset.UtcNow;
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return Results.Unauthorized();
            var expiredIds = await db.UpdateJobs.Where(x => x.DeviceId == id && x.State == "leased" && x.LeaseExpiresAt < now).Select(x => x.Id).ToListAsync();
            await db.UpdateJobs.Where(x => x.DeviceId == id && x.State == "leased" && x.LeaseExpiresAt < now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "failed").SetProperty(x => x.CompletedAt, now));
            foreach (var expiredId in expiredIds) db.AuditEvents.Add(Event(null, "update.lease.expired", expiredId));
            var job = await db.UpdateJobs.AsNoTracking().Where(x => x.DeviceId == id && x.State == "queued").OrderBy(x => x.CreatedAt).FirstOrDefaultAsync();
            if (job is null) { await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent(); }
            if (!await permissions.Allows(job.RequestedBy, "updates", id))
            {
                await db.UpdateJobs.Where(x => x.Id == job.Id && x.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.CompletedAt, now));
                db.AuditEvents.Add(Event(null, "update.cancel.authorization", job.Id));
                await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
            }
            var lease = Guid.NewGuid(); var expiry = now.AddMinutes(65);
            var changed = await db.UpdateJobs.Where(x => x.Id == job.Id && x.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "leased").SetProperty(x => x.LeaseId, lease).SetProperty(x => x.LeaseExpiresAt, expiry));
            if (changed != 1) return Results.NoContent();
            db.AuditEvents.Add(Event(null, "update.claim", job.Id)); await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Ok(new UpdateLease(job.Id, lease, job.Kind, JsonSerializer.Deserialize<UpdateSelection[]>(job.Payload)!, expiry));
        });
        agent.MapPost("/{jobId:guid}/result", async (Guid jobId, UpdateResultRequest r, HttpContext c, AppDbContext db) => {
            if (r.Updates is null || r.Updates.Length > 2000 || r.Error?.Length > 1000 || r.Updates.Any(x => x is null || x.UpdateId == Guid.Empty || x.Revision < 0 || x.Title is null || x.Title.Length > 500 || x.Kb is null || x.Kb.Length > 30 || x.Kb.Any(k => k is null || k.Length > 50))) return Results.BadRequest();
            var id = Guid.Parse(c.User.UserId()); var now = DateTimeOffset.UtcNow;
            var job = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId && x.DeviceId == id && x.LeaseId == r.LeaseId);
            if (job is null) return Results.NotFound();
            if ((job.State is "succeeded" or "failed") && job.Result is not null) return Results.NoContent();
            await using var tx = await db.Database.BeginTransactionAsync();
            var payload = JsonSerializer.Serialize(r); var state = r.Succeeded ? "succeeded" : "failed";
            var changed = await db.UpdateJobs.Where(x => x.Id == jobId && x.DeviceId == id && x.State == "leased" && x.LeaseId == r.LeaseId && x.LeaseExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, state).SetProperty(x => x.Result, payload).SetProperty(x => x.CompletedAt, now));
            if (changed != 1) return Results.Conflict();
            db.AuditEvents.Add(Event(null, "update.result." + state, jobId)); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
    }
    private static AuditEvent Event(string? actor, string action, Guid id) => new() { ActorId = actor, Action = action, Target = id.ToString() };
}
