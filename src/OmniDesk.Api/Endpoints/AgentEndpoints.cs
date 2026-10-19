using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Contracts;

namespace OmniDesk.Api.Endpoints;
public sealed record TokenRequest(Guid? GroupId, int ExpiresInMinutes = 30);
public sealed record GroupRequest(string Name);
public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization().AddEndpointFilter(async (c, next) =>
            await c.HttpContext.RequestServices.GetRequiredService<PermissionService>().Allows(c.HttpContext.User.UserId(), "administration")
                ? await next(c) : Results.Forbid());
        admin.MapGet("/groups", async (AppDbContext db) => Results.Ok(await db.DeviceGroups.AsNoTracking().OrderBy(x => x.Name).ToListAsync()));
        admin.MapPost("/groups", async (GroupRequest r, HttpContext c, AppDbContext db) => {
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200) return Results.BadRequest();
            var group = new DeviceGroup { Id = Guid.NewGuid(), Name = r.Name.Trim() };
            db.DeviceGroups.Add(group); db.AuditEvents.Add(Audit(c.User.UserId(), "group.create", group.Id));
            await db.SaveChangesAsync(); return Results.Created($"/api/admin/groups/{group.Id}", group);
        });
        admin.MapPost("/enrollment-tokens", async (TokenRequest r, HttpContext c, AppDbContext db) => {
            if (r.ExpiresInMinutes is < 1 or > 1440 || (r.GroupId is not null && !await db.DeviceGroups.AnyAsync(x => x.Id == r.GroupId))) return Results.BadRequest();
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var token = new EnrollmentToken { TokenHash = Hash(secret), GroupId = r.GroupId, CreatedBy = c.User.UserId(), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(r.ExpiresInMinutes) };
            db.EnrollmentTokens.Add(token); db.AuditEvents.Add(Audit(c.User.UserId(), "enrollment.issue", token.Id));
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/enrollment-tokens/{token.Id}", new { token.Id, token.ExpiresAt, token = secret });
        });
        admin.MapGet("/enrollment-tokens", async (AppDbContext db) => Results.Ok(await db.EnrollmentTokens.AsNoTracking().OrderByDescending(x => x.ExpiresAt)
            .Take(100).Select(x => new { x.Id, x.GroupId, x.ExpiresAt, x.UsedAt, x.RevokedAt }).ToListAsync()));
        admin.MapDelete("/enrollment-tokens/{id:guid}", async (Guid id, HttpContext c, AppDbContext db) => {
            var token = await db.EnrollmentTokens.FindAsync(id); if (token is null) return Results.NotFound();
            token.RevokedAt = DateTimeOffset.UtcNow; db.AuditEvents.Add(Audit(c.User.UserId(), "enrollment.revoke", id));
            await db.SaveChangesAsync(); return Results.NoContent();
        });
        var machine = app.MapGroup("/api/agent").WithMetadata(new MachineRequest());
        machine.MapPost("/enroll", async (EnrollmentRequest r, AppDbContext db) => {
            if (r.Token is null || r.Token.Length != 64 || !r.Token.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || r.PublicKeyPem is null || r.PublicKeyPem.Length > 4096) return Results.BadRequest();
            try { using var rsa = RSA.Create(); rsa.ImportFromPem(r.PublicKeyPem); if (rsa.KeySize is < 2048 or > 4096 || r.PublicKeyPem.Contains("PRIVATE", StringComparison.Ordinal)) return Results.BadRequest(); }
            catch (Exception e) when (e is CryptographicException or ArgumentException) { return Results.BadRequest(); }
            var hash = Hash(r.Token); var now = DateTimeOffset.UtcNow;
            await using var tx = await db.Database.BeginTransactionAsync();
            var enrollment = await db.EnrollmentTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
            if (enrollment is null) return Results.Unauthorized();
            var consumed = await db.EnrollmentTokens.Where(x => x.Id == enrollment.Id && x.UsedAt == null && x.RevokedAt == null && x.ExpiresAt > now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now));
            if (consumed != 1) return Results.Unauthorized();
            var device = new Device { Id = Guid.NewGuid(), Name = r.Name.Trim(), GroupId = enrollment.GroupId };
            db.Devices.Add(device); db.DeviceCredentials.Add(new DeviceCredential { DeviceId = device.Id, PublicKeyPem = r.PublicKeyPem });
            db.AuditEvents.Add(Audit(null, "device.enroll", device.Id)); await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Created($"/api/devices/{device.Id}", new EnrollmentResponse(device.Id));
        }).RequireRateLimiting("enrollment");
        var authenticated = machine.MapGroup("").RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = DeviceAuthentication.SchemeName });
        authenticated.MapPost("/uninstall", async (HttpContext c, AppDbContext db) => {
            var id = Guid.Parse(c.User.UserId());
            await using var tx = await db.Database.BeginTransactionAsync();
            await DeviceOperationLock.Acquire(db, id);
            var now = DateTimeOffset.UtcNow;
            var revoked = await db.DeviceCredentials.Where(x => x.DeviceId == id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));
            if (revoked == 0) return Results.Unauthorized();
            var jobs = await db.UpdateJobs.Where(x => x.DeviceId == id && x.State == "queued").Select(x => x.Id).ToListAsync();
            await db.UpdateJobs.Where(x => x.DeviceId == id && x.State == "queued")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.CompletedAt, now));
            await db.RemoteSessions.Where(x => x.DeviceId == id && x.State != "revoked" && x.State != "failed" && x.State != "revocation_pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "revocation_pending"));
            await db.RemoteProvisionings.Where(x => x.DeviceId == id && x.Enabled)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled, false).SetProperty(x => x.UnattendedAllowed, false));
            foreach (var job in jobs) db.AuditEvents.Add(Audit(null, "update.cancel.device_uninstall", job));
            db.AuditEvents.Add(Audit(null, "device.self_uninstall", id));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        authenticated.MapPost("/metrics", async (MetricsRequest r, HttpContext c, AppDbContext db) => {
            if (!Valid(r)) return Results.BadRequest();
            var id = Guid.Parse(c.User.UserId()); var now = DateTimeOffset.UtcNow;
            var payload = JsonSerializer.Serialize(r);
            var updated = await db.DeviceMetrics.Where(x => x.DeviceId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReceivedAt, now).SetProperty(x => x.Payload, payload));
            if (updated == 0) { db.DeviceMetrics.Add(new DeviceMetrics { DeviceId = id, ReceivedAt = now, Payload = payload }); await db.SaveChangesAsync(); }
            return Results.NoContent();
        });
        authenticated.MapPut("/inventory", async (InventoryRequest r, HttpContext c, AppDbContext db) => {
            if (string.IsNullOrWhiteSpace(r.OperatingSystem) || r.OperatingSystem.Length > 500 || string.IsNullOrWhiteSpace(r.Architecture) || r.Architecture.Length > 100 ||
                new[] { r.Manufacturer, r.Model, r.SerialNumber, r.Processor }.Any(x => x?.Length > 500) ||
                r.LogicalProcessors is < 1 or > 65536 || r.Software is null || r.Software.Length > 2000 ||
                r.Software.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 500 || x.Version?.Length > 200 || x.Publisher?.Length > 500) ||
                r.Warnings is null || r.Warnings.Length > 50 || r.Warnings.Any(x => x is null || x.Length > 500)) return Results.BadRequest();
            var id = Guid.Parse(c.User.UserId()); var now = DateTimeOffset.UtcNow; var payload = JsonSerializer.Serialize(r);
            var updated = await db.DeviceInventories.Where(x => x.DeviceId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReceivedAt, now).SetProperty(x => x.Payload, payload));
            if (updated == 0) { db.DeviceInventories.Add(new DeviceInventory { DeviceId = id, ReceivedAt = now, Payload = payload }); await db.SaveChangesAsync(); }
            return Results.NoContent();
        });
        var read = app.MapGroup("/api/devices").RequireAuthorization();
        read.MapGet("/{id:guid}/status", async (Guid id, HttpContext c, AppDbContext db, PermissionService p) => {
            if (!await p.Allows(c.User.UserId(), "view", id)) return Results.Forbid();
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id); if (device is null) return Results.NotFound();
            var metrics = await db.DeviceMetrics.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id);
            var inventory = await db.DeviceInventories.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id);
            var revoked = await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt != null);
            var capabilities = new List<string>();
            foreach (var capability in Permissions.All) if (await p.Allows(c.User.UserId(), capability, id)) capabilities.Add(capability);
            return Results.Ok(new { device.Id, device.Name, device.GroupId, revoked, capabilities,
                lastSeen = metrics?.ReceivedAt, online = !revoked && metrics?.ReceivedAt > DateTimeOffset.UtcNow.AddMinutes(-3),
                metrics = metrics is null ? null : JsonSerializer.Deserialize<MetricsRequest>(metrics.Payload),
                inventoryAt = inventory?.ReceivedAt, inventory = inventory is null ? null : JsonSerializer.Deserialize<InventoryRequest>(inventory.Payload) });
        });
    }
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static AuditEvent Audit(string? actor, string action, Guid id) => new() { ActorId = actor, Action = action, Target = id.ToString() };
    private static bool Valid(MetricsRequest r) => (r.CpuPercent is null || (double.IsFinite(r.CpuPercent.Value) && r.CpuPercent is >= 0 and <= 100)) &&
        r.TotalMemoryBytes > 0 && r.AvailableMemoryBytes >= 0 && r.AvailableMemoryBytes <= r.TotalMemoryBytes &&
        r.NetworkReceivedBytes >= 0 && r.NetworkSentBytes >= 0 && r.UptimeSeconds >= 0 && r.Disks is not null && r.Disks.Length <= 64 &&
        (r.AgentVersion is null || (r.AgentVersion.Length <= 64 && System.Text.RegularExpressions.Regex.IsMatch(r.AgentVersion, @"\A[0-9]+\.[0-9]+\.[0-9]+\z"))) &&
        r.Disks.All(x => x is not null && !string.IsNullOrWhiteSpace(x.Name) && x.Name.Length <= 200 && x.TotalBytes > 0 && x.FreeBytes >= 0 && x.FreeBytes <= x.TotalBytes);
}
