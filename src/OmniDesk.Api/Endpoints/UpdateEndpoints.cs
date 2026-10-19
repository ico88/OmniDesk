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
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return AgentUnavailable();
            var existing = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id && x.RequestId == request.RequestId);
            if (existing is not null) return existing.Kind == "agent.update" && existing.Payload == JsonSerializer.Serialize(new AgentUpdatePackage(release.Version, release.MsiSha256, release.MsiSize.Value))
                ? Results.Ok(new { existing.Id, existing.State }) : Results.Conflict();
            var active = await db.UpdateJobs.AnyAsync(x => x.DeviceId == id && !x.Kind.StartsWith("diagnostics.") && (x.State == "queued" || x.State == "leased"));
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
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return AgentUnavailable();
            var existing = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id && x.RequestId == r.RequestId);
            if (existing is not null)
                return existing.Kind == kind && JsonSerializer.Deserialize<UpdateSelection[]>(existing.Payload)!.SequenceEqual(r.Updates ?? [])
                    ? Results.Ok(new { existing.Id, existing.State }) : Results.Conflict();
            if (await db.UpdateJobs.AnyAsync(x => x.DeviceId == id && !x.Kind.StartsWith("diagnostics.") && (x.State == "queued" || x.State == "leased")))
                return Results.Conflict(new { code = "job_active", message = "Un altro intervento è già in coda o in esecuzione. Attendi il completamento e riprova." });
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
        var diagnostics = app.MapGroup("/api/devices/{id:guid}/diagnostics").RequireAuthorization().AddEndpointFilter(async (c, next) => {
            var id = Guid.Parse(c.HttpContext.Request.RouteValues["id"]!.ToString()!);
            return await c.HttpContext.RequestServices.GetRequiredService<PermissionService>().Allows(c.HttpContext.User.UserId(), "diagnostics.view", id) ? await next(c) : Results.Forbid();
        });
        diagnostics.MapGet("", async (Guid id, AppDbContext db) => Results.Ok(await db.UpdateJobs.AsNoTracking()
            .Where(x => x.DeviceId == id && x.Kind.StartsWith("diagnostics."))
            .OrderByDescending(x => x.CreatedAt).Take(30)
            .Select(x => new { x.Id, x.Kind, x.State, x.CreatedAt, x.CompletedAt, x.Result }).ToListAsync()));
        diagnostics.MapPost("/{kind}", async (Guid id, string kind, DiagnosticRequest r, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (kind is not ("processes" or "services" or "events") || r.RequestId == Guid.Empty) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await permissions.Allows(c.User.UserId(), "diagnostics.view", id)) return Results.Forbid();
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return AgentUnavailable();
            var jobKind = "diagnostics." + kind;
            var existing = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id && x.RequestId == r.RequestId);
            if (existing is not null) return existing.Kind == jobKind ? Results.Ok(new { existing.Id, existing.State }) : Results.Conflict();
            var pending = await db.UpdateJobs.AsNoTracking().Where(x => x.DeviceId == id && x.Kind == jobKind && (x.State == "queued" || x.State == "leased"))
                .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
            if (pending is not null) return Results.Ok(new { pending.Id, pending.State });
            var job = new UpdateJob { DeviceId = id, RequestId = r.RequestId, RequestedBy = c.User.UserId(), Kind = jobKind, Payload = "[]" };
            db.UpdateJobs.Add(job); db.AuditEvents.Add(Event(c.User.UserId(), jobKind + ".queue", job.Id));
            try { await db.SaveChangesAsync(); } catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(); }
            await tx.CommitAsync(); return Results.Created($"/api/devices/{id}/diagnostics/{job.Id}", new { job.Id, job.State });
        });
        var services = app.MapGroup("/api/devices/{id:guid}/services").RequireAuthorization().AddEndpointFilter(async (c, next) => {
            var id = Guid.Parse(c.HttpContext.Request.RouteValues["id"]!.ToString()!);
            return await c.HttpContext.RequestServices.GetRequiredService<PermissionService>().Allows(c.HttpContext.User.UserId(), "services.manage", id) ? await next(c) : Results.Forbid();
        });
        services.MapGet("jobs", async (Guid id, AppDbContext db) => Results.Ok(await db.UpdateJobs.AsNoTracking()
            .Where(x => x.DeviceId == id && x.Kind.StartsWith("service."))
            .OrderByDescending(x => x.CreatedAt).Take(30)
            .Select(x => new { x.Id, x.Kind, x.State, x.CreatedAt, x.CompletedAt, x.Result }).ToListAsync()));
        services.MapPost("actions", async (Guid id, ServiceControlRequest r, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (r.RequestId == Guid.Empty || !ServiceControlPolicy.IsControllable(r.ServiceName) || !ServiceControlPolicy.IsValidAction(r.Action)) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await permissions.Allows(c.User.UserId(), "services.manage", id)) return Results.Forbid();
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return AgentUnavailable();
            var kind = "service." + r.Action;
            var control = new ServiceControlPayload(r.ServiceName, r.Action);
            var payload = JsonSerializer.Serialize(control);
            var existing = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id && x.RequestId == r.RequestId);
            if (existing is not null)
            {
                var existingControl = existing.Kind == kind ? JsonSerializer.Deserialize<ServiceControlPayload>(existing.Payload) : null;
                return existingControl == control ? Results.Ok(new { existing.Id, existing.State }) : Results.Conflict();
            }
            if (await db.UpdateJobs.AnyAsync(x => x.DeviceId == id && !x.Kind.StartsWith("diagnostics.") && (x.State == "queued" || x.State == "leased"))) return Results.Conflict(new { code = "job_active", message = "Un altro intervento è già in coda o in esecuzione. Attendi il completamento e riprova." });
            var job = new UpdateJob { DeviceId = id, RequestId = r.RequestId, RequestedBy = c.User.UserId(), Kind = kind, Payload = payload };
            db.UpdateJobs.Add(job);
            db.AuditEvents.Add(Event(c.User.UserId(), "service.control.queue", job.Id));
            CoreOutbox.Add(db, "service.action.queued", "device", id, new { jobId = job.Id, serviceName = r.ServiceName, action = r.Action }, c.TraceIdentifier);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(); }
            await tx.CommitAsync(); return Results.Created($"/api/devices/{id}/services/jobs/{job.Id}", new { job.Id, job.State });
        });
        services.MapDelete("jobs/{jobId:guid}", async (Guid id, Guid jobId, HttpContext c, AppDbContext db) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            var job = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId && x.DeviceId == id && x.Kind.StartsWith("service."));
            if (job is null) return Results.NotFound();
            var cancelled = await db.UpdateJobs.Where(x => x.Id == jobId && x.State == "queued").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow));
            if (cancelled != 1) return Results.Conflict();
            db.AuditEvents.Add(Event(c.User.UserId(), "service.control.cancel", jobId));
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        var processes = app.MapGroup("/api/devices/{id:guid}/processes").RequireAuthorization().AddEndpointFilter(async (c, next) => {
            var id = Guid.Parse(c.HttpContext.Request.RouteValues["id"]!.ToString()!);
            return await c.HttpContext.RequestServices.GetRequiredService<PermissionService>().Allows(c.HttpContext.User.UserId(), "processes.manage", id) ? await next(c) : Results.Forbid();
        });
        processes.MapGet("jobs", async (Guid id, AppDbContext db) => Results.Ok(await db.UpdateJobs.AsNoTracking()
            .Where(x => x.DeviceId == id && x.Kind == "process.kill").OrderByDescending(x => x.CreatedAt).Take(30)
            .Select(x => new { x.Id, x.Kind, x.State, x.CreatedAt, x.CompletedAt, x.Result, x.Payload }).ToListAsync()));
        processes.MapDelete("jobs/{jobId:guid}", async (Guid id, Guid jobId, HttpContext c, AppDbContext db, PermissionService permissions) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await permissions.Allows(c.User.UserId(), "processes.manage", id)) return Results.Forbid();
            var changed = await db.UpdateJobs.Where(x => x.Id == jobId && x.DeviceId == id && x.Kind == "process.kill" && x.State == "queued")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow));
            if (changed != 1) return Results.Conflict();
            db.AuditEvents.Add(Event(c.User.UserId(), "process.kill.cancel", jobId));
            CoreOutbox.Add(db, "process.action.cancelled", "device", id, new { jobId }, c.TraceIdentifier);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
        processes.MapPost("actions", async (Guid id, ProcessControlRequest r, HttpContext c, AppDbContext db, PermissionService permissions) => {
            if (r.RequestId == Guid.Empty || !ProcessControlPolicy.IsControllable(r.ProcessId, r.ProcessName, r.StartedAt)) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            if (!await permissions.Allows(c.User.UserId(), "processes.manage", id)) return Results.Forbid();
            if (!await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt == null)) return AgentUnavailable();
            var control = new ProcessControlPayload(r.ProcessId, Path.GetFileName(r.ProcessName), r.StartedAt);
            var payload = JsonSerializer.Serialize(control);
            var existing = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id && x.RequestId == r.RequestId);
            if (existing is not null)
            {
                var existingControl = existing.Kind == "process.kill" ? JsonSerializer.Deserialize<ProcessControlPayload>(existing.Payload) : null;
                return existingControl == control ? Results.Ok(new { existing.Id, existing.State }) : Results.Conflict();
            }
            if (await db.UpdateJobs.AnyAsync(x => x.DeviceId == id && !x.Kind.StartsWith("diagnostics.") && (x.State == "queued" || x.State == "leased"))) return Results.Conflict(new { code = "job_active", message = "Un altro intervento è già in coda o in esecuzione. Attendi il completamento e riprova." });
            var job = new UpdateJob { DeviceId = id, RequestId = r.RequestId, RequestedBy = c.User.UserId(), Kind = "process.kill", Payload = payload };
            db.UpdateJobs.Add(job);
            db.AuditEvents.Add(Event(c.User.UserId(), "process.kill.queue", job.Id));
            CoreOutbox.Add(db, "process.action.queued", "device", id, new { jobId = job.Id, processId = control.ProcessId, processName = control.ProcessName }, c.TraceIdentifier);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(); }
            await tx.CommitAsync(); return Results.Created($"/api/devices/{id}/processes/jobs/{job.Id}", new { job.Id, job.State });
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
                : job.Kind.StartsWith("diagnostics.")
                    ? (job.Kind is "diagnostics.processes" or "diagnostics.services" or "diagnostics.events") && await permissions.Allows(job.RequestedBy, "diagnostics.view", id)
                : job.Kind.StartsWith("service.")
                    ? (job.Kind is "service.start" or "service.stop" or "service.restart") && await permissions.Allows(job.RequestedBy, "services.manage", id)
                : job.Kind == "process.kill"
                    ? await permissions.Allows(job.RequestedBy, "processes.manage", id)
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
            var claimAction = job.Kind.StartsWith("diagnostics.") ? "diagnostics.claim" : job.Kind.StartsWith("service.") ? "service.control.claim" : job.Kind == "process.kill" ? "process.control.claim" : "update.claim";
            db.AuditEvents.Add(Event(null, claimAction, job.Id)); await db.SaveChangesAsync(); await tx.CommitAsync();
            var updatePackage = job.Kind == "agent.update" ? JsonSerializer.Deserialize<AgentUpdatePackage>(job.Payload) : null;
            var serviceControl = job.Kind.StartsWith("service.") ? JsonSerializer.Deserialize<ServiceControlPayload>(job.Payload) : null;
            var processControl = job.Kind == "process.kill" ? JsonSerializer.Deserialize<ProcessControlPayload>(job.Payload) : null;
            var selections = job.Kind == "agent.update" || job.Kind.StartsWith("service.") || job.Kind == "process.kill" ? [] : JsonSerializer.Deserialize<UpdateSelection[]>(job.Payload)!;
            return Results.Ok(new UpdateLease(job.Id, lease, job.Kind, selections, expiry, updatePackage, serviceControl, processControl));
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
            if (r.Updates is null || r.Updates.Length > 2000 || r.Error?.Length > 1000 || r.DiagnosticJson?.Length > 1_000_000 || r.Updates.Any(x => x is null || x.UpdateId == Guid.Empty || x.Revision < 0 || x.Title is null || x.Title.Length > 500 || x.Kb is null || x.Kb.Length > 30 || x.Kb.Any(k => k is null || k.Length > 50))) return Results.BadRequest();
            var id = Guid.Parse(c.User.UserId()); var now = DateTimeOffset.UtcNow;
            var job = await db.UpdateJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId && x.DeviceId == id && x.LeaseId == r.LeaseId);
            if (job is null) return Results.NotFound();
            if (job.Kind.StartsWith("diagnostics.") && (r.Succeeded ? r.DiagnosticJson is null || !ValidDiagnostic(job.Kind, r.DiagnosticJson) : r.DiagnosticJson is not null)) return Results.BadRequest();
            if (!job.Kind.StartsWith("diagnostics.") && r.DiagnosticJson is not null) return Results.BadRequest();
            if ((job.State is "succeeded" or "failed") && job.Result is not null) return Results.NoContent();
            await using var tx = await db.Database.BeginTransactionAsync();
            var payload = JsonSerializer.Serialize(r); var state = r.Succeeded ? "succeeded" : "failed";
            var changed = await db.UpdateJobs.Where(x => x.Id == jobId && x.DeviceId == id && x.State == "leased" && x.LeaseId == r.LeaseId && x.LeaseExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, state).SetProperty(x => x.Result, payload).SetProperty(x => x.CompletedAt, now));
            if (changed != 1) return Results.Conflict();
            var resultAction = job.Kind.StartsWith("diagnostics.") ? "diagnostics.result." : job.Kind.StartsWith("service.") ? "service.control.result." : job.Kind == "process.kill" ? "process.control.result." : "update.result.";
            db.AuditEvents.Add(Event(null, resultAction + state, jobId)); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        });
    }
    private static AuditEvent Event(string? actor, string action, Guid id) => new() { ActorId = actor, Action = action, Target = id.ToString() };
    private static IResult AgentUnavailable() => Results.Conflict(new {
        code = "agent_credential_unavailable",
        message = "Identità Agent assente o revocata: le operazioni sul dispositivo sono disabilitate. Verifica lo stato e la registrazione dell’Agent."
    });
    private static bool ValidDiagnostic(string kind, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Kind", out var k) || k.GetString() != kind || !root.TryGetProperty("CollectedAt", out _)) return false;
            var property = kind switch { "diagnostics.processes" => "Processes", "diagnostics.services" => "Services", "diagnostics.events" => "Events", _ => "" };
            if (!root.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > (kind == "diagnostics.events" ? 100 : 500)) return false;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return false;
                foreach (var value in item.EnumerateObject()) if (value.Value.ValueKind == JsonValueKind.String && value.Value.GetString() is { Length: > 1000 }) return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }
}
