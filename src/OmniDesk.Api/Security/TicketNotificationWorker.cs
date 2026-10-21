using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.ServiceDesk;

namespace OmniDesk.Api.Security;

public sealed class TicketNotificationWorker(IServiceScopeFactory scopes, ILogger<TicketNotificationWorker> logger) : BackgroundService
{
    private const string Consumer = "ticket-notifications.v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    while (await ProcessNext(stoppingToken)) { }
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    logger.LogWarning("Ticket notification worker failed: {Type}", e.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<bool> ProcessNext(CancellationToken ct)
    {
        Guid eventId;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var candidates = await db.CoreOutboxEvents.FromSqlInterpolated($"""
                SELECT e.* FROM "CoreOutboxEvents" e
                WHERE COALESCE((SELECT d."NextAttemptAt" FROM "CoreOutboxDeliveries" d
                    WHERE d."EventId" = e."Id" AND d."ConsumerName" = {Consumer}), e."NextAttemptAt") <= {now}
                  AND NOT EXISTS (SELECT 1 FROM "CoreOutboxDeliveries" d
                    WHERE d."EventId" = e."Id" AND d."ConsumerName" = {Consumer}
                      AND (d."ProcessedAt" IS NOT NULL OR d."DeadLetteredAt" IS NOT NULL))
                ORDER BY e."OccurredAt", e."Id"
                FOR UPDATE OF e SKIP LOCKED
                LIMIT 1
                """).ToListAsync(ct);
            var evt = candidates.SingleOrDefault();
            if (evt is null) { await tx.CommitAsync(ct); return false; }

            eventId = evt.Id;
            try
            {
                await ProjectNotifications(db, scope.ServiceProvider.GetRequiredService<PermissionService>(), evt, now, ct,
                    outboundEmailEnabled: true);
                var delivery = await db.CoreOutboxDeliveries.FindAsync([evt.Id, Consumer], ct);
                if (delivery is null)
                {
                    delivery = new CoreOutboxDelivery { EventId = evt.Id, ConsumerName = Consumer };
                    db.CoreOutboxDeliveries.Add(delivery);
                }
                delivery.ProcessedAt = now;
                delivery.NextAttemptAt = now;
                delivery.LastErrorType = null;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                await tx.RollbackAsync(ct);
                await RecordFailure(eventId, e, ct);
                logger.LogWarning("Ticket notification delivery failed for {EventId}: {Type}", eventId, e.GetType().Name);
                return true;
            }
        }
    }

    public static async Task ProjectNotifications(AppDbContext db, PermissionService permissions, CoreOutboxEvent evt,
        DateTimeOffset now, CancellationToken ct, bool outboundEmailEnabled = false)
    {
        if (evt.AggregateType == "device" && evt.EventType == "device.presence.offline")
        {
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == evt.AggregateId, ct);
            if (device is null || !await db.DeviceCredentials.AnyAsync(x => x.DeviceId == device.Id && x.RevokedAt == null, ct)) return;
            var users = await db.Users.AsNoTracking().Where(x => !x.IsDisabled).Select(x => new
                { x.Id, x.Email, x.EmailNotificationsEnabled, x.EmailNotificationsEnabledAt }).ToArrayAsync(ct);
            foreach (var user in users)
            {
                if (!await permissions.Allows(user.Id, "view", device.Id, siteId: device.SiteId)) continue;
                var notification = new UserNotification
                {
                    EventId = evt.Id, UserId = user.Id, DeviceId = device.Id, Kind = "device.offline", CreatedAt = now
                };
                db.UserNotifications.Add(notification);
                if (outboundEmailEnabled && await db.Users.AnyAsync(candidate => candidate.Id == user.Id &&
                        candidate.EmailNotificationsEnabled && candidate.EmailNotificationsEnabledAt != null && candidate.Email != null, ct))
                    db.EmailNotificationDeliveries.Add(new EmailNotificationDelivery { NotificationId = notification.Id, State = "pending", NextAttemptAt = now });
            }
            return;
        }
        if (evt.AggregateType != "ticket") return;
        var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == evt.AggregateId, ct);
        if (ticket is null) return;

        using var payload = JsonDocument.Parse(evt.Payload);
        var root = payload.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Outbox payload must be a JSON object.");
        var recipients = new HashSet<string>(StringComparer.Ordinal);
        string? kind = null;
        if (evt.EventType == "ticket.created")
        {
            kind = "ticket.new";
            await AddQueueMembers(db, root, recipients, ct);
        }
        else if (evt.EventType == "ticket.requester_replied")
        {
            kind = "ticket.reply";
            await AddAssigneeOrQueue(db, root, recipients, ct);
            await AddTicketParticipants(db, ticket.Id, evt.OccurredAt, recipients, ct);
        }
        else if (evt.EventType == "ticket.updated")
        {
            var assignmentChanged = TryString(root, "assigneeId", out var assignee) &&
                (!TryString(root, "oldAssigneeId", out var oldAssignee) || !StringComparer.Ordinal.Equals(assignee, oldAssignee));
            kind = assignmentChanged ? "ticket.assigned" : TryBoolean(root, "replyAdded") ? "ticket.reply" : "ticket.updated";
            if (assignmentChanged) recipients.Add(assignee);
            else if (kind == "ticket.reply") await AddAssigneeOrQueue(db, root, recipients, ct);
            if (kind == "ticket.reply" && ticket.RequesterUserId is { } requesterId) recipients.Add(requesterId);
            await AddTicketParticipants(db, ticket.Id, evt.OccurredAt, recipients, ct);
        }
        else if (evt.EventType == "ticket.participant.added" && TryString(root, "participantUserId", out var participant))
        {
            kind = "ticket.participant.added";
            recipients.Add(participant);
        }
        else if (evt.EventType == "ticket.attachment.ready")
        {
            kind = "ticket.attachment";
            await AddAssigneeOrQueue(db, root, recipients, ct);
            await AddTicketParticipants(db, ticket.Id, evt.OccurredAt, recipients, ct);
        }
        else if (evt.EventType is "ticket.sla.breached" or "ticket.sla.escalated")
        {
            kind = evt.EventType;
            await AddAssigneeOrQueue(db, root, recipients, ct);
            await AddTicketParticipants(db, ticket.Id, evt.OccurredAt, recipients, ct);
        }
        if (kind is null) return;

        TryString(root, "actorId", out var actorId);
        foreach (var userId in recipients)
        {
            if (userId == actorId || !await db.Users.AnyAsync(x => x.Id == userId && !x.IsDisabled, ct) ||
                !await TicketAccessPolicy.CanViewAsync(db, permissions, userId, ticket.Id, ticket.RequesterUserId,
                    ticket.DeviceId, ticket.SiteId, ct)) continue;
            if (evt.EventType == "ticket.attachment.ready" && TryString(root, "visibility", out var attachmentVisibility) &&
                attachmentVisibility == "internal" && !await permissions.Allows(userId, "tickets.note.internal", ticket.DeviceId, siteId: ticket.SiteId)) continue;
            if (await db.UserNotifications.AnyAsync(existing => existing.EventId == evt.Id && existing.UserId == userId, ct))
                continue;
            var notification = new UserNotification
            {
                EventId = evt.Id, UserId = userId, TicketId = ticket.Id,
                Kind = kind == "ticket.reply" && userId == ticket.RequesterUserId ? "ticket.staff.reply" : kind,
                CreatedAt = now
            };
            db.UserNotifications.Add(notification);
            if (outboundEmailEnabled && await db.Users.AnyAsync(x => x.Id == userId && x.EmailNotificationsEnabled &&
                    x.EmailNotificationsEnabledAt != null && x.Email != null, ct))
                db.EmailNotificationDeliveries.Add(new EmailNotificationDelivery { NotificationId = notification.Id, State = "pending", NextAttemptAt = now });
        }
    }

    private static async Task AddAssigneeOrQueue(AppDbContext db, JsonElement root, HashSet<string> recipients, CancellationToken ct)
    {
        if (TryString(root, "assigneeId", out var assignee)) recipients.Add(assignee);
        else await AddQueueMembers(db, root, recipients, ct);
    }

    private static async Task AddQueueMembers(AppDbContext db, JsonElement root, HashSet<string> recipients, CancellationToken ct)
    {
        if (!root.TryGetProperty("queueId", out var queue) || queue.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(queue.GetString(), out var queueId)) return;
        foreach (var userId in await db.SupportQueueMembers.AsNoTracking().Where(x => x.QueueId == queueId)
                     .Select(x => x.UserId).ToArrayAsync(ct)) recipients.Add(userId);
    }

    private static async Task AddTicketParticipants(AppDbContext db, Guid ticketId, DateTimeOffset eventAt,
        HashSet<string> recipients, CancellationToken ct)
    {
        foreach (var userId in await db.SupportTicketParticipants.AsNoTracking()
                     .Where(x => x.TicketId == ticketId && x.AddedAt <= eventAt)
                     .Select(x => x.UserId).ToArrayAsync(ct)) recipients.Add(userId);
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString()))
        { value = property.GetString()!; return true; }
        value = "";
        return false;
    }

    private static bool TryBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private async Task RecordFailure(Guid eventId, Exception error, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var delivery = await db.CoreOutboxDeliveries.FindAsync([eventId, Consumer], ct);
        if (delivery is null)
        {
            delivery = new CoreOutboxDelivery { EventId = eventId, ConsumerName = Consumer };
            db.CoreOutboxDeliveries.Add(delivery);
        }
        delivery.AttemptCount++;
        delivery.LastErrorType = error.GetType().Name;
        delivery.DeadLetteredAt = delivery.AttemptCount >= 10 ? DateTimeOffset.UtcNow : null;
        delivery.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600, Math.Pow(2, Math.Min(delivery.AttemptCount, 12))));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
