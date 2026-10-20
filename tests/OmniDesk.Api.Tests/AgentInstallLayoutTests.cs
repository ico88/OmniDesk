using OmniDesk.Agent.Windows;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class AgentInstallLayoutTests
{
    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"D:\Apps")]
    public void Updater_checks_the_installed_agent_not_its_staging_directory(string programFiles)
    {
        var path = AgentInstallLayout.ExecutablePath(programFiles);
        Assert.Equal(Path.Combine("OmniDesk Agent", "OmniDesk.Agent.Windows.exe"), Path.GetRelativePath(programFiles, path));
    }
}
