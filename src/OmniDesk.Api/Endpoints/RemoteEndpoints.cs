using System.Text.RegularExpressions;
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
            if (r.NodeId is null || !Regex.IsMatch(r.NodeId, @"^node/[A-Za-z0-9_-]*/[A-Za-z0-9@$+=_-]{8,128}$") || !await db.Devices.AnyAsync(x => x.Id == id)) return Results.BadRequest();
            if (await db.MeshDeviceMappings.AnyAsync(x => x.NodeId == r.NodeId && x.DeviceId != id)) return Results.Conflict();
            var mapping = await db.MeshDeviceMappings.FindAsync(id);
            if (mapping is null) db.MeshDeviceMappings.Add(new MeshDeviceMapping { DeviceId = id, NodeId = r.NodeId }); else mapping.NodeId = r.NodeId;
            db.AuditEvents.Add(Event(c, "mesh.mapping", id)); await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization();
        var devices = app.MapGroup("/api/devices/{id:guid}/remote-sessions").RequireAuthorization();
        devices.MapGet("", async (Guid id, HttpContext c, AppDbContext db, PermissionService p) => {
            if (!await p.Allows(c.User.UserId(), "view", id)) return Results.Forbid();
            return Results.Ok(await db.RemoteSessions.AsNoTracking().Where(x => x.DeviceId == id).OrderByDescending(x => x.CreatedAt).Take(100)
                .Select(x => new { x.Id, x.ActorId, x.Capability, x.State, x.CreatedAt, x.ExpiresAt }).ToListAsync());
        });
        devices.MapPost("", async (Guid id, RemoteRequest r, HttpContext c, AppDbContext db, PermissionService p, IMeshCentralClient mesh, ILoggerFactory logs) => {
            if (!Capabilities.Contains(r.Capability)) return Results.BadRequest();
            if (!await p.Allows(c.User.UserId(), r.Capability, id)) return Results.Forbid();
            if (!await db.Devices.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var mapping = await db.MeshDeviceMappings.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id);
            if (!mesh.Configured || mapping is null) return Results.Problem("MeshCentral or device mapping unavailable", statusCode:503);
            var session = new RemoteSession { DeviceId = id, ActorId = c.User.UserId(), Capability = r.Capability };
            db.RemoteSessions.Add(session); db.AuditEvents.Add(Event(c, "remote.request", session.Id)); await db.SaveChangesAsync();
            MeshSession? created = null;
            try
            {
                created = await mesh.Create(mapping.NodeId, r.Capability, c.User.UserId(), c.RequestAborted);
                session.PublicId = created.PublicId; session.ExpiresAt = created.ExpiresAt; session.State = "active";
                db.AuditEvents.Add(Event(c, "remote.create", session.Id)); await db.SaveChangesAsync();
                return Results.Created($"/api/devices/{id}/remote-sessions/{session.Id}", new { session.Id, created.Url, created.ExpiresAt });
            }
            catch (Exception e)
            {
                if (created is not null)
                {
                    try { await mesh.Revoke(mapping.NodeId, created.PublicId, CancellationToken.None); }
                    catch (Exception cleanup) { logs.CreateLogger("Remote").LogWarning("Mesh compensation failed: {Type}", cleanup.GetType().Name); }
                }
                session.State = "failed"; db.AuditEvents.Add(Event(c, "remote.failed", session.Id)); await db.SaveChangesAsync();
                logs.CreateLogger("Remote").LogWarning("Mesh session request failed: {Type}", e.GetType().Name);
                return Results.Problem("Remote engine unavailable", statusCode:503);
            }
        });
        devices.MapDelete("/{sessionId:guid}", async (Guid id, Guid sessionId, HttpContext c, AppDbContext db, PermissionService p, IMeshCentralClient mesh) => {
            var session = await db.RemoteSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.DeviceId == id);
            if (session is null) return Results.NotFound();
            if (!await p.Allows(c.User.UserId(), "administration") && session.ActorId != c.User.UserId()) return Results.Forbid();
            if (session.State is "revoked" or "failed") return Results.NoContent();
            var mapping = await db.MeshDeviceMappings.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id);
            if (mapping is null || session.PublicId is null) return Results.Conflict();
            try { await mesh.Revoke(mapping.NodeId, session.PublicId, c.RequestAborted); }
            catch { return Results.Problem("Remote revocation failed; session expires automatically", statusCode:503); }
            session.State = "revoked"; db.AuditEvents.Add(Event(c, "remote.revoke", session.Id)); await db.SaveChangesAsync(); return Results.NoContent();
        });
    }
    private static AuditEvent Event(HttpContext c, string action, Guid id) => new() { ActorId = c.User.UserId(), Action = action, Target = id.ToString() };
}
