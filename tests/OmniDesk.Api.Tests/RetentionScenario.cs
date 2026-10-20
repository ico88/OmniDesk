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
    public async Task Device_metrics_and_inventory_retention_purges_old_snapshots_without_deleting_live_devices()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        var site = new Site { Name = "Device data retention " + Guid.NewGuid().ToString("N") };
        var oldOffline = new Device { Id = Guid.NewGuid(), Name = "old offline", SiteId = site.Id };
        var oldOnline = new Device { Id = Guid.NewGuid(), Name = "old online", SiteId = site.Id };
        var recentOffline = new Device { Id = Guid.NewGuid(), Name = "recent offline", SiteId = site.Id };

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TestDatabaseMigration.ApplyAsync(db);
            db.Sites.Add(site);
            db.Devices.AddRange(oldOffline, oldOnline, recentOffline);
            db.DevicePresence.AddRange(
                new DevicePresence { DeviceId = oldOffline.Id, IsOnline = false, ChangedAt = now.AddDays(-190) },
                new DevicePresence { DeviceId = oldOnline.Id, IsOnline = true, ChangedAt = now.AddDays(-190) },
                new DevicePresence { DeviceId = recentOffline.Id, IsOnline = false, ChangedAt = now.AddDays(-10) });
            db.DeviceMetrics.AddRange(
                new DeviceMetrics { DeviceId = oldOffline.Id, ReceivedAt = now.AddDays(-200), Payload = "{}" },
                new DeviceMetrics { DeviceId = oldOnline.Id, ReceivedAt = now.AddDays(-200), Payload = "{}" },
                new DeviceMetrics { DeviceId = recentOffline.Id, ReceivedAt = now.AddDays(-20), Payload = "{}" });
            db.DeviceInventories.AddRange(
                new DeviceInventory { DeviceId = oldOffline.Id, ReceivedAt = now.AddDays(-200), Payload = "{}" },
                new DeviceInventory { DeviceId = oldOnline.Id, ReceivedAt = now.AddDays(-200), Payload = "{}" },
                new DeviceInventory { DeviceId = recentOffline.Id, ReceivedAt = now.AddDays(-20), Payload = "{}" });
            await db.SaveChangesAsync();
        }

        try
        {
            var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
            var disabled = new CoreOutboxRetentionWorker(scopeFactory,
                Options.Create(new RetentionOptions()), NullLogger<CoreOutboxRetentionWorker>.Instance);
            Assert.Equal(0, await disabled.PurgeDeviceMetricsBatchAsync());
            Assert.Equal(0, await disabled.PurgeDeviceInventoryBatchAsync());

            var worker = new CoreOutboxRetentionWorker(scopeFactory,
                Options.Create(new RetentionOptions { DeviceMetricsDays = 180, DeviceInventoryDays = 180 }),
                NullLogger<CoreOutboxRetentionWorker>.Instance);
            Assert.Equal(1, await worker.PurgeDeviceMetricsBatchAsync());
            Assert.Equal(2, await worker.PurgeDeviceInventoryBatchAsync());

            using var verificationScope = factory.Services.CreateScope();
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await verificationDb.DeviceMetrics.AnyAsync(item => item.DeviceId == oldOffline.Id));
            Assert.True(await verificationDb.DeviceMetrics.AnyAsync(item => item.DeviceId == oldOnline.Id),
                "A device marked online is retained even when the stored snapshot is old.");
            Assert.True(await verificationDb.DeviceMetrics.AnyAsync(item => item.DeviceId == recentOffline.Id));
            Assert.False(await verificationDb.DeviceInventories.AnyAsync(item => item.DeviceId == oldOffline.Id));
            Assert.False(await verificationDb.DeviceInventories.AnyAsync(item => item.DeviceId == oldOnline.Id));
            Assert.True(await verificationDb.DeviceInventories.AnyAsync(item => item.DeviceId == recentOffline.Id));
            Assert.True(await verificationDb.Devices.AnyAsync(item => item.Id == oldOffline.Id));
            Assert.Contains(await verificationDb.AuditEvents.Where(item => item.At >= startedAt).ToArrayAsync(), item =>
                item.Action == "retention.device-metrics.purged" && item.Target == "1");
            Assert.Contains(await verificationDb.AuditEvents.Where(item => item.At >= startedAt).ToArrayAsync(), item =>
                item.Action == "retention.device-inventory.purged" && item.Target == "2");
        }
        finally
        {
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deviceIds = new[] { oldOffline.Id, oldOnline.Id, recentOffline.Id };
            await cleanupDb.DeviceMetrics.Where(item => deviceIds.Contains(item.DeviceId)).ExecuteDeleteAsync();
            await cleanupDb.DeviceInventories.Where(item => deviceIds.Contains(item.DeviceId)).ExecuteDeleteAsync();
            await cleanupDb.DevicePresence.Where(item => deviceIds.Contains(item.DeviceId)).ExecuteDeleteAsync();
            await cleanupDb.Devices.Where(item => deviceIds.Contains(item.Id)).ExecuteDeleteAsync();
            await cleanupDb.Sites.Where(item => item.Id == site.Id).ExecuteDeleteAsync();
            await cleanupDb.AuditEvents.Where(item => item.At >= startedAt &&
                    (item.Action == "retention.device-metrics.purged" || item.Action == "retention.device-inventory.purged"))
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Audit_retention_is_disabled_by_default_and_purges_old_events_in_audited_batches()
    {
        var marker = "retention-audit-" + Guid.NewGuid().ToString("N");
        var testStartedAt = DateTimeOffset.UtcNow;
        var oldAt = DateTimeOffset.UtcNow.AddDays(-400);
        var recentAt = DateTimeOffset.UtcNow.AddDays(-20);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TestDatabaseMigration.ApplyAsync(db);
            db.AuditEvents.AddRange(
                new AuditEvent { At = oldAt, Action = marker, Target = "old-1" },
                new AuditEvent { At = oldAt.AddMinutes(1), Action = marker, Target = "old-2" },
                new AuditEvent { At = oldAt, Action = "retention.audit-events.purged", Target = "historic" },
                new AuditEvent { At = recentAt, Action = marker, Target = "recent" });
            await db.SaveChangesAsync();
        }

        try
        {
            var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
            var disabled = new CoreOutboxRetentionWorker(scopeFactory,
                Options.Create(new RetentionOptions()), NullLogger<CoreOutboxRetentionWorker>.Instance);
            Assert.Equal(0, await disabled.PurgeAuditEventsBatchAsync());
            var worker = new CoreOutboxRetentionWorker(scopeFactory,
                Options.Create(new RetentionOptions { AuditEventsDays = 365 }), NullLogger<CoreOutboxRetentionWorker>.Instance);
            Assert.Equal(2, await worker.PurgeAuditEventsBatchAsync());

            using var verificationScope = factory.Services.CreateScope();
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var remaining = await verificationDb.AuditEvents.Where(item => item.Action == marker ||
                    item.Action == "retention.audit-events.purged" && item.Target == "historic")
                .Select(item => item.Target).ToArrayAsync();
            Assert.Equal(new[] { "recent", "historic" }.OrderBy(x => x), remaining.OrderBy(x => x));
            Assert.Contains(await verificationDb.AuditEvents.ToArrayAsync(), item =>
                item.Action == "retention.audit-events.purged" && item.Target == "2");
        }
        finally
        {
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await cleanupDb.AuditEvents.Where(item => item.Action == marker ||
                    item.Action == "retention.audit-events.purged" && item.At >= testStartedAt)
                .ExecuteDeleteAsync();
        }
    }

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
    public async Task Ticket_content_retention_redacts_old_resolved_content_but_keeps_structure_and_operational_history()
    {
        var now = DateTimeOffset.UtcNow;
        var site = new Site { Name = "Ticket content retention " + Guid.NewGuid().ToString("N") };
        var device = new Device { Id = Guid.NewGuid(), Name = "Ticket content device", SiteId = site.Id };
        var oldTicket = Ticket(device.Id, site.Id, "closed", now.AddDays(-200));
        oldTicket.Subject = "Secret customer subject";
        oldTicket.Description = "Secret customer description";
        oldTicket.Resolution = "Secret resolution text";
        var recentTicket = Ticket(device.Id, site.Id, "resolved", now.AddDays(-20));
        var openTicket = Ticket(device.Id, site.Id, "open", now.AddDays(-300));
        var oldReply = new SupportMessage { TicketId = oldTicket.Id, Kind = "portal_reply", Body = "Secret public conversation", CreatedAt = now.AddDays(-199) };
        var oldNote = new SupportMessage { TicketId = oldTicket.Id, Kind = "internal_note", Visibility = "internal", Body = "Secret internal note", CreatedAt = now.AddDays(-198) };
        var intervention = new SupportMessage { TicketId = oldTicket.Id, Kind = "rmm_intervention", Visibility = "internal", Body = "RMM artifact reference", CreatedAt = now.AddDays(-197) };
        var recentReply = new SupportMessage { TicketId = recentTicket.Id, Kind = "portal_reply", Body = "Recent conversation" };
        var openReply = new SupportMessage { TicketId = openTicket.Id, Kind = "reply", Body = "Open ticket conversation" };
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await TestDatabaseMigration.ApplyAsync(db);
                db.Sites.Add(site);
                db.Devices.Add(device);
                db.SupportTickets.AddRange(oldTicket, recentTicket, openTicket);
                db.SupportMessages.AddRange(oldReply, oldNote, intervention, recentReply, openReply);
                await db.SaveChangesAsync();
            }

            var disabled = new CoreOutboxRetentionWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RetentionOptions()), NullLogger<CoreOutboxRetentionWorker>.Instance);
            Assert.Equal(0, await disabled.PurgeResolvedTicketContentBatchAsync());
            var worker = new CoreOutboxRetentionWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RetentionOptions { ResolvedTicketContentDays = 180 }), NullLogger<CoreOutboxRetentionWorker>.Instance);
            Assert.Equal(1, await worker.PurgeResolvedTicketContentBatchAsync());

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var purged = await db.SupportTickets.SingleAsync(ticket => ticket.Id == oldTicket.Id);
                Assert.True(purged.ContentPurged);
                Assert.Equal($"Ticket #{purged.Number}", purged.Subject);
                Assert.Equal("", purged.Description);
                Assert.Null(purged.Resolution);
                Assert.InRange(Math.Abs((now.AddDays(-200) - purged.UpdatedAt).Ticks), 0, TimeSpan.TicksPerMicrosecond);
                Assert.True((await db.SupportMessages.SingleAsync(message => message.Id == oldReply.Id)).ContentPurged);
                Assert.Equal("", (await db.SupportMessages.SingleAsync(message => message.Id == oldNote.Id)).Body);
                Assert.Equal("RMM artifact reference", (await db.SupportMessages.SingleAsync(message => message.Id == intervention.Id)).Body);
                Assert.False((await db.SupportTickets.SingleAsync(ticket => ticket.Id == recentTicket.Id)).ContentPurged);
                Assert.Equal("Recent conversation", (await db.SupportMessages.SingleAsync(message => message.Id == recentReply.Id)).Body);
                Assert.False((await db.SupportTickets.SingleAsync(ticket => ticket.Id == openTicket.Id)).ContentPurged);
                Assert.Equal("Open ticket conversation", (await db.SupportMessages.SingleAsync(message => message.Id == openReply.Id)).Body);
                Assert.Contains(await db.AuditEvents.ToArrayAsync(), audit => audit.Action == "retention.ticket-content.purged" && audit.Target == "tickets=1;messages=2");
            }
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SupportMessages.Where(message => message.TicketId == oldTicket.Id || message.TicketId == recentTicket.Id || message.TicketId == openTicket.Id).ExecuteDeleteAsync();
            await db.SupportTickets.Where(ticket => ticket.Id == oldTicket.Id || ticket.Id == recentTicket.Id || ticket.Id == openTicket.Id).ExecuteDeleteAsync();
            await db.Devices.Where(item => item.Id == device.Id).ExecuteDeleteAsync();
            await db.Sites.Where(item => item.Id == site.Id).ExecuteDeleteAsync();
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
