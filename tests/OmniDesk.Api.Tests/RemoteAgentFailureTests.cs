using OmniDesk.Agent.Windows;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class RemoteAgentFailureTests
{
    [Fact]
    public void Access_denial_points_operator_to_windows_security_without_bypassing_it()
    {
        var result = RemoteAgentFailure.Classify(new UnauthorizedAccessException());

        Assert.Equal("binary_access_denied", result.Code);
        Assert.Contains("Windows Security", result.Guidance);
        Assert.Contains("hash check", result.Guidance);
        Assert.DoesNotContain("exclusion", result.Guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_remote_binary_has_repair_guidance()
    {
        var result = RemoteAgentFailure.Classify(new FileNotFoundException());

        Assert.Equal("binary_missing", result.Code);
        Assert.Contains("Protection history", result.Guidance);
        Assert.Contains("repair", result.Guidance);
    }

    [Fact]
    public void Http_failure_points_to_connectivity()
    {
        var result = RemoteAgentFailure.Classify(new HttpRequestException());

        Assert.Equal("server_unreachable", result.Code);
        Assert.Contains("network connectivity", result.Guidance);
    }
}
