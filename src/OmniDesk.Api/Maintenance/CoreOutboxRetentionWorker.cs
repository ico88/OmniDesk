using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;
using OmniDesk.Api.Attachments;

namespace OmniDesk.Api.Maintenance;

public sealed class CoreOutboxRetentionWorker(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    ILogger<CoreOutboxRetentionWorker> logger) : BackgroundService
{
    private const int BatchSize = 250;
    private const int MaxBatchesPerRun = 10;
    private readonly RetentionOptions retention = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (retention.DeliveredOutboxDays is < 0 or > 36500)
            throw new InvalidOperationException("Retention:DeliveredOutboxDays must be between 0 and 36500.");
        ValidateEmailIntakeRetention();
        ValidateTicketAttachmentRetention();
        ValidateTicketContentRetention();
        ValidateAuditRetention();
        ValidateDeviceRetention();
        if (retention.DeliveredOutboxDays == 0)
            logger.LogInformation("Delivered outbox event retention is disabled.");

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var removed = 0;
                    if (retention.DeliveredOutboxDays > 0)
                    {
                        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
                        {
                            var count = await PurgeEligibleBatchAsync(stoppingToken);
                            removed += count;
                            if (count < BatchSize) break;
                        }
                    }
                    if (removed > 0) logger.LogInformation("Retention removed {Count} delivered outbox events and dependent notifications.", removed);
                    var emailIntakesRemoved = 0;
                    for (var batch = 0; batch < MaxBatchesPerRun; batch++)
                    {
                        var count = await PurgeEmailIntakesBatchAsync(stoppingToken);
                        emailIntakesRemoved += count;
                        if (count < BatchSize) break;
                    }
                    if (emailIntakesRemoved > 0) logger.LogInformation("Retention removed {Count} expired email intake records.", emailIntakesRemoved);
                    var attachmentsRemoved = 0;
                    if (retention.ResolvedTicketAttachmentsDays > 0)
                    {
                        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
                        {
                            var count = await PurgeResolvedTicketAttachmentsBatchAsync(stoppingToken);
                            attachmentsRemoved += count;
                            if (count < AttachmentBatchSize) break;
                        }
                    }
                    if (attachmentsRemoved > 0) logger.LogInformation("Retention purged {Count} resolved-ticket attachments and screenshots.", attachmentsRemoved);
                    var contentTicketsPurged = 0;
                    if (retention.ResolvedTicketContentDays > 0)
                    {
                        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
                        {
                            var count = await PurgeResolvedTicketContentBatchAsync(stoppingToken);
                            contentTicketsPurged += count;
                            if (count < BatchSize) break;
                        }
                    }
                    if (contentTicketsPurged > 0) logger.LogInformation("Retention redacted content for {Count} resolved tickets.", contentTicketsPurged);
                    var auditEventsRemoved = 0;
                    if (retention.AuditEventsDays > 0)
                    {
                        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
                        {
                            var count = await PurgeAuditEventsBatchAsync(stoppingToken);
                            auditEventsRemoved += count;
                            if (count < AuditBatchSize) break;
                        }
                    }
                    if (auditEventsRemoved > 0) logger.LogInformation("Retention removed {Count} expired audit events.", auditEventsRemoved);
                    var deviceMetricsRemoved = 0;
                    if (retention.DeviceMetricsDays > 0)
                    {
                        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
                        {
                            var count = await PurgeDeviceMetricsBatchAsync(stoppingToken);
                            deviceMetricsRemoved += count;
                            if (count < DeviceBatchSize) break;
                        }
                    }
                    if (deviceMetricsRemoved > 0) logger.LogInformation("Retention removed {Count} expired device metrics snapshots.", deviceMetricsRemoved);
                    var inventoriesRemoved = 0;
                    if (retention.DeviceInventoryDays > 0)
                    {
                        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
                        {
                            var count = await PurgeDeviceInventoryBatchAsync(stoppingToken);
                            inventoriesRemoved += count;
                            if (count < DeviceBatchSize) break;
                        }
                    }
                    if (inventoriesRemoved > 0) logger.LogInformation("Retention removed {Count} expired device inventory snapshots.", inventoriesRemoved);
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    logger.LogWarning("Data retention pass failed: {Type}", error.GetType().Name);
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<int> PurgeEligibleBatchAsync(CancellationToken cancellationToken = default)
    {
        if (retention.DeliveredOutboxDays is < 1 or > 36500)
            throw new InvalidOperationException("Delivered outbox retention is disabled or outside its supported range.");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retention.DeliveredOutboxDays);
        var candidates = await db.CoreOutboxEvents.AsNoTracking()
            .Where(item => item.OccurredAt < cutoff &&
                db.CoreOutboxDeliveries.Any(delivery => delivery.EventId == item.Id) &&
                !db.CoreOutboxDeliveries.Any(delivery => delivery.EventId == item.Id &&
                    (delivery.ProcessedAt == null || delivery.DeadLetteredAt != null)))
            .OrderBy(item => item.OccurredAt)
            .Select(item => item.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Re-check eligibility in the delete statement to preserve late or retried deliveries.
        var deleted = await db.CoreOutboxEvents
            .Where(item => candidates.Contains(item.Id) && item.OccurredAt < cutoff &&
                db.CoreOutboxDeliveries.Any(delivery => delivery.EventId == item.Id) &&
                !db.CoreOutboxDeliveries.Any(delivery => delivery.EventId == item.Id &&
                    (delivery.ProcessedAt == null || delivery.DeadLetteredAt != null)))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Action = "retention.outbox.purged",
                Target = deleted.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<int> PurgeEmailIntakesBatchAsync(CancellationToken cancellationToken = default)
    {
        ValidateEmailIntakeRetention();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pendingCutoff = DateTimeOffset.UtcNow.AddDays(-retention.PendingEmailIntakesDays);
        var processedCutoff = DateTimeOffset.UtcNow.AddDays(-retention.ProcessedEmailIntakesDays);
        var eligible = db.SupportEmailIntakes.Where(item =>
            (item.State == "pending" && item.ReceivedAt < pendingCutoff) ||
            ((item.State == "discarded" || item.State == "promoted") && item.ReceivedAt < processedCutoff));
        var candidates = await eligible.OrderBy(item => item.ReceivedAt)
            .Select(item => item.Id).Take(BatchSize).ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Recheck current state at deletion so concurrent qualification uses the terminal retention window.
        var deleted = await db.SupportEmailIntakes
            .Where(item => candidates.Contains(item.Id) &&
                ((item.State == "pending" && item.ReceivedAt < pendingCutoff) ||
                 ((item.State == "discarded" || item.State == "promoted") && item.ReceivedAt < processedCutoff)))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Action = "retention.email-intakes.purged",
                Target = deleted.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private const int AttachmentBatchSize = 50;
    private const int TicketContentBatchSize = 250;
    private const int AuditBatchSize = 500;
    private const int DeviceBatchSize = 250;

    public async Task<int> PurgeDeviceMetricsBatchAsync(CancellationToken cancellationToken = default)
    {
        ValidateDeviceRetention();
        if (retention.DeviceMetricsDays == 0) return 0;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retention.DeviceMetricsDays);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidateIds = await db.DeviceMetrics.AsNoTracking()
            .Where(item => item.ReceivedAt < cutoff &&
                !db.DevicePresence.Any(presence => presence.DeviceId == item.DeviceId && presence.IsOnline))
            .OrderBy(item => item.ReceivedAt).ThenBy(item => item.DeviceId)
            .Select(item => item.DeviceId).Take(DeviceBatchSize).ToArrayAsync(cancellationToken);
        if (candidateIds.Length == 0) return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await db.DeviceMetrics
            .Where(item => candidateIds.Contains(item.DeviceId) && item.ReceivedAt < cutoff &&
                !db.DevicePresence.Any(presence => presence.DeviceId == item.DeviceId && presence.IsOnline))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Action = "retention.device-metrics.purged",
                Target = deleted.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<int> PurgeDeviceInventoryBatchAsync(CancellationToken cancellationToken = default)
    {
        ValidateDeviceRetention();
        if (retention.DeviceInventoryDays == 0) return 0;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retention.DeviceInventoryDays);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidateIds = await db.DeviceInventories.AsNoTracking()
            .Where(item => item.ReceivedAt < cutoff)
            .OrderBy(item => item.ReceivedAt).ThenBy(item => item.DeviceId)
            .Select(item => item.DeviceId).Take(DeviceBatchSize).ToArrayAsync(cancellationToken);
        if (candidateIds.Length == 0) return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await db.DeviceInventories
            .Where(item => candidateIds.Contains(item.DeviceId) && item.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Action = "retention.device-inventory.purged",
                Target = deleted.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<int> PurgeAuditEventsBatchAsync(CancellationToken cancellationToken = default)
    {
        ValidateAuditRetention();
        if (retention.AuditEventsDays == 0) return 0;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retention.AuditEventsDays);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidateIds = await db.AuditEvents.AsNoTracking()
            .Where(item => item.At < cutoff && item.Action != "retention.audit-events.purged")
            .OrderBy(item => item.At).ThenBy(item => item.Id)
            .Select(item => item.Id).Take(AuditBatchSize).ToArrayAsync(cancellationToken);
        if (candidateIds.Length == 0) return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await db.AuditEvents
            .Where(item => candidateIds.Contains(item.Id) && item.At < cutoff && item.Action != "retention.audit-events.purged")
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Action = "retention.audit-events.purged",
                Target = deleted.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<int> PurgeResolvedTicketContentBatchAsync(CancellationToken cancellationToken = default)
    {
        ValidateTicketContentRetention();
        if (retention.ResolvedTicketContentDays == 0) return 0;
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-retention.ResolvedTicketContentDays);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tickets = await db.SupportTickets.FromSqlInterpolated($"""
            SELECT * FROM "SupportTickets"
            WHERE "ContentPurged" = FALSE AND "State" IN ('resolved','closed') AND "UpdatedAt" < {cutoff}
            ORDER BY "UpdatedAt", "Id"
            FOR UPDATE SKIP LOCKED
            LIMIT {TicketContentBatchSize}
            """).ToListAsync(cancellationToken);
        if (tickets.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var ticketIds = tickets.Select(ticket => ticket.Id).ToArray();
        foreach (var ticket in tickets)
        {
            ticket.Subject = $"Ticket #{ticket.Number}";
            ticket.Description = "";
            ticket.Resolution = null;
            ticket.ContentPurged = true;
        }
        var messages = await db.SupportMessages.Where(message => ticketIds.Contains(message.TicketId) && !message.ContentPurged &&
                (message.Kind == "reply" || message.Kind == "requester_reply" || message.Kind == "portal_reply" || message.Kind == "internal_note"))
            .ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            message.Body = "";
            message.ContentPurged = true;
        }
        db.AuditEvents.Add(new AuditEvent
        {
            Action = "retention.ticket-content.purged",
            Target = $"tickets={tickets.Count};messages={messages.Count}"
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return tickets.Count;
    }

    public async Task<int> PurgeResolvedTicketAttachmentsBatchAsync(CancellationToken cancellationToken = default)
    {
        ValidateTicketAttachmentRetention();
        if (retention.ResolvedTicketAttachmentsDays == 0) return 0;
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-retention.ResolvedTicketAttachmentsDays);
        List<(Guid Id, string StorageKey)> candidates;
        int screenshotsPurged;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var rows = await db.SupportAttachments.FromSqlInterpolated($"""
                SELECT a.* FROM "SupportAttachments" a
                LEFT JOIN "SupportTickets" t ON t."Id" = a."TicketId"
                WHERE (a."ScanState" = 'retention_pending' AND a."NextScanAt" <= {now})
                   OR (a."ScanState" <> 'retention_pending' AND t."State" IN ('resolved','closed') AND t."UpdatedAt" < {cutoff}
                       AND (a."ScanState" <> 'scanning' OR a."ScanLeaseUntil" IS NULL OR a."ScanLeaseUntil" <= {now}))
                ORDER BY a."NextScanAt", a."CreatedAt", a."Id"
                FOR UPDATE OF a SKIP LOCKED
                LIMIT {AttachmentBatchSize}
                """).ToListAsync(cancellationToken);
            foreach (var attachment in rows)
            {
                attachment.ScanState = "retention_pending";
                attachment.ScanLeaseUntil = null;
                attachment.NextScanAt = now;
                attachment.ScanAttempts = 0;
                attachment.LastScanErrorType = null;
            }
            await db.SaveChangesAsync(cancellationToken);

            var ticketIds = await db.SupportTickets.AsNoTracking()
                .Where(ticket => (ticket.State == "resolved" || ticket.State == "closed") && ticket.UpdatedAt < cutoff && ticket.Screenshot != null)
                .OrderBy(ticket => ticket.UpdatedAt).Select(ticket => ticket.Id).Take(BatchSize).ToArrayAsync(cancellationToken);
            screenshotsPurged = ticketIds.Length == 0 ? 0 : await db.SupportTickets
                .Where(ticket => ticketIds.Contains(ticket.Id) && (ticket.State == "resolved" || ticket.State == "closed") &&
                    ticket.UpdatedAt < cutoff && ticket.Screenshot != null)
                .ExecuteUpdateAsync(update => update.SetProperty(ticket => ticket.Screenshot, (byte[]?)null), cancellationToken);
            if (screenshotsPurged > 0)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Action = "retention.ticket-screenshots.purged",
                    Target = screenshotsPurged.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            candidates = rows.Select(row => (row.Id, row.StorageKey)).ToList();
        }

        var deletedIds = new List<Guid>(candidates.Count);
        using (var scope = scopes.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<AttachmentStorage>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    storage.DeleteForRetention(candidate.StorageKey);
                    deletedIds.Add(candidate.Id);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    var attempt = await db.SupportAttachments.Where(item => item.Id == candidate.Id && item.ScanState == "retention_pending")
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(item => item.ScanAttempts, item => item.ScanAttempts + 1)
                            .SetProperty(item => item.NextScanAt, now.AddMinutes(5))
                            .SetProperty(item => item.LastScanErrorType, error.GetType().Name), cancellationToken);
                    if (attempt > 0) logger.LogWarning("Ticket attachment retention deferred: {Type}", error.GetType().Name);
                }
                catch (AttachmentStorageUnavailableException error)
                {
                    logger.LogWarning("Ticket attachment retention deferred: {Type}", error.GetType().Name);
                    break;
                }
            }
        }

        var attachmentsPurged = 0;
        if (deletedIds.Count > 0)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            attachmentsPurged = await db.SupportAttachments
                .Where(item => deletedIds.Contains(item.Id) && item.ScanState == "retention_pending")
                .ExecuteDeleteAsync(cancellationToken);
            if (attachmentsPurged > 0)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Action = "retention.ticket-attachments.purged",
                    Target = attachmentsPurged.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        return attachmentsPurged + screenshotsPurged;
    }

    private void ValidateEmailIntakeRetention()
    {
        if (retention.ProcessedEmailIntakesDays is < 1 or > 36500)
            throw new InvalidOperationException("Retention:ProcessedEmailIntakesDays must be between 1 and 36500.");
        if (retention.PendingEmailIntakesDays is < 1 or > 36500)
            throw new InvalidOperationException("Retention:PendingEmailIntakesDays must be between 1 and 36500.");
    }

    private void ValidateTicketAttachmentRetention()
    {
        if (retention.ResolvedTicketAttachmentsDays is < 0 or > 36500)
            throw new InvalidOperationException("Retention:ResolvedTicketAttachmentsDays must be between 0 and 36500.");
    }

    private void ValidateTicketContentRetention()
    {
        if (retention.ResolvedTicketContentDays is < 0 or > 36500)
            throw new InvalidOperationException("Retention:ResolvedTicketContentDays must be between 0 and 36500.");
    }

    private void ValidateAuditRetention()
    {
        if (retention.AuditEventsDays is < 0 or > 36500)
            throw new InvalidOperationException("Retention:AuditEventsDays must be between 0 and 36500.");
    }

    private void ValidateDeviceRetention()
    {
        if (retention.DeviceMetricsDays is < 0 or > 36500)
            throw new InvalidOperationException("Retention:DeviceMetricsDays must be between 0 and 36500.");
        if (retention.DeviceInventoryDays is < 0 or > 36500)
            throw new InvalidOperationException("Retention:DeviceInventoryDays must be between 0 and 36500.");
    }
}
