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
}
