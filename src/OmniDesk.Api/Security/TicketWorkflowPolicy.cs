namespace OmniDesk.Api.Security;

/// <summary>Default ticket lifecycle until a published workflow version is configured.</summary>
public static class TicketWorkflowPolicy
{
    private static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["open"] = ["open", "in_progress", "resolved"],
        ["in_progress"] = ["in_progress", "open", "resolved"],
        ["resolved"] = ["resolved", "open", "in_progress", "closed"],
        ["closed"] = ["closed"]
    };

    public static string[] AllowedStates(string state) => Transitions.TryGetValue(state, out var allowed)
        ? [.. allowed]
        : [state];

    public static bool Allows(string from, string to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to, StringComparer.Ordinal);

    public static bool RequiresResolution(string state) => state is "resolved" or "closed";
}
