using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;

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
        if (retention.DeliveredOutboxDays == 0)
        {
            logger.LogInformation("Delivered outbox retention is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var removed = 0;
                    for (var batch = 0; batch < MaxBatchesPerRun; batch++)
                    {
                        var count = await PurgeEligibleBatchAsync(stoppingToken);
                        removed += count;
                        if (count < BatchSize) break;
                    }
                    if (removed > 0) logger.LogInformation("Retention removed {Count} delivered outbox events and dependent notifications.", removed);
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    logger.LogWarning("Outbox retention pass failed: {Type}", error.GetType().Name);
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
}
