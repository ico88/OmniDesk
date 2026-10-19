using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using OmniDesk.Contracts;

namespace OmniDesk.Agent.Windows;

internal static class AgentUpdater
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task Apply(string receiptPath, string msiPath)
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        if (WindowsIdentity.GetCurrent().User is not { } currentUser || !system.Equals(currentUser)) throw new UnauthorizedAccessException("Updater must run as LocalSystem.");
        var folder = Path.GetFullPath(AgentIdentity.Folder) + Path.DirectorySeparatorChar;
        receiptPath = Path.GetFullPath(receiptPath); msiPath = Path.GetFullPath(msiPath);
        if (!receiptPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !msiPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Updater paths must remain under the protected data directory.");
        AgentIdentity.AssertTrusted(new DirectoryInfo(AgentIdentity.Folder));
        AgentIdentity.AssertTrusted(new FileInfo(receiptPath)); AgentIdentity.AssertTrusted(new FileInfo(msiPath));
        var receipt = JsonSerializer.Deserialize<UpdateReceipt>(await File.ReadAllTextAsync(receiptPath), Json)
            ?? throw new InvalidDataException("Update receipt is invalid.");
        if (receipt.Kind != "agent.update" || receipt.State != "installing" || receipt.Result is not null ||
            !Version.TryParse(receipt.TargetVersion, out var target) || receipt.MsiSha256 is not { Length: 64 })
            throw new InvalidDataException("Update receipt state is invalid.");
        var msiName = $"agent-update-{receipt.JobId:N}.msi";
        if (!string.Equals(Path.GetFileName(msiPath), msiName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("MSI path does not match the update job.");
        await using (var verify = File.OpenRead(msiPath))
            if (Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(verify)).ToLowerInvariant() != receipt.MsiSha256)
                throw new InvalidDataException("Staged MSI hash mismatch.");

        var logPath = Path.Combine(AgentIdentity.Folder, $"agent-update-{receipt.JobId:N}.log");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msiexec.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "/i", msiPath, "/qn", "/norestart", "/l*v", logPath }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows Installer could not be started.");
        await process.WaitForExitAsync();
        var ok = process.ExitCode is 0 or 3010;
        var installedVersion = FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "OmniDesk.Agent.Windows.exe")).ProductVersion?.Split('+')[0];
        ok &= Version.TryParse(installedVersion, out var actual) && actual >= target;
        var result = new UpdateResultRequest(receipt.LeaseId, ok, process.ExitCode == 3010, [], ok ? null : $"Windows Installer failed or installed version did not reach {target} (exit {process.ExitCode}). Log: {Path.GetFileName(logPath)}");
        UpdateWorker.Save(receiptPath, receipt with { State = ok ? "installed" : "failed", Result = result });
    }
}
