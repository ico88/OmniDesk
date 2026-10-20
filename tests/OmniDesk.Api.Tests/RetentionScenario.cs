using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;
using OmniDesk.Api.Maintenance;
using Xunit;

namespace OmniDesk.Api.Tests;

[CollectionDefinition("Serialized shared database", DisableParallelization = true)]
public sealed class SerializedSharedDatabaseCollection;

[Collection("Serialized shared database")]
public sealed class RetentionScenario(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Ticket_attachment_retention_purges_only_old_resolved_content_and_preserves_auditable_ticket_metadata()
    {
        var now = DateTimeOffset.UtcNow;
        var site = new Site { Name = "Retention site " + Guid.NewGuid().ToString("N") };
        var device = new Device { Id = Guid.NewGuid(), Name = "Retention device", SiteId = site.Id };
        var oldTicket = Ticket(device.Id, site.Id, "closed", now.AddDays(-200));
        oldTicket.Screenshot = [1, 2, 3, 4];
        var recentTicket = Ticket(device.Id, site.Id, "resolved", now.AddDays(-20));
        recentTicket.Screenshot = [5, 6, 7, 8];
        var openTicket = Ticket(device.Id, site.Id, "open", now.AddDays(-300));
        openTicket.Screenshot = [9, 10, 11, 12];
        var purgeKeys = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        var retainedKeys = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        try
        {
            Directory.CreateDirectory(factory.AttachmentRoot);
            foreach (var key in purgeKeys.Concat(retainedKeys)) File.WriteAllBytes(Path.Combine(factory.AttachmentRoot, key), [1, 2, 3]);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await TestDatabaseMigration.ApplyAsync(db);
                db.Sites.Add(site);
                db.Devices.Add(device);
                db.SupportTickets.AddRange(oldTicket, recentTicket, openTicket);
                db.SupportAttachments.AddRange(
                    Attachment(oldTicket.Id, purgeKeys[0], now.AddDays(-200)),
                    Attachment(oldTicket.Id, purgeKeys[1], now.AddDays(-190)),
                    Attachment(recentTicket.Id, retainedKeys[0], now.AddDays(-20)),
                    Attachment(openTicket.Id, retainedKeys[1], now.AddDays(-300)));
                await db.SaveChangesAsync();
            }

            var disabled = new CoreOutboxRetentionWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RetentionOptions()), NullLogger<CoreOutboxRetentionWorker>.Instance);
            Assert.Equal(0, await disabled.PurgeResolvedTicketAttachmentsBatchAsync());

            var worker = new CoreOutboxRetentionWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RetentionOptions { ResolvedTicketAttachmentsDays = 180 }), NullLogger<CoreOutboxRetentionWorker>.Instance);
            Assert.Equal(3, await worker.PurgeResolvedTicketAttachmentsBatchAsync()); // two files and one database screenshot

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.False(await db.SupportAttachments.AnyAsync(x => x.TicketId == oldTicket.Id));
                Assert.True(await db.SupportAttachments.AnyAsync(x => x.StorageKey == retainedKeys[0]));
                Assert.True(await db.SupportAttachments.AnyAsync(x => x.StorageKey == retainedKeys[1]));
                Assert.Null(await db.SupportTickets.Where(x => x.Id == oldTicket.Id).Select(x => x.Screenshot).SingleAsync());
                Assert.NotNull(await db.SupportTickets.Where(x => x.Id == recentTicket.Id).Select(x => x.Screenshot).SingleAsync());
                Assert.NotNull(await db.SupportTickets.Where(x => x.Id == openTicket.Id).Select(x => x.Screenshot).SingleAsync());
                Assert.Equal("closed", await db.SupportTickets.Where(x => x.Id == oldTicket.Id).Select(x => x.State).SingleAsync());
                Assert.Contains(await db.AuditEvents.ToArrayAsync(), audit => audit.Action == "retention.ticket-attachments.purged" && audit.Target == "2");
                Assert.Contains(await db.AuditEvents.ToArrayAsync(), audit => audit.Action == "retention.ticket-screenshots.purged" && audit.Target == "1");
            }
            Assert.All(purgeKeys, key => Assert.False(File.Exists(Path.Combine(factory.AttachmentRoot, key))));
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SupportAttachments.Where(item => item.TicketId == oldTicket.Id || item.TicketId == recentTicket.Id || item.TicketId == openTicket.Id).ExecuteDeleteAsync();
            await db.SupportTickets.Where(item => item.Id == oldTicket.Id || item.Id == recentTicket.Id || item.Id == openTicket.Id).ExecuteDeleteAsync();
            await db.Devices.Where(item => item.Id == device.Id).ExecuteDeleteAsync();
            await db.Sites.Where(item => item.Id == site.Id).ExecuteDeleteAsync();
            foreach (var key in purgeKeys.Concat(retainedKeys)) File.Delete(Path.Combine(factory.AttachmentRoot, key));
        }
    }

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

    [Fact]
    public async Task Email_intake_retention_purges_expired_pending_and_terminal_records_without_logging_content()
    {
        var oldPendingId = Guid.NewGuid();
        var oldDiscardedId = Guid.NewGuid();
        var oldPromotedId = Guid.NewGuid();
        var recentPendingId = Guid.NewGuid();
        var recentDiscardedId = Guid.NewGuid();
        var unknownStateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TestDatabaseMigration.ApplyAsync(db);
            db.SupportEmailIntakes.AddRange(
                Intake(oldPendingId, "pending", now.AddDays(-31)),
                Intake(oldDiscardedId, "discarded", now.AddDays(-181)),
                Intake(oldPromotedId, "promoted", now.AddDays(-181)),
                Intake(recentPendingId, "pending", now.AddDays(-29)),
                Intake(recentDiscardedId, "discarded", now.AddDays(-179)),
                Intake(unknownStateId, "future-state", now.AddDays(-400)));
            await db.SaveChangesAsync();
        }

        var worker = new CoreOutboxRetentionWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RetentionOptions { DeliveredOutboxDays = 180, ProcessedEmailIntakesDays = 180, PendingEmailIntakesDays = 30 }),
            NullLogger<CoreOutboxRetentionWorker>.Instance);
        Assert.Equal(3, await worker.PurgeEmailIntakesBatchAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var remaining = await db.SupportEmailIntakes.Where(item =>
                    item.Id == oldPendingId || item.Id == oldDiscardedId || item.Id == oldPromotedId ||
                    item.Id == recentPendingId || item.Id == recentDiscardedId || item.Id == unknownStateId)
                .Select(item => item.Id).ToArrayAsync();
            Assert.DoesNotContain(oldPendingId, remaining);
            Assert.DoesNotContain(oldDiscardedId, remaining);
            Assert.DoesNotContain(oldPromotedId, remaining);
            Assert.Contains(recentPendingId, remaining);
            Assert.Contains(recentDiscardedId, remaining);
            Assert.Contains(unknownStateId, remaining);
            var audit = await db.AuditEvents.SingleAsync(item => item.Action == "retention.email-intakes.purged");
            Assert.Equal("3", audit.Target);
            Assert.DoesNotContain("retention@example.test", audit.Target ?? "");
        }
    }

    private static SupportEmailIntake Intake(Guid id, string state, DateTimeOffset receivedAt) => new()
    {
        Id = id,
        MailboxAddress = "support@example.test",
        MessageId = $"<{id:N}@retention.example.test>",
        SenderAddress = "retention@example.test",
        Subject = "Retention fixture — personal data",
        BodyText = "This content must be purged by the retention policy.",
        PayloadSha256 = new string('a', 64),
        MessageReceivedAt = receivedAt,
        ReceivedAt = receivedAt,
        State = state
    };

    private static SupportTicket Ticket(Guid deviceId, Guid siteId, string state, DateTimeOffset updatedAt) => new()
    {
        Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), DeviceId = deviceId, SiteId = siteId,
        RequesterKey = Guid.NewGuid().ToString("N"), Origin = "manual", Subject = "Retention fixture",
        Description = "Ticket metadata is retained after content retention.", State = state,
        CreatedAt = updatedAt, UpdatedAt = updatedAt
    };

    private static SupportAttachment Attachment(Guid ticketId, string key, DateTimeOffset createdAt) => new()
    {
        TicketId = ticketId, StorageKey = key, OriginalFileName = "fixture.txt", MediaType = "text/plain",
        SizeBytes = 3, Sha256 = new string('a', 64), ScanState = "clean", CreatedAt = createdAt
    };

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
