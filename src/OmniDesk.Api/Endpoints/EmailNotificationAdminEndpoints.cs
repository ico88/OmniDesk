using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Api.ServiceDesk;

namespace OmniDesk.Api.Endpoints;

public static class EmailNotificationAdminEndpoints
{
    public static void MapEmailNotificationAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/email-notifications").RequireAuthorization();
        admin.MapGet("", async (HttpContext context, AppDbContext db, PermissionService permissions) =>
        {
            if (!await permissions.Allows(context.User.UserId(), "administration")) return Results.Forbid();
            var counts = await db.EmailNotificationDeliveries.AsNoTracking()
                .GroupBy(x => x.State).Select(group => new { state = group.Key, count = group.Count() }).ToArrayAsync();
            var pending = await (from delivery in db.EmailNotificationDeliveries.AsNoTracking()
                                 join notification in db.UserNotifications.AsNoTracking() on delivery.NotificationId equals notification.Id
                                 where delivery.State != "sent" && delivery.State != "skipped"
                                 orderby delivery.NextAttemptAt, notification.CreatedAt
                                 select new { notificationId = delivery.NotificationId, delivery.State, delivery.AttemptCount,
                                     delivery.NextAttemptAt, delivery.DeadLetteredAt, delivery.LastErrorType,
                                     notification.TicketId, notification.DeviceId, notification.Kind, notification.CreatedAt })
                .Take(100).ToArrayAsync();
            var items = new List<object>();
            foreach (var delivery in pending)
            {
                var ticketNumber = delivery.TicketId is { } ticketId
                    ? await db.SupportTickets.Where(ticket => ticket.Id == ticketId).Select(ticket => (long?)ticket.Number).SingleOrDefaultAsync()
                    : null;
                var deviceName = delivery.DeviceId is { } deviceId
                    ? await db.Devices.Where(device => device.Id == deviceId).Select(device => device.Name).SingleOrDefaultAsync()
                    : null;
                items.Add(new { delivery.notificationId, delivery.State, delivery.AttemptCount, delivery.NextAttemptAt,
                    delivery.DeadLetteredAt, delivery.LastErrorType, ticketNumber, deviceName, delivery.Kind, delivery.CreatedAt });
            }
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { counts, items });
        });

        admin.MapPost("/{notificationId:guid}/retry", async (Guid notificationId, HttpContext context,
            AppDbContext db, PermissionService permissions, IOutboundEmailSender email) =>
        {
            var actorId = context.User.UserId();
            if (!await permissions.Allows(actorId, "administration")) return Results.Forbid();
            if (!await email.IsEnabledAsync(context.RequestAborted))
                return Results.Conflict(new { error = "Outbound email is not configured." });

            await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
            var now = DateTimeOffset.UtcNow;
            var changed = await db.EmailNotificationDeliveries
                .Where(x => x.NotificationId == notificationId && x.State == "dead")
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.State, "pending")
                    .SetProperty(x => x.AttemptCount, 0)
                    .SetProperty(x => x.NextAttemptAt, now)
                    .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(x => x.DeadLetteredAt, (DateTimeOffset?)null)
                    .SetProperty(x => x.LastErrorType, (string?)null), context.RequestAborted);
            if (changed == 0)
            {
                if (!await db.EmailNotificationDeliveries.AnyAsync(x => x.NotificationId == notificationId, context.RequestAborted))
                    return Results.NotFound();
                return Results.Conflict(new { error = "Only dead-letter deliveries can be retried." });
            }
            db.AuditEvents.Add(new AuditEvent { ActorId = actorId, Action = "notification.email.retry", Target = notificationId.ToString() });
            await db.SaveChangesAsync(context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.NoContent();
        });
    }
}
