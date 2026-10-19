using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Security;

public sealed record TicketWorkflowState(string Code, string Label, bool RequiresResolution = false);
public sealed record TicketWorkflowTransition(string From, string To);
public sealed record TicketWorkflowDefinition(TicketWorkflowState[] States, TicketWorkflowTransition[] Transitions)
{
    public static TicketWorkflowDefinition Default { get; } = new(
        [new("open", "Aperto"), new("in_progress", "In lavorazione"), new("resolved", "Risolto", true), new("closed", "Chiuso", true)],
        [new("open", "open"), new("open", "in_progress"), new("open", "resolved"),
            new("in_progress", "in_progress"), new("in_progress", "open"), new("in_progress", "resolved"),
            new("resolved", "resolved"), new("resolved", "open"), new("resolved", "in_progress"), new("resolved", "closed"),
            new("closed", "closed")]);
}

public sealed record TicketWorkflowBinding(Guid? WorkflowId, int? WorkflowVersion);

public static class TicketWorkflowPolicy
{
    private static readonly Regex StateCode = new("^[a-z][a-z0-9_]{0,19}$", RegexOptions.CultureInvariant);
    public static bool IsValidStateCode(string value) => StateCode.IsMatch(value);

    public static bool Validate(TicketWorkflowDefinition? definition, out string error)
    {
        error = "La definizione del workflow non è valida.";
        if (definition?.States is null || definition.Transitions is null || definition.States.Length is < 3 or > 16 ||
            definition.Transitions.Length is < 3 or > 256) return false;
        if (definition.States.Any(s => s is null || !StateCode.IsMatch(s.Code) || string.IsNullOrWhiteSpace(s.Label) || s.Label.Trim().Length > 80)) return false;
        if (definition.States.Select(s => s.Code).Distinct(StringComparer.Ordinal).Count() != definition.States.Length) return false;
        var codes = definition.States.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
        if (!codes.Contains("open") || !codes.Contains("resolved") || !codes.Contains("closed")) return false;
        if (definition.Transitions.Any(t => t is null || !codes.Contains(t.From) || !codes.Contains(t.To)) ||
            definition.Transitions.Select(t => (t.From, t.To)).Distinct().Count() != definition.Transitions.Length) return false;
        if (definition.States.Any(s => !definition.Transitions.Any(t => t.From == s.Code && t.To == s.Code))) return false;
        if (definition.Transitions.Any(t => t.From == "closed" && t.To != "closed")) return false;
        if (definition.States.Where(s => s.RequiresResolution).All(s => s.Code != "resolved") ||
            !definition.States.Single(s => s.Code == "closed").RequiresResolution) return false;
        error = "";
        return true;
    }

    public static string[] AllowedStates(string state) => AllowedStates(TicketWorkflowDefinition.Default, state);
    public static string[] AllowedStates(TicketWorkflowDefinition? definition, string state) => definition is null
        ? [state]
        : definition.Transitions.Where(t => t.From == state).Select(t => t.To).Distinct(StringComparer.Ordinal).ToArray();

    public static bool Allows(string from, string to) => Allows(TicketWorkflowDefinition.Default, from, to);
    public static bool Allows(TicketWorkflowDefinition? definition, string from, string to) =>
        definition is not null && definition.Transitions.Any(t => t.From == from && t.To == to);

    public static bool RequiresResolution(string state) => RequiresResolution(TicketWorkflowDefinition.Default, state);
    public static bool RequiresResolution(TicketWorkflowDefinition? definition, string state) =>
        definition?.States.Any(s => s.Code == state && s.RequiresResolution) ?? false;

    public static string Label(TicketWorkflowDefinition definition, string state) =>
        definition.States.SingleOrDefault(s => s.Code == state)?.Label ?? state;

    public static string Serialize(TicketWorkflowDefinition definition) => JsonSerializer.Serialize(definition);

    public static TicketWorkflowDefinition? Deserialize(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<TicketWorkflowDefinition>(json);
            return Validate(value, out _) ? value : null;
        }
        catch (JsonException) { return null; }
    }
}

public static class TicketWorkflowResolver
{
    public static async Task<TicketWorkflowBinding> BindForNewTicket(AppDbContext db, Guid siteId, CancellationToken ct = default)
    {
        var siteKey = "site:" + siteId.ToString("D");
        var workflow = await db.SupportWorkflows.AsNoTracking()
            .Where(x => (x.ScopeKey == siteKey || x.ScopeKey == "global") && x.ActiveVersion > 0)
            .OrderByDescending(x => x.SiteId != null).Select(x => new { x.Id, x.ActiveVersion }).FirstOrDefaultAsync(ct);
        return workflow is null ? new(null, null) : new(workflow.Id, workflow.ActiveVersion);
    }

    public static async Task<TicketWorkflowDefinition?> ForTicket(AppDbContext db, SupportTicket ticket, CancellationToken ct = default)
    {
        if (ticket.WorkflowId is null && ticket.WorkflowVersion is null) return TicketWorkflowDefinition.Default;
        if (ticket.WorkflowId is null || ticket.WorkflowVersion is null) return null;
        var json = await db.SupportWorkflowVersions.AsNoTracking()
            .Where(x => x.WorkflowId == ticket.WorkflowId && x.Version == ticket.WorkflowVersion)
            .Select(x => x.DefinitionJson).SingleOrDefaultAsync(ct);
        return json is null ? null : TicketWorkflowPolicy.Deserialize(json);
    }
}
