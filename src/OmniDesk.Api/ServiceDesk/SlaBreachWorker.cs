using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.ServiceDesk;

/// <summary>Persists each overdue SLA breach once and emits a transactional notification event.</summary>
public sealed class SlaBreachWorker(IServiceScopeFactory scopes, ILogger<SlaBreachWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
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
                catch (Exception exception)
                {
                    logger.LogWarning("SLA breach worker failed: {Type}", exception.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<bool> ProcessNext(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var processed = await MarkNextBreach(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return processed;
    }

    /// <summary>Caller owns the transaction. FOR UPDATE SKIP LOCKED makes competing workers idempotent.</summary>
    public static async Task<bool> MarkNextBreach(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.SupportTicketSlaTargets.FromSqlInterpolated($"""
            SELECT t.* FROM "SupportTicketSlaTargets" t
            WHERE t."DueAt" <= {now} AND t."CompletedAt" IS NULL AND t."BreachedAt" IS NULL
            ORDER BY t."DueAt", t."Id"
            FOR UPDATE OF t SKIP LOCKED
            LIMIT 1
            """).ToListAsync(cancellationToken);
        var target = rows.SingleOrDefault();
        if (target is null) return false;

        target.BreachedAt = now;
        var ticket = await db.SupportTickets.AsNoTracking().SingleAsync(x => x.Id == target.TicketId, cancellationToken);
        db.AuditEvents.Add(new AuditEvent
        {
            Action = "ticket.sla.target.breached",
            Target = target.TicketId.ToString(),
            Outcome = "success"
        });
        CoreOutbox.Add(db, "ticket.sla.breached", "ticket", ticket.Id, new
        {
            ticketId = ticket.Id,
            deviceId = ticket.DeviceId,
            siteId = ticket.SiteId,
            assigneeId = ticket.AssigneeId,
            queueId = ticket.QueueId,
            targetType = target.TargetType,
            policyId = target.PolicyId,
            policyVersion = target.PolicyVersion,
            cycle = target.Cycle,
            dueAt = target.DueAt
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
