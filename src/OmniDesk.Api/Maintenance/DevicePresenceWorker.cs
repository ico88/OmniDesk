using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Maintenance;

public sealed class DevicePresenceOptions
{
    public const string SectionName = "DevicePresence";
    public int OfflineAfterSeconds { get; set; } = 180;
    public int PollSeconds { get; set; } = 30;
    public static DateTimeOffset Cutoff(DateTimeOffset now, DevicePresenceOptions settings) =>
        now.AddSeconds(-Math.Clamp(settings.OfflineAfterSeconds, 30, 86400));
}

public sealed class DevicePresenceWorker(
    IServiceScopeFactory scopes,
    IOptions<DevicePresenceOptions> options,
    ILogger<DevicePresenceWorker> logger) : BackgroundService
{
    private readonly DevicePresenceOptions settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.PollSeconds, 5, 3600));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await SweepOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Device presence sweep failed"); }
        }
    }

    // Existing devices are seeded without synthetic transition audit. Later state changes
    // are conditional on the heartbeat still being stale/fresh at update time.
    public async Task SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = DevicePresenceOptions.Cutoff(now, settings);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "DevicePresence" ("DeviceId", "IsOnline", "ChangedAt")
            SELECT d."Id", m."ReceivedAt" >= {cutoff}, {now}
            FROM "Devices" d
            JOIN "DeviceCredentials" c ON c."DeviceId" = d."Id" AND c."RevokedAt" IS NULL
            JOIN "DeviceMetrics" m ON m."DeviceId" = d."Id"
            ON CONFLICT ("DeviceId") DO NOTHING
            """, cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH changed AS (
                UPDATE "DevicePresence" p
                SET "IsOnline" = (m."ReceivedAt" >= {cutoff}), "ChangedAt" = {now}
                FROM "DeviceMetrics" m
                JOIN "DeviceCredentials" c ON c."DeviceId" = m."DeviceId" AND c."RevokedAt" IS NULL
                WHERE p."DeviceId" = m."DeviceId"
                  AND p."IsOnline" IS DISTINCT FROM (m."ReceivedAt" >= {cutoff})
                RETURNING p."DeviceId", p."IsOnline"
            ), audit AS (
                INSERT INTO "AuditEvents" ("At", "ActorId", "Action", "Target", "Outcome")
                SELECT {now}, NULL,
                   CASE WHEN "IsOnline" THEN 'device.presence.online' ELSE 'device.presence.offline' END,
                   "DeviceId"::text,
                   CASE WHEN "IsOnline" THEN 'success' ELSE 'warning' END
                FROM changed
                RETURNING 1
            )
            INSERT INTO "CoreOutboxEvents" ("Id", "EventType", "SchemaVersion", "AggregateType", "AggregateId", "OccurredAt", "CorrelationId", "Payload", "AttemptCount", "NextAttemptAt")
            SELECT gen_random_uuid(), 'device.presence.offline', 1, 'device', "DeviceId", {now}, gen_random_uuid()::text,
                   jsonb_build_object('deviceId', "DeviceId"), 0, {now}
            FROM changed WHERE NOT "IsOnline"
            """, cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "DevicePresence" p
            WHERE EXISTS (
                SELECT 1 FROM "DeviceCredentials" c
                WHERE c."DeviceId" = p."DeviceId" AND c."RevokedAt" IS NOT NULL
            )
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
