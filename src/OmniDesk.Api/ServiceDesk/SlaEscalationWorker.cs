using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.ServiceDesk;

/// <summary>Raises each configured post-breach SLA escalation once, using persisted breach timestamps.</summary>
public sealed class SlaEscalationWorker(IServiceScopeFactory scopes, ILogger<SlaEscalationWorker> logger) : BackgroundService
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
                    logger.LogWarning("SLA escalation worker failed: {Type}", exception.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<bool> ProcessNext(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var processed = await MarkNextEscalation(db, DateTimeOffset.UtcNow, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return processed;
    }

    /// <summary>Caller owns the transaction; target row locking and the unique key prevent duplicate escalation.</summary>
    public static async Task<bool> MarkNextEscalation(AppDbContext db, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var candidate = await db.Database.SqlQuery<SlaEscalationCandidate>($"""
            SELECT t."Id" AS "TargetId", step.value::integer AS "StepMinutes"
            FROM "SupportTicketSlaTargets" t
            JOIN "SupportSlaPolicyVersions" v ON v."PolicyId" = t."PolicyId" AND v."Version" = t."PolicyVersion"
            CROSS JOIN LATERAL jsonb_array_elements_text(COALESCE(v."DefinitionJson"->'escalationMinutes', '[]'::jsonb)) step(value)
            WHERE t."BreachedAt" IS NOT NULL AND t."CompletedAt" IS NULL
              AND t."BreachedAt" <= {now.ToUniversalTime()}
              AND t."BreachedAt" + (step.value::integer * interval '1 minute') <= {now.ToUniversalTime()}
              AND NOT EXISTS (SELECT 1 FROM "SupportTicketSlaEscalations" e
                WHERE e."TargetId" = t."Id" AND e."StepMinutes" = step.value::integer)
            ORDER BY t."BreachedAt", step.value::integer, t."Id"
            FOR UPDATE OF t SKIP LOCKED
            LIMIT 1
            """).SingleOrDefaultAsync(cancellationToken);
        if (candidate is null) return false;

        var target = await db.SupportTicketSlaTargets.SingleAsync(x => x.Id == candidate.TargetId, cancellationToken);
        var ticket = await db.SupportTickets.AsNoTracking().SingleAsync(x => x.Id == target.TicketId, cancellationToken);
        db.SupportTicketSlaEscalations.Add(new SupportTicketSlaEscalation
        {
            TargetId = target.Id,
            StepMinutes = candidate.StepMinutes,
            EscalatedAt = now.ToUniversalTime()
        });
        db.AuditEvents.Add(new AuditEvent
        {
            Action = "ticket.sla.escalated",
            Target = ticket.Id.ToString(),
            Outcome = "success"
        });
        CoreOutbox.Add(db, "ticket.sla.escalated", "ticket", ticket.Id, new
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
            stepMinutes = candidate.StepMinutes,
            breachedAt = target.BreachedAt
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private sealed class SlaEscalationCandidate
    {
        public Guid TargetId { get; set; }
        public int StepMinutes { get; set; }
    }
}
