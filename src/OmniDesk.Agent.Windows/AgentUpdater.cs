using System.Diagnostics;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;
using OmniDesk.Contracts;
using ServiceController = System.ServiceProcess.ServiceController;
using ServiceControllerStatus = System.ServiceProcess.ServiceControllerStatus;

namespace OmniDesk.Agent.Windows;

internal static class AgentUpdater
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(90);

    public static async Task Apply(string receiptPath, string msiPath)
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        if (WindowsIdentity.GetCurrent().User is not { } currentUser || !system.Equals(currentUser))
            throw new UnauthorizedAccessException("Updater must run as LocalSystem.");

        var folder = Path.GetFullPath(AgentIdentity.Folder) + Path.DirectorySeparatorChar;
        receiptPath = Path.GetFullPath(receiptPath); msiPath = Path.GetFullPath(msiPath);
        if (!receiptPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !msiPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Updater paths must remain under the protected data directory.");
        AgentIdentity.AssertTrusted(new DirectoryInfo(AgentIdentity.Folder));
        AgentIdentity.AssertTrusted(new FileInfo(receiptPath)); AgentIdentity.AssertTrusted(new FileInfo(msiPath));

        var receipt = JsonSerializer.Deserialize<UpdateReceipt>(await File.ReadAllTextAsync(receiptPath), Json)
            ?? throw new InvalidDataException("Update receipt is invalid.");
        if (receipt.Kind != "agent.update" || receipt.State != "installing" || receipt.Result is not null ||
            !Version.TryParse(receipt.TargetVersion, out var target) || receipt.MsiSha256 is not { Length: 64 } ||
            !Version.TryParse(receipt.PreviousVersion, out var previousVersion) || receipt.PreviousMsiSha256 is not { Length: 64 } ||
            receipt.PreviousMsiPath is null)
            throw new InvalidDataException("Update receipt is missing a verified rollback package.");

        var msiName = $"agent-update-{receipt.JobId:N}.msi";
        var rollbackName = $"agent-rollback-{receipt.JobId:N}.msi";
        if (!string.Equals(Path.GetFileName(msiPath), msiName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(receipt.PreviousMsiPath), Path.Combine(folder, rollbackName), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MSI path does not match the update job.");
        var rollbackPath = receipt.PreviousMsiPath;
        AgentIdentity.AssertTrusted(new FileInfo(rollbackPath));
        await VerifyHash(msiPath, receipt.MsiSha256, "Staged MSI hash mismatch.");
        await VerifyHash(rollbackPath, receipt.PreviousMsiSha256, "Rollback MSI hash mismatch.");

        var logPath = Path.Combine(AgentIdentity.Folder, $"agent-update-{receipt.JobId:N}.log");
        var installStartedAt = DateTimeOffset.UtcNow;
        int installExit;
        try { installExit = await RunMsi(["/i", msiPath, "/qn", "/norestart", "/l*v", logPath]); }
        catch (TimeoutException)
        {
            var reverted = await WaitHealthy(receipt.PreviousVersion, installStartedAt, HealthTimeout);
            SaveResult(receiptPath, receipt, new(receipt.LeaseId, false, false, [],
                $"Windows Installer ha superato il limite di tempo. Registro: {Path.GetFileName(logPath)}",
                FailureCategory: "msi_install_failed", PreviousVersion: receipt.PreviousVersion, TargetVersion: receipt.TargetVersion,
                InstalledVersion: InstalledVersion(), RollbackState: reverted ? "installer_reverted" : "service_unhealthy"), "installer_failed");
            return;
        }
        if (installExit is not (0 or 3010))
        {
            var reverted = await WaitHealthy(receipt.PreviousVersion, installStartedAt, HealthTimeout);
            SaveResult(receiptPath, receipt, new(receipt.LeaseId, false, false, [],
                $"Windows Installer ha rifiutato l’aggiornamento (codice {installExit}). Registro: {Path.GetFileName(logPath)}",
                FailureCategory: "msi_install_failed", PreviousVersion: receipt.PreviousVersion, TargetVersion: receipt.TargetVersion,
                InstalledVersion: InstalledVersion(), RollbackState: reverted ? "installer_reverted" : "service_unhealthy",
                WindowsInstallerExitCode: installExit), "installer_failed");
            return;
        }

        var installedHealthy = await WaitHealthy(receipt.TargetVersion, installStartedAt, HealthTimeout);
        if (installedHealthy)
        {
            SaveResult(receiptPath, receipt, new(receipt.LeaseId, true, installExit == 3010, [], null,
                PreviousVersion: receipt.PreviousVersion, TargetVersion: receipt.TargetVersion, InstalledVersion: InstalledVersion(),
                RollbackState: "not_needed", WindowsInstallerExitCode: installExit), "installed");
            return;
        }

        var rollbackRemoveLog = Path.Combine(AgentIdentity.Folder, $"agent-rollback-remove-{receipt.JobId:N}.log");
        var rollbackRestoreLog = Path.Combine(AgentIdentity.Folder, $"agent-rollback-restore-{receipt.JobId:N}.log");
        string rollbackState;
        var rollbackSucceeded = false;
        try
        {
            var current = AgentMsiCache.FindInstalledPackage()
                ?? throw new InvalidOperationException("Installed OmniDesk MSI could not be identified for rollback.");
            var uninstallExit = await RunMsi(["/x", current.ProductCode, "/qn", "/norestart", "/l*v", rollbackRemoveLog, "OMNIDESK_ROLLBACK=1"]);
            if (uninstallExit is not (0 or 3010)) throw new InvalidOperationException($"MSI removal returned {uninstallExit}.");
            var rollbackStartedAt = DateTimeOffset.UtcNow;
            var restoreExit = await RunMsi(["/i", rollbackPath, "/qn", "/norestart", "/l*v", rollbackRestoreLog, "OMNIDESK_ROLLBACK=1"]);
            if (restoreExit is not (0 or 3010)) throw new InvalidOperationException($"Known-good MSI restore returned {restoreExit}.");
            rollbackSucceeded = await WaitHealthy(receipt.PreviousVersion, rollbackStartedAt, HealthTimeout);
            rollbackState = rollbackSucceeded ? "succeeded" : "service_unhealthy";
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or TimeoutException)
        { rollbackState = "failed"; }

        SaveResult(receiptPath, receipt, new(receipt.LeaseId, false, installExit == 3010, [],
            rollbackSucceeded
                ? $"L’Agent aggiornato non ha superato il controllo salute. Ripristinata la versione {receipt.PreviousVersion}."
                : $"L’Agent aggiornato non ha superato il controllo salute e il ripristino automatico non è riuscito. Consulta i registri Windows Installer nella cartella ProgramData OmniDesk.",
            FailureCategory: "post_install_health_failed", PreviousVersion: receipt.PreviousVersion, TargetVersion: receipt.TargetVersion,
            InstalledVersion: InstalledVersion(), RollbackState: rollbackState, WindowsInstallerExitCode: installExit), "failed");
    }

    private static async Task<int> RunMsi(IEnumerable<string> arguments)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msiexec.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows Installer could not be started.");
        using var timeout = new CancellationTokenSource(InstallerTimeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Windows Installer exceeded its time limit.");
        }
        return process.ExitCode;
    }

    private static async Task<bool> WaitHealthy(string? expectedVersion, DateTimeOffset heartbeatNotBefore, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var service = new ServiceController("OmniDeskAgent");
                service.Refresh();
                var installed = InstalledVersion();
                if (service.Status == ServiceControllerStatus.Running && Version.TryParse(expectedVersion, out var expected) &&
                    Version.TryParse(installed, out var actual) && actual == expected && AgentIdentity.Load() is { } identity)
                {
                    _ = identity.Assertion(); // Check DPAPI identity availability without sending credentials.
                    var health = AgentUpdateHealth.Read();
                    if (health is not null && Version.TryParse(health.Version, out var heartbeatVersion) && heartbeatVersion == expected && health.HeartbeatAt >= heartbeatNotBefore)
                        return true;
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException or CryptographicException or FormatException or JsonException) { }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return false;
    }

    private static string? InstalledVersion()
    {
        try
        {
            var executable = AgentInstallLayout.ExecutablePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            return File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).ProductVersion?.Split('+')[0] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static async Task VerifyHash(string path, string expected, string message)
    {
        await using var verify = File.OpenRead(path);
        var expectedHash = Convert.FromHexString(expected);
        var actualHash = await SHA256.HashDataAsync(verify);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            throw new InvalidDataException(message);
    }

    private static void SaveResult(string path, UpdateReceipt receipt, UpdateResultRequest result, string state) =>
        UpdateWorker.Save(path, receipt with { State = state, Result = result });
}
