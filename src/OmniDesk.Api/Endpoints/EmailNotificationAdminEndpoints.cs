using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
            var items = await (from delivery in db.EmailNotificationDeliveries.AsNoTracking()
                               join notification in db.UserNotifications.AsNoTracking() on delivery.NotificationId equals notification.Id
                               join ticket in db.SupportTickets.AsNoTracking() on notification.TicketId equals ticket.Id
                               where delivery.State != "sent" && delivery.State != "skipped"
                               orderby delivery.NextAttemptAt, notification.CreatedAt
                               select new { notificationId = delivery.NotificationId, delivery.State, delivery.AttemptCount,
                                   delivery.NextAttemptAt, delivery.DeadLetteredAt, delivery.LastErrorType,
                                   ticketNumber = ticket.Number, notification.Kind, notification.CreatedAt })
                .Take(100).ToArrayAsync();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { counts, items });
        });

        admin.MapPost("/{notificationId:guid}/retry", async (Guid notificationId, HttpContext context,
            AppDbContext db, PermissionService permissions, IOptions<OutboundEmailOptions> email) =>
        {
            var actorId = context.User.UserId();
            if (!await permissions.Allows(actorId, "administration")) return Results.Forbid();
            if (!email.Value.Enabled || !email.Value.IsValid())
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
