using OmniDesk.Api.Security;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class TicketWorkflowPolicyTests
{
    [Theory]
    [InlineData("open", "open", true)]
    [InlineData("open", "in_progress", true)]
    [InlineData("open", "resolved", true)]
    [InlineData("open", "closed", false)]
    [InlineData("in_progress", "open", true)]
    [InlineData("in_progress", "resolved", true)]
    [InlineData("in_progress", "closed", false)]
    [InlineData("resolved", "open", true)]
    [InlineData("resolved", "in_progress", true)]
    [InlineData("resolved", "closed", true)]
    [InlineData("closed", "closed", true)]
    [InlineData("closed", "open", false)]
    public void Default_workflow_allows_only_declared_transitions(string from, string to, bool expected) =>
        Assert.Equal(expected, TicketWorkflowPolicy.Allows(from, to));

    [Fact]
    public void Closed_ticket_has_no_outgoing_transition_and_closed_state_requires_resolution()
    {
        Assert.Equal(["closed"], TicketWorkflowPolicy.AllowedStates("closed"));
        Assert.True(TicketWorkflowPolicy.RequiresResolution("resolved"));
        Assert.True(TicketWorkflowPolicy.RequiresResolution("closed"));
        Assert.False(TicketWorkflowPolicy.RequiresResolution("in_progress"));
    }

    [Fact]
    public void Published_custom_definition_controls_labels_transitions_and_resolution_requirements()
    {
        var definition = CustomDefinition();
        Assert.True(TicketWorkflowPolicy.Validate(definition, out _));
        Assert.True(TicketWorkflowPolicy.Allows(definition, "open", "waiting_vendor"));
        Assert.False(TicketWorkflowPolicy.Allows(definition, "open", "closed"));
        Assert.True(TicketWorkflowPolicy.RequiresResolution(definition, "resolved"));
        Assert.Equal("In attesa fornitore", TicketWorkflowPolicy.Label(definition, "waiting_vendor"));
        Assert.Equal("waiting_vendor", TicketWorkflowPolicy.Deserialize(TicketWorkflowPolicy.Serialize(definition))!.States[2].Code);
    }

    [Fact]
    public void Workflow_definition_requires_known_states_resolution_and_terminal_closed()
    {
        var definition = CustomDefinition();
        Assert.False(TicketWorkflowPolicy.Validate(definition with { Transitions = [.. definition.Transitions, new("closed", "open")] }, out _));
        Assert.False(TicketWorkflowPolicy.Validate(definition with { States = definition.States.Where(s => s.Code != "closed").ToArray() }, out _));
        Assert.False(TicketWorkflowPolicy.Validate(definition with { States = definition.States.Select(s => s.Code == "resolved" ? s with { RequiresResolution = false } : s).ToArray() }, out _));
    }

    public static TicketWorkflowDefinition CustomDefinition() => new(
        [new("open", "Aperto"), new("in_progress", "In lavorazione"), new("waiting_vendor", "In attesa fornitore"),
            new("resolved", "Risolto", true), new("closed", "Chiuso", true)],
        [new("open", "open"), new("open", "in_progress"), new("open", "waiting_vendor"), new("open", "resolved"),
            new("in_progress", "in_progress"), new("in_progress", "open"), new("in_progress", "waiting_vendor"), new("in_progress", "resolved"),
            new("waiting_vendor", "waiting_vendor"), new("waiting_vendor", "in_progress"), new("waiting_vendor", "resolved"),
            new("resolved", "resolved"), new("resolved", "open"), new("resolved", "in_progress"), new("resolved", "closed"),
            new("closed", "closed")]);
}
