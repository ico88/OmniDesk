using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;
using OmniDesk.Api.Maintenance;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class RetentionScenario(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Retention_removes_only_old_fully_processed_events_and_audits_the_batch()
    {
        var deliveredId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var deadLetterId = Guid.NewGuid();
        var noConsumerId = Guid.NewGuid();
        var recentId = Guid.NewGuid();
        var old = DateTimeOffset.UtcNow.AddDays(-200);
        var recent = DateTimeOffset.UtcNow.AddDays(-20);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TestDatabaseMigration.ApplyAsync(db);
            db.CoreOutboxEvents.AddRange(
                Event(deliveredId, old), Event(pendingId, old), Event(deadLetterId, old), Event(noConsumerId, old), Event(recentId, recent));
            db.CoreOutboxDeliveries.AddRange(
                Delivery(deliveredId, old.AddDays(1), null),
                Delivery(pendingId, null, null),
                Delivery(deadLetterId, null, old.AddDays(1)),
                Delivery(recentId, recent.AddDays(1), null));
            await db.SaveChangesAsync();
        }

        var worker = new CoreOutboxRetentionWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RetentionOptions { DeliveredOutboxDays = 180 }),
            NullLogger<CoreOutboxRetentionWorker>.Instance);
        Assert.Equal(1, await worker.PurgeEligibleBatchAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var remaining = await db.CoreOutboxEvents.Where(item =>
                item.Id == deliveredId || item.Id == pendingId || item.Id == deadLetterId || item.Id == noConsumerId || item.Id == recentId)
                .Select(item => item.Id).ToArrayAsync();
            Assert.DoesNotContain(deliveredId, remaining);
            Assert.Contains(pendingId, remaining);
            Assert.Contains(deadLetterId, remaining);
            Assert.Contains(noConsumerId, remaining);
            Assert.Contains(recentId, remaining);
            Assert.False(await db.CoreOutboxDeliveries.AnyAsync(item => item.EventId == deliveredId));
            Assert.Contains(await db.AuditEvents.ToArrayAsync(), audit =>
                audit.Action == "retention.outbox.purged" && audit.Target == "1");
        }
    }

    private static CoreOutboxEvent Event(Guid id, DateTimeOffset occurredAt) => new()
    {
        Id = id,
        EventType = "retention.test",
        AggregateType = "ticket",
        AggregateId = Guid.NewGuid(),
        OccurredAt = occurredAt,
        CorrelationId = Guid.NewGuid().ToString("N"),
        Payload = "{}"
    };

    private static CoreOutboxDelivery Delivery(Guid eventId, DateTimeOffset? processedAt, DateTimeOffset? deadLetteredAt) => new()
    {
        EventId = eventId,
        ConsumerName = "retention-test",
        ProcessedAt = processedAt,
        DeadLetteredAt = deadLetteredAt
    };
}
