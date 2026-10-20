using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/notifications").RequireAuthorization();
        api.MapGet("/devices", async (HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            var userId = c.User.UserId();
            var visibleDevices = permissions.VisibleDevices(userId).AsNoTracking();
            var candidates = await (from presence in db.DevicePresence.AsNoTracking()
                                    join device in visibleDevices on presence.DeviceId equals device.Id
                                    join metrics in db.DeviceMetrics.AsNoTracking() on device.Id equals metrics.DeviceId
                                    where !presence.IsOnline &&
                                          db.DeviceCredentials.Any(credential => credential.DeviceId == device.Id && credential.RevokedAt == null)
                                    orderby presence.ChangedAt descending
                                    select new { device.Id, device.Name, device.SiteId, presence.ChangedAt, metrics.ReceivedAt })
                .Take(50).Select(item => new { deviceId = item.Id, item.Name, item.ChangedAt, lastSeen = item.ReceivedAt })
                .ToArrayAsync();
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { offlineDevices = candidates });
        });
        api.MapGet("", async (HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            var userId = c.User.UserId();
            var rows = await db.UserNotifications.AsNoTracking().Where(notification => notification.UserId == userId)
                .OrderByDescending(notification => notification.CreatedAt).Take(100).ToArrayAsync();
            var visible = new List<object>();
            var unread = 0;
            foreach (var item in rows)
            {
                if (item.TicketId is { } ticketId)
                {
                    var ticket = await db.SupportTickets.AsNoTracking().Where(x => x.Id == ticketId)
                        .Select(x => new { x.Id, x.Number, x.Subject, x.DeviceId, x.SiteId }).SingleOrDefaultAsync();
                    if (ticket is null || !await permissions.Allows(userId, "tickets.view", ticket.DeviceId, siteId: ticket.SiteId)) continue;
                    if (item.ReadAt is null) unread++;
                    visible.Add(new { item.Id, item.Kind, item.CreatedAt, item.ReadAt, ticketId = ticket.Id,
                        ticket.Number, ticket.Subject, deviceId = (Guid?)null, deviceName = (string?)null });
                    if (visible.Count == 50) break;
                    continue;
                }
                if (item.DeviceId is not { } deviceId) continue;
                var device = await db.Devices.AsNoTracking().Where(x => x.Id == deviceId)
                    .Select(x => new { x.Id, x.Name, x.SiteId }).SingleOrDefaultAsync();
                if (device is null || !await db.DeviceCredentials.AnyAsync(x => x.DeviceId == deviceId && x.RevokedAt == null) ||
                    !await permissions.Allows(userId, "view", deviceId, siteId: device.SiteId)) continue;
                if (item.ReadAt is null) unread++;
                visible.Add(new { item.Id, item.Kind, item.CreatedAt, item.ReadAt, ticketId = (Guid?)null,
                    number = (long?)null, subject = (string?)null, deviceId = device.Id, deviceName = device.Name });
                if (visible.Count == 50) break;
            }
            c.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { unreadCount = unread, notifications = visible });
        });

        api.MapPost("/{id:guid}/read", async (Guid id, HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            var userId = c.User.UserId();
            var notification = await db.UserNotifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (notification is null) return Results.NotFound();
            if (notification.TicketId is { } ticketId)
            {
                var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId);
                if (ticket is null) return Results.NotFound();
                if (!await permissions.Allows(userId, "tickets.view", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            }
            else if (notification.DeviceId is { } deviceId)
            {
                var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId);
                if (device is null || !await db.DeviceCredentials.AnyAsync(x => x.DeviceId == deviceId && x.RevokedAt == null)) return Results.NotFound();
                if (!await permissions.Allows(userId, "view", deviceId, siteId: device.SiteId)) return Results.Forbid();
            }
            else return Results.NotFound();
            if (notification.ReadAt is null)
            {
                notification.ReadAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });
    }
}
