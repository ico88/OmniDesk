using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/notifications").RequireAuthorization();
        api.MapGet("", async (HttpContext c, AppDbContext db, PermissionService permissions) =>
        {
            var userId = c.User.UserId();
            var rows = await (from notification in db.UserNotifications.AsNoTracking()
                              join ticket in db.SupportTickets.AsNoTracking() on notification.TicketId equals ticket.Id
                              where notification.UserId == userId
                              orderby notification.CreatedAt descending
                              select new { notification.Id, notification.Kind, notification.CreatedAt, notification.ReadAt,
                                  ticketId = ticket.Id, ticket.Number, ticket.Subject, ticket.DeviceId, ticket.SiteId })
                .Take(100).ToArrayAsync();
            var visible = new List<object>();
            var unread = 0;
            foreach (var item in rows)
            {
                if (!await permissions.Allows(userId, "tickets.view", item.DeviceId, siteId: item.SiteId)) continue;
                if (item.ReadAt is null) unread++;
                visible.Add(new { item.Id, item.Kind, item.CreatedAt, item.ReadAt, item.ticketId, item.Number, item.Subject });
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
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notification.TicketId);
            if (ticket is null) return Results.NotFound();
            if (!await permissions.Allows(userId, "tickets.view", ticket.DeviceId, siteId: ticket.SiteId)) return Results.Forbid();
            if (notification.ReadAt is null)
            {
                notification.ReadAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });
    }
}
