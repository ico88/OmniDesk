using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;

public sealed record RustDeskMappingRequest(string PeerId);

public static partial class RustDeskEndpoints
{
    [GeneratedRegex("^[0-9]{6,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex PeerIdPattern();

    [GeneratedRegex("^[A-Za-z0-9+/]{42,44}={0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex PublicKeyPattern();

    [GeneratedRegex("^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(?:\\.(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?))*$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    public static void MapRustDeskEndpoints(this WebApplication app)
    {
        app.MapGet("/api/devices/{id:guid}/rustdesk", async (Guid id, HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            var userId = c.User.UserId();
            if (!await permissions.Allows(userId, "view", id)) return Results.Forbid();
            if (!await db.Devices.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var allowed = await permissions.Allows(userId, "remote.control", id) || await permissions.Allows(userId, "administration");
            var mapping = await db.RustDeskDeviceMappings.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id);
            var server = app.Configuration["RustDesk:IdServer"] ?? "";
            var relay = app.Configuration["RustDesk:RelayServer"] ?? "";
            var key = app.Configuration["RustDesk:PublicKey"] ?? "";
            var configured = HostPattern().IsMatch(server) && HostPattern().IsMatch(relay) && PublicKeyPattern().IsMatch(key);
            return Results.Ok(new
            {
                configured,
                mapped = mapping is not null,
                peerId = allowed ? mapping?.PeerId : null,
                idServer = configured && allowed ? server : null,
                relayServer = configured && allowed ? relay : null,
                apiServer = configured && allowed ? "" : null,
                publicKey = configured && allowed ? key : null
            });
        }).RequireAuthorization();

        app.MapPut("/api/admin/devices/{id:guid}/rustdesk", async (Guid id, RustDeskMappingRequest request, HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            if (!await permissions.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            if (!PeerIdPattern().IsMatch(request.PeerId) || !await db.Devices.AnyAsync(x => x.Id == id)) return Results.BadRequest();
            if (await db.RustDeskDeviceMappings.AnyAsync(x => x.PeerId == request.PeerId && x.DeviceId != id)) return Results.Conflict();
            var mapping = await db.RustDeskDeviceMappings.FindAsync(id);
            if (mapping is null) db.RustDeskDeviceMappings.Add(new RustDeskDeviceMapping { DeviceId = id, PeerId = request.PeerId });
            else mapping.PeerId = request.PeerId;
            db.AuditEvents.Add(Event(c, "rustdesk.mapping", id));
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapDelete("/api/admin/devices/{id:guid}/rustdesk", async (Guid id, HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            if (!await permissions.Allows(c.User.UserId(), "administration")) return Results.Forbid();
            var mapping = await db.RustDeskDeviceMappings.FindAsync(id);
            if (mapping is null) return Results.NoContent();
            db.RustDeskDeviceMappings.Remove(mapping);
            db.AuditEvents.Add(Event(c, "rustdesk.mapping.removed", id));
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/api/devices/{id:guid}/rustdesk/connect", async (Guid id, HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            var userId = c.User.UserId();
            if (!await permissions.Allows(userId, "remote.control", id)) return Results.Forbid();
            if (!await db.Devices.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (await db.DeviceCredentials.AnyAsync(x => x.DeviceId == id && x.RevokedAt != null)) return Results.Forbid();
            var mapping = await db.RustDeskDeviceMappings.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id);
            if (mapping is null) return Results.Problem("RustDesk non è associato a questo dispositivo.", statusCode: 503);
            var server = app.Configuration["RustDesk:IdServer"] ?? "";
            var key = app.Configuration["RustDesk:PublicKey"] ?? "";
            if (!HostPattern().IsMatch(server) || !PublicKeyPattern().IsMatch(key))
                return Results.Problem("Configurazione RustDesk del server incompleta.", statusCode: 503);

            db.AuditEvents.Add(Event(c, "rustdesk.connect.requested", id));
            await db.SaveChangesAsync();
            var uri = $"rustdesk://connect/{mapping.PeerId}@{server}?key={Uri.EscapeDataString(key)}";
            return Results.Ok(new { url = uri });
        }).RequireAuthorization();
    }

    private static AuditEvent Event(HttpContext c, string action, Guid id) => new()
    {
        ActorId = c.User.UserId(),
        Action = action,
        Target = id.ToString()
    };
}
