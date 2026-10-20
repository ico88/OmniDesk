namespace OmniDesk.Agent.Windows;

internal static class AgentInstallLayout
{
    internal static string ExecutablePath(string programFiles) =>
        Path.Combine(programFiles, "OmniDesk Agent", "OmniDesk.Agent.Windows.exe");
}
