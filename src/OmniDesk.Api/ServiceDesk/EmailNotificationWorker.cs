using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.ServiceDesk;

public sealed class EmailNotificationWorker(IServiceScopeFactory scopes, ILogger<EmailNotificationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await ProcessNext(stoppingToken)) continue;
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    logger.LogWarning("Email notification worker failed: {Type}", e.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<bool> ProcessNext(CancellationToken ct)
    {
        Guid id;
        using (var scope = scopes.CreateScope())
        {
            if (!await scope.ServiceProvider.GetRequiredService<IOutboundEmailSender>().IsEnabledAsync(ct)) return false;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var rows = await db.EmailNotificationDeliveries.FromSqlInterpolated($"""
                SELECT d.* FROM "EmailNotificationDeliveries" d
                WHERE d."State" IN ('pending','retry','sending') AND d."NextAttemptAt" <= {now}
                  AND (d."LeaseUntil" IS NULL OR d."LeaseUntil" < {now})
                ORDER BY d."NextAttemptAt", d."NotificationId"
                FOR UPDATE SKIP LOCKED LIMIT 1
                """).ToListAsync(ct);
            var delivery = rows.SingleOrDefault();
            if (delivery is null) { await tx.CommitAsync(ct); return false; }
            id = delivery.NotificationId;
            delivery.State = "sending";
            delivery.LeaseUntil = now.Add(Lease);
            delivery.AttemptCount++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.EmailNotificationDeliveries.SingleAsync(x => x.NotificationId == id, ct);
            var notification = await db.UserNotifications.SingleOrDefaultAsync(x => x.Id == id, ct);
            var user = notification is null ? null : await db.Users.SingleOrDefaultAsync(x => x.Id == notification.UserId, ct);
            var ticket = notification?.TicketId is { } ticketId ? await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId, ct) : null;
            var device = notification?.DeviceId is { } deviceId ? await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId, ct) : null;
            var permissions = scope.ServiceProvider.GetRequiredService<PermissionService>();
            var eligible = notification is not null && user is { IsDisabled: false, EmailNotificationsEnabled: true,
                    EmailNotificationsEnabledAt: { } consentAt, Email: not null } && notification.CreatedAt >= consentAt &&
                (ticket is not null && await TicketAccessPolicy.CanViewAsync(db, permissions, user.Id, ticket.Id,
                     ticket.RequesterUserId, ticket.DeviceId, ticket.SiteId, ct) ||
                 device is not null && await db.DeviceCredentials.AnyAsync(x => x.DeviceId == device.Id && x.RevokedAt == null, ct) &&
                    await permissions.Allows(user.Id, "view", device.Id, siteId: device.SiteId));
            if (eligible && notification!.Kind == "ticket.attachment")
            {
                var evt = await db.CoreOutboxEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notification.EventId, ct);
                if (evt is null || System.Text.Json.JsonDocument.Parse(evt.Payload).RootElement.TryGetProperty("visibility", out var visibility) &&
                    visibility.ValueKind == System.Text.Json.JsonValueKind.String && visibility.GetString() == "internal")
                    eligible = evt is not null && await scope.ServiceProvider.GetRequiredService<PermissionService>().Allows(user!.Id,
                        "tickets.note.internal", ticket!.DeviceId, siteId: ticket.SiteId);
            }
            if (!eligible)
            {
                delivery.State = "skipped";
                delivery.LeaseUntil = null;
                await db.SaveChangesAsync(ct);
                return true;
            }

            var emailConfiguration = await scope.ServiceProvider.GetRequiredService<SmtpOutboundEmailSender>().ReadConfigurationAsync(ct);
            var dashboardUrl = emailConfiguration.DashboardUrl.TrimEnd('/');
            var kind = notification!.Kind switch
            {
                "device.offline" => "Dispositivo non raggiungibile",
                "ticket.new" => "Nuovo ticket assegnato",
                "ticket.reply" => "Nuova risposta al ticket",
                "ticket.staff.reply" => "Nuova risposta dell'assistenza al ticket",
                "ticket.assigned" => "Ticket assegnato",
                "ticket.attachment" => "Nuovo allegato al ticket",
                "ticket.sla.breached" => "SLA del ticket superato",
                "ticket.sla.escalated" => "Ticket escalato",
                _ => "Aggiornamento ticket"
            };
            var subject = ticket is not null ? $"OmniDesk: {kind} #{ticket.Number}" : $"OmniDesk: {kind}";
            var content = ticket is not null
                ? $"{kind} #{ticket.Number}.\n\nApri OmniDesk per consultare il ticket: {dashboardUrl}/?ticket={ticket.Id}\n"
                : $"{kind}: {device!.Name}.\n\nApri OmniDesk e consulta i dispositivi: {dashboardUrl}\n";
            var message = new OutboundEmailMessage(user!.Email!, subject, content,
                $"notification-{id:N}@omnidesk.invalid");
            await scope.ServiceProvider.GetRequiredService<IOutboundEmailSender>().SendAsync(message, ct);
            delivery.State = "sent";
            delivery.SentAt = DateTimeOffset.UtcNow;
            delivery.LeaseUntil = null;
            delivery.LastErrorType = null;
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.EmailNotificationDeliveries.SingleOrDefaultAsync(x => x.NotificationId == id, ct);
            if (delivery is not null)
            {
                delivery.LeaseUntil = null;
                delivery.LastErrorType = e.GetType().Name;
                delivery.State = delivery.AttemptCount >= 8 ? "dead" : "retry";
                delivery.DeadLetteredAt = delivery.State == "dead" ? DateTimeOffset.UtcNow : null;
                delivery.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600, Math.Pow(2, Math.Min(delivery.AttemptCount, 12))));
                await db.SaveChangesAsync(ct);
            }
            logger.LogWarning("Email notification delivery {NotificationId} failed: {Type}", id, e.GetType().Name);
        }
        return true;
    }
}
