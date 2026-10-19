using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Remote.MeshCentral;
namespace OmniDesk.Api.Endpoints;
public sealed record MeshMappingRequest(string NodeId);
public sealed record RemoteRequest(string Capability);
public static class RemoteEndpoints
{
    private static readonly string[] Capabilities = ["remote.control", "terminal", "files"];
    public static void MapRemoteEndpoints(this WebApplication app)
    {
        app.MapPut("/api/admin/devices/{id:guid}/mesh", async (Guid id, MeshMappingRequest r, HttpContext c, AppDbContext db, PermissionService p) => {
            if (!await p.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            if (r.NodeId is null || r.NodeId.Length > 128 || !Regex.IsMatch(r.NodeId, @"^node/[A-Za-z0-9_-]*/[A-Za-z0-9@$+=_-]{8,128}$") || !await db.Devices.AnyAsync(x => x.Id == id)) return Results.BadRequest();
            if (await db.MeshDeviceMappings.AnyAsync(x => x.NodeId == r.NodeId && x.DeviceId != id)) return Results.Conflict();
            var mapping = await db.MeshDeviceMappings.FindAsync(id);
            if (mapping is null) db.MeshDeviceMappings.Add(new MeshDeviceMapping { DeviceId = id, NodeId = r.NodeId }); else mapping.NodeId = r.NodeId;
            db.AuditEvents.Add(Event(c, "mesh.mapping", id)); await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization();
        var devices = app.MapGroup("/api/devices/{id:guid}/remote-sessions").RequireAuthorization();
        devices.MapGet("/availability", async (Guid id, HttpContext c, AppDbContext db, PermissionService p, IMeshCentralClient mesh) => {
            if (!await p.Allows(c.User.UserId(), "view", id)) return Results.Forbid();
            if (!await db.Devices.AnyAsync(d => d.Id == id)) return Results.NotFound();
            var mapping = await db.MeshDeviceMappings.AsNoTracking().SingleOrDefaultAsync(m => m.DeviceId == id);
            var revoked = await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt != null);
            var accessPolicy=await db.RemoteProvisionings.AsNoTracking().SingleOrDefaultAsync(x=>x.DeviceId==id);
            var admin = await p.Allows(c.User.UserId(), "administration");
            return Results.Ok(new { configured = mesh.Configured, mapped = mapping != null, revoked, enabled = accessPolicy?.Enabled ?? true,
                nodeId = admin ? mapping?.NodeId : null, unattendedAllowed=accessPolicy?.UnattendedAllowed??false });
        });
        devices.MapGet("", async (Guid id, HttpContext c, AppDbContext db, PermissionService p) => {
            if (!await p.Allows(c.User.UserId(), "view", id)) return Results.Forbid();
            return Results.Ok(await db.RemoteSessions.AsNoTracking().Where(x => x.DeviceId == id).OrderByDescending(x => x.CreatedAt).Take(100)
                .Select(x => new { x.Id, x.ActorId, x.Capability, x.State, x.CreatedAt, x.ExpiresAt, x.ConsentRequired }).ToListAsync());
        });
        devices.MapPost("", async (Guid id, RemoteRequest r, HttpContext c, AppDbContext db, UserManager<AppUser> users, PermissionService p, IMeshCentralClient mesh, ILoggerFactory logs) => {
            if (!Capabilities.Contains(r.Capability)) return Results.BadRequest();
            if (!await p.Allows(c.User.UserId(), r.Capability, id)) return Results.Forbid();
            if (!await db.Devices.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt != null)) return Results.Forbid();
            if (await db.RemoteProvisionings.AnyAsync(x => x.DeviceId == id && !x.Enabled)) return Results.Forbid();
            var mapping = await db.MeshDeviceMappings.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id);
            if (!mesh.Configured || mapping is null) return Results.Problem("MeshCentral or device mapping unavailable", statusCode:503);
            var policy=await db.RemoteProvisionings.AsNoTracking().SingleOrDefaultAsync(x=>x.DeviceId==id);
            var session = new RemoteSession { DeviceId = id, NodeId = mapping.NodeId, ActorId = c.User.UserId(), Capability = r.Capability, ConsentRequired=!(policy?.UnattendedAllowed??false), AccessPolicyVersion=policy?.AccessPolicyVersion??0 };
            db.RemoteSessions.Add(session); db.AuditEvents.Add(Event(c, "remote.request", session.Id)); if(!session.ConsentRequired)db.AuditEvents.Add(Event(c,"remote.unattended.request",session.Id)); await db.SaveChangesAsync();
            MeshSession? created = null;
            try
            {
                var operatorUser = await users.FindByIdAsync(c.User.UserId());
                var operatorName = operatorUser?.DisplayName;
                if (string.IsNullOrWhiteSpace(operatorName)) operatorName = operatorUser?.Email;
                if (string.IsNullOrWhiteSpace(operatorName)) operatorName = "Operatore OmniDesk";
                created = await mesh.Create(mapping.NodeId, r.Capability, operatorName, session.ConsentRequired, c.RequestAborted);
                session.PublicId = created.PublicId; session.ExpiresAt = created.ExpiresAt; session.State = "active";
                db.AuditEvents.Add(Event(c, "remote.create", session.Id)); await db.SaveChangesAsync();
                if (!await p.Allows(c.User.UserId(), r.Capability, id) || await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt != null) || await db.RemoteProvisionings.AnyAsync(x => x.DeviceId == id && (!x.Enabled || x.AccessPolicyVersion != session.AccessPolicyVersion)) || (!session.ConsentRequired && !await db.RemoteProvisionings.AnyAsync(x=>x.DeviceId==id&&x.Enabled&&x.UnattendedAllowed)))
                {
                    session.State = "revocation_pending";
                    db.AuditEvents.Add(Event(c, "remote.authorization.changed", session.Id)); await db.SaveChangesAsync();
                    return Results.Forbid();
                }
                return Results.Created($"/api/devices/{id}/remote-sessions/{session.Id}", new { session.Id, created.Url, created.ExpiresAt });
            }
            catch (Exception e)
            {
                var cleanupPending = false;
                if (created is not null)
                {
                    try { await mesh.Revoke(mapping.NodeId, created.PublicId, CancellationToken.None); }
                    catch (Exception cleanup) { cleanupPending = true; logs.CreateLogger("Remote").LogWarning("Mesh compensation failed: {Type}", cleanup.GetType().Name); }
                }
                session.State = cleanupPending ? "revocation_pending" : "failed"; db.AuditEvents.Add(Event(c, "remote.failed", session.Id)); await db.SaveChangesAsync();
                logs.CreateLogger("Remote").LogWarning("Mesh session request failed: {Type}", e.GetType().Name);
                return Results.Problem("Remote engine unavailable", statusCode:503);
            }
        });
        devices.MapPost("/{sessionId:guid}/disconnect", async (Guid id, Guid sessionId, HttpContext c, AppDbContext db, PermissionService p, IMeshCentralClient mesh) => {
            var session = await db.RemoteSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.DeviceId == id);
            if (session is null) return Results.NotFound();
            var userId = c.User.UserId();
            if (!await p.Allows(userId, "administration") && session.ActorId != userId) return Results.Forbid();
            if (session.Capability != "remote.control") return Results.BadRequest();
            if (session.State != "active" || session.ExpiresAt is null || session.ExpiresAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(session.PublicId))
                return Results.Conflict();
            if (!await p.Allows(userId, "administration") && !await p.Allows(userId, "remote.control", id)) return Results.Forbid();
            if (await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt != null) ||
                await db.RemoteProvisionings.AnyAsync(x => x.DeviceId == id && !x.Enabled)) return Results.Forbid();
            await mesh.Disconnect(session.NodeId, session.PublicId, c.RequestAborted);
            db.AuditEvents.Add(Event(c, "remote.disconnect", session.Id));
            await db.SaveChangesAsync();
            return Results.Accepted(value: new { session.Id, state = "disconnect_requested", reconnectAllowedUntil = session.ExpiresAt });
        });
        devices.MapDelete("/{sessionId:guid}", async (Guid id, Guid sessionId, HttpContext c, AppDbContext db, PermissionService p, IMeshCentralClient mesh) => {
            var session = await db.RemoteSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.DeviceId == id);
            if (session is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "administration") && session.ActorId != c.User.UserId()) return Results.Forbid();
            if (session.State is "revoked" or "failed") return Results.NoContent();
            // Creation has not returned an engine identity yet; do not race its pending-to-active commit.
            if (session.State == "pending") return Results.Conflict();
            // Persist intent before contacting the engine. A timeout, request cancellation or API
            // restart must not discard an operator's request to close an existing session.
            await using (var intent = await db.Database.BeginTransactionAsync())
            {
                var changed = await db.RemoteSessions.Where(x => x.Id == sessionId && x.DeviceId == id &&
                    x.State != "revoked" && x.State != "failed" && x.State != "revocation_pending")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "revocation_pending"));
                if (changed > 0)
                {
                    db.AuditEvents.Add(Event(c, "remote.revoke.request", session.Id));
                    await db.SaveChangesAsync();
                }
                await intent.CommitAsync();
            }
            // The tracked object predates ExecuteUpdate; avoid overwriting newer durable state.
            await db.Entry(session).ReloadAsync();
            if (session.State is "revoked" or "failed") return Results.NoContent();
            if (string.IsNullOrEmpty(session.NodeId) || session.PublicId is null)
                return Results.Accepted(value: new { session.Id, state = "revocation_pending" });
            try { await mesh.Revoke(session.NodeId, session.PublicId, c.RequestAborted); }
            catch
            {
                return Results.Accepted(value: new { session.Id, state = "revocation_pending" });
            }
            await using var tx = await db.Database.BeginTransactionAsync();
            var completed = await db.RemoteSessions.Where(x => x.Id == sessionId && x.State == "revocation_pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "revoked"));
            if (completed > 0)
            {
                db.AuditEvents.Add(Event(c, "remote.revoke", session.Id));
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
            return Results.NoContent();
        });
    }
    private static AuditEvent Event(HttpContext c, string action, Guid id) => new() { ActorId = c.User.UserId(), Action = action, Target = id.ToString() };
}
