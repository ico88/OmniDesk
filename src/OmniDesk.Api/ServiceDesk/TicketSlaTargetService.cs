using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.ServiceDesk;

public sealed class SlaPolicyConfigurationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Creates immutable initial SLA timer targets from the policy version effective at ticket creation.</summary>
public static class TicketSlaTargetService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task ApplyInitialTargets(AppDbContext db, SupportTicket ticket, CancellationToken cancellationToken = default)
    {
        var siteKey = "site:" + ticket.SiteId.ToString("D");
        var policy = await db.SupportSlaPolicies.AsNoTracking()
            .Where(x => x.ScopeKey == siteKey || x.ScopeKey == "global")
            .OrderByDescending(x => x.SiteId != null)
            .FirstOrDefaultAsync(cancellationToken);
        if (policy is null) return;

        var version = await db.SupportSlaPolicyVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PolicyId == policy.Id && x.Version == policy.CurrentVersion, cancellationToken);
        if (version is null)
            throw new SlaPolicyConfigurationException($"The current SLA policy version for policy {policy.Id} is missing.");

        SlaPolicyDefinition? definition;
        try { definition = JsonSerializer.Deserialize<SlaPolicyDefinition>(version.DefinitionJson, Json); }
        catch (JsonException exception)
        { throw new SlaPolicyConfigurationException($"The current SLA policy version for policy {policy.Id} is invalid.", exception); }

        var calendar = ValidateAndCreateCalendar(definition, policy.Id);
        db.SupportTicketSlaTargets.AddRange(
            Create(ticket, policy.Id, version.Version, "first_response", definition!.FirstResponseMinutes, calendar),
            Create(ticket, policy.Id, version.Version, "resolution", definition.ResolutionMinutes, calendar));
    }

    public static Task<int> CompleteTarget(AppDbContext db, Guid ticketId, string targetType,
        DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        if (targetType is not ("first_response" or "resolution"))
            throw new ArgumentOutOfRangeException(nameof(targetType));
        return CompleteLatestTarget(db, ticketId, targetType, completedAt, cancellationToken);
    }

    private static async Task<int> CompleteLatestTarget(AppDbContext db, Guid ticketId, string targetType,
        DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        var cycle = await db.SupportTicketSlaTargets.AsNoTracking()
            .Where(x => x.TicketId == ticketId && x.TargetType == targetType && x.CompletedAt == null)
            .MaxAsync(x => (int?)x.Cycle, cancellationToken);
        if (cycle is null) return 0;
        return await db.SupportTicketSlaTargets
            .Where(x => x.TicketId == ticketId && x.TargetType == targetType && x.Cycle == cycle && x.CompletedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.CompletedAt, completedAt.ToUniversalTime()), cancellationToken);
    }

    public static async Task<int> ApplyPauseState(AppDbContext db, Guid ticketId, string newState,
        string actorId, DateTimeOffset changedAt, CancellationToken cancellationToken = default)
    {
        var targets = await db.SupportTicketSlaTargets.AsNoTracking().Where(x => x.TicketId == ticketId && x.CompletedAt == null)
            .OrderBy(x => x.Cycle).ToArrayAsync(cancellationToken);
        var changed = 0;
        foreach (var target in targets)
        {
            var version = await db.SupportSlaPolicyVersions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PolicyId == target.PolicyId && x.Version == target.PolicyVersion, cancellationToken);
            if (version is null) throw new SlaPolicyConfigurationException($"SLA policy version {target.PolicyId}/{target.PolicyVersion} is missing.");
            SlaPolicyDefinition? definition;
            try { definition = JsonSerializer.Deserialize<SlaPolicyDefinition>(version.DefinitionJson, Json); }
            catch (JsonException exception) { throw new SlaPolicyConfigurationException("The SLA policy pause configuration is invalid.", exception); }
            var pauseStates = definition?.PauseStates ?? [];
            var shouldPause = pauseStates.Contains(newState, StringComparer.Ordinal);
            if (shouldPause && target.PausedAt is null && target.DueAt > changedAt.ToUniversalTime())
            {
                var pauseTime = changedAt.ToUniversalTime();
                var updated = await db.SupportTicketSlaTargets.Where(x => x.Id == target.Id && x.PausedAt == null &&
                        x.CompletedAt == null && x.BreachedAt == null && x.DueAt > pauseTime)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.PausedAt, pauseTime), cancellationToken);
                if (updated > 0)
                {
                    db.AuditEvents.Add(new AuditEvent { ActorId = actorId, Action = "ticket.sla.target.paused", Target = ticketId.ToString() });
                    changed++;
                }
            }
            else if (!shouldPause && target.PausedAt is { } pausedAt)
            {
                var duration = changedAt.ToUniversalTime() - pausedAt;
                if (duration < TimeSpan.Zero) throw new SlaPolicyConfigurationException("SLA pause end precedes its start.");
                // Store whole elapsed seconds without rounding a fractional boundary up.
                var accumulated = checked(target.AccumulatedPauseSeconds + (long)Math.Floor(duration.TotalSeconds));
                var updated = await db.SupportTicketSlaTargets.Where(x => x.Id == target.Id && x.PausedAt == pausedAt && x.CompletedAt == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.DueAt, target.DueAt.Add(duration))
                        .SetProperty(x => x.AccumulatedPauseSeconds, accumulated).SetProperty(x => x.PausedAt, (DateTimeOffset?)null), cancellationToken);
                if (updated > 0)
                {
                    db.AuditEvents.Add(new AuditEvent { ActorId = actorId, Action = "ticket.sla.target.resumed", Target = ticketId.ToString() });
                    changed++;
                }
            }
        }
        return changed;
    }

    public static async Task<bool> StartResolutionCycle(AppDbContext db, Guid ticketId, DateTimeOffset startedAt,
        string actorId, CancellationToken cancellationToken = default)
    {
        var previous = await db.SupportTicketSlaTargets.AsNoTracking().Where(x => x.TicketId == ticketId && x.TargetType == "resolution")
            .OrderByDescending(x => x.Cycle).FirstOrDefaultAsync(cancellationToken);
        if (previous is null || previous.CompletedAt is null) return false;
        var version = await db.SupportSlaPolicyVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PolicyId == previous.PolicyId && x.Version == previous.PolicyVersion, cancellationToken);
        if (version is null) throw new SlaPolicyConfigurationException("The SLA policy version for the reopened target is missing.");
        SlaPolicyDefinition? definition;
        try { definition = JsonSerializer.Deserialize<SlaPolicyDefinition>(version.DefinitionJson, Json); }
        catch (JsonException exception) { throw new SlaPolicyConfigurationException("The SLA policy version for the reopened target is invalid.", exception); }
        var calendar = ValidateAndCreateCalendar(definition, previous.PolicyId);
        db.SupportTicketSlaTargets.Add(new SupportTicketSlaTarget
        {
            TicketId = ticketId,
            TargetType = "resolution",
            PolicyId = previous.PolicyId,
            PolicyVersion = previous.PolicyVersion,
            Cycle = checked(previous.Cycle + 1),
            StartedAt = startedAt.ToUniversalTime(),
            DueAt = BusinessCalendar.AddWorkingTime(startedAt, TimeSpan.FromMinutes(definition!.ResolutionMinutes), calendar)
        });
        db.AuditEvents.Add(new AuditEvent { ActorId = actorId, Action = "ticket.sla.target.reopened", Target = ticketId.ToString() });
        return true;
    }

    private static SupportTicketSlaTarget Create(SupportTicket ticket, Guid policyId, int policyVersion,
        string targetType, int minutes, BusinessCalendarDefinition calendar) => new()
    {
        TicketId = ticket.Id,
        TargetType = targetType,
        PolicyId = policyId,
        PolicyVersion = policyVersion,
        Cycle = 1,
        StartedAt = ticket.CreatedAt.ToUniversalTime(),
        DueAt = BusinessCalendar.AddWorkingTime(ticket.CreatedAt, TimeSpan.FromMinutes(minutes), calendar)
    };

    private static BusinessCalendarDefinition ValidateAndCreateCalendar(SlaPolicyDefinition? definition, Guid policyId)
    {
        if (definition?.Calendar is null || definition.Calendar.WeeklyHours is null || definition.Calendar.Holidays is null ||
            definition.FirstResponseMinutes is < 1 or > 525600 || definition.ResolutionMinutes is < 1 or > 525600 ||
            definition.ResolutionMinutes < definition.FirstResponseMinutes || definition.Calendar.Holidays.Length > 3660 ||
            definition.Calendar.WeeklyHours.Count == 0 || definition.Calendar.WeeklyHours.Values.Any(ranges => ranges is null || ranges.Length == 0) ||
            definition.PauseStates is null || definition.PauseStates.Length > 20 ||
            definition.PauseStates.Distinct(StringComparer.Ordinal).Count() != definition.PauseStates.Length ||
            definition.PauseStates.Any(state => state is null || state is "resolved" or "closed" ||
                !System.Text.RegularExpressions.Regex.IsMatch(state, "^[a-z][a-z0-9_]{0,39}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
            throw new SlaPolicyConfigurationException($"The current SLA policy definition for policy {policyId} is invalid.");

        try
        {
            var calendar = new BusinessCalendarDefinition(definition.Calendar.TimeZoneId,
                definition.Calendar.WeeklyHours.ToDictionary(x => x.Key, x => (IReadOnlyList<BusinessTimeRange>)x.Value),
                definition.Calendar.Holidays.ToHashSet());
            BusinessCalendar.ValidateDefinition(calendar);
            return calendar;
        }
        catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new SlaPolicyConfigurationException($"The current SLA calendar for policy {policyId} is invalid.", exception); }
    }
}
