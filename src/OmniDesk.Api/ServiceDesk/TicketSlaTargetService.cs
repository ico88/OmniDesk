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
            definition.Calendar.WeeklyHours.Count == 0 || definition.Calendar.WeeklyHours.Values.Any(ranges => ranges is null || ranges.Length == 0))
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
