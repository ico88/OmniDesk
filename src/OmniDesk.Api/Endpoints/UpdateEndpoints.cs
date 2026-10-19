using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Contracts;
namespace OmniDesk.Api.Endpoints;
public static class UpdateEndpoints
{
    private static readonly Version MinimumUpdaterVersion = new(0, 8, 1);
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        var adminUpdates = app.MapGroup("/api/admin/devices/{id:guid}/agent-update").RequireAuthorization().AddEndpointFilter(async (c, next) =>
            (await c.HttpContext.RequestServices.GetRequiredService<PermissionService>().Global(c.HttpContext.User.UserId())).Contains("administration")
                ? await next(c) : Results.Forbid());
        adminUpdates.MapPost("", async (Guid id, AgentUpdateRequest request, HttpContext c, AppDbContext db, IConfiguration config) => {
            if (request.RequestId == Guid.Empty) return Results.BadRequest();
            var release = DownloadEndpoints.ReadRelease(config);
            if (release?.MsiSha256 is null || release.MsiSize is null) return Results.Conflict(new { code = "release_unavailable", message = "La release Windows pubblicata non include metadati MSI verificabili." });
            var metrics = await db.DeviceMetrics.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id);
            var deviceVersion = metrics is null ? null : JsonSerializer.Deserialize<MetricsRequest>(metrics.Payload)?.AgentVersion;
            if (metrics is null || metrics.ReceivedAt < DateTimeOffset.UtcNow.AddMinutes(-3)) return Results.Conflict(new { code = "device_offline", message = "Il dispositivo non invia heartbeat recenti." });
            if (!Version.TryParse(deviceVersion, out var installed) || !Version.TryParse(release.Version, out var target)) return Results.Conflict(new { code = "agent_version_unknown", message = "La versione Agent non è nota; aggiorna il dispositivo con MSI una volta." });
            if (installed < MinimumUpdaterVersion) return Results.Conflict(new { code = "updater_unavailable", message = "Questa versione Agent non include l’aggiornatore remoto; installa una volta l’MSI 0.8.1 o successivo." });
            if (installed >= target) return Results.Conflict(new { code = "already_current", message = "L’Agent installato è già aggiornato." });
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return Results.Conflict();
            var existing = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id && x.RequestId == request.RequestId);
            if (existing is not null) return existing.Kind == "agent.update" && existing.Payload == JsonSerializer.Serialize(new AgentUpdatePackage(release.Version, release.MsiSha256, release.MsiSize.Value))
                ? Results.Ok(new { existing.Id, existing.State }) : Results.Conflict();
            var active = await db.UpdateJobs.AnyAsync(x => x.DeviceId == id && (x.State == "queued" || x.State == "leased"));
            if (active) return Results.Conflict(new { code = "job_active", message = "È già in corso un lavoro per questo dispositivo." });
            var package = new AgentUpdatePackage(release.Version, release.MsiSha256, release.MsiSize.Value);
            var job = new UpdateJob { DeviceId = id, RequestId = request.RequestId, RequestedBy = c.User.UserId(), Kind = "agent.update", Payload = JsonSerializer.Serialize(package) };
            db.UpdateJobs.Add(job); db.AuditEvents.Add(Event(c.User.UserId(), "agent.update.queue", job.Id));
            try { await db.SaveChangesAsync(); } catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(); }
            await tx.CommitAsync(); return Results.Created($"/api/devices/{id}/updates/{job.Id}", new { job.Id, job.State, targetVersion = release.Version });
        });
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
            var authorized = job.Kind == "agent.update"
                ? (await permissions.Global(job.RequestedBy)).Contains("administration")
                : await permissions.Allows(job.RequestedBy, "updates", id);
            if (!authorized)
            {
                await db.UpdateJobs.Where(x => x.Id == job.Id && x.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.CompletedAt, now));
                db.AuditEvents.Add(Event(null, "update.cancel.authorization", job.Id));
                await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
            }
            var lease = Guid.NewGuid(); var expiry = now.AddMinutes(65);
            var changed = await db.UpdateJobs.Where(x => x.Id == job.Id && x.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "leased").SetProperty(x => x.LeaseId, lease).SetProperty(x => x.LeaseExpiresAt, expiry));
            if (changed != 1) return Results.NoContent();
            db.AuditEvents.Add(Event(null, "update.claim", job.Id)); await db.SaveChangesAsync(); await tx.CommitAsync();
            var updatePackage = job.Kind == "agent.update" ? JsonSerializer.Deserialize<AgentUpdatePackage>(job.Payload) : null;
            return Results.Ok(new UpdateLease(job.Id, lease, job.Kind, job.Kind == "agent.update" ? [] : JsonSerializer.Deserialize<UpdateSelection[]>(job.Payload)!, expiry, updatePackage));
        });
        agent.MapGet("/{jobId:guid}/package", async (Guid jobId, Guid leaseId, HttpContext c, AppDbContext db, IConfiguration config) => {
            var deviceId = Guid.Parse(c.User.UserId()); var now = DateTimeOffset.UtcNow;
            var job = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId && x.DeviceId == deviceId && x.Kind == "agent.update" && x.State == "leased" && x.LeaseId == leaseId && x.LeaseExpiresAt > now);
            if (job is null) return Results.NotFound();
            var requested = JsonSerializer.Deserialize<AgentUpdatePackage>(job.Payload);
            var release = DownloadEndpoints.ReadRelease(config);
            if (requested is null || release is null || release.Version != requested.Version || release.MsiSha256 != requested.Sha256 || release.MsiSize != requested.Size) return Results.Conflict();
            var path = Path.Combine(config["Downloads:Directory"]!, release.FileName);
            await using var archiveStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            var archiveHash = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, c.RequestAborted)).ToLowerInvariant();
            if (archiveHash != release.Sha256) return Results.Problem("Package integrity check failed", statusCode: 503);
            archiveStream.Position = 0;
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry("OmniDesk.Agent.msi");
            if (entry is null || entry.Length != requested.Size || entry.Length > 268435456) return Results.Problem("MSI entry is invalid", statusCode: 503);
            var content = new MemoryStream(checked((int)entry.Length));
            await using (var input = entry.Open())
            {
                var buffer = new byte[65536]; int read;
                while ((read = await input.ReadAsync(buffer, c.RequestAborted)) > 0)
                {
                    if (content.Length + read > requested.Size) return Results.Problem("MSI entry exceeds declared size", statusCode: 503);
                    await content.WriteAsync(buffer.AsMemory(0, read), c.RequestAborted);
                }
            }
            if (content.Length != requested.Size || Convert.ToHexString(SHA256.HashData(content.ToArray())).ToLowerInvariant() != requested.Sha256) return Results.Problem("MSI integrity check failed", statusCode: 503);
            db.AuditEvents.Add(Event(null, "agent.update.package", job.Id)); await db.SaveChangesAsync(c.RequestAborted);
            content.Position = 0; return Results.File(content, "application/octet-stream", $"OmniDesk.Agent-{requested.Version}.msi");
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
