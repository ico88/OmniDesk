using System.Security.Cryptography;
using Microsoft.Win32;

namespace OmniDesk.Agent.Windows;

internal sealed record RollbackMsi(string Version, string Path, string Sha256);
internal sealed class RollbackPackageUnavailableException : IOException
{
    internal RollbackPackageUnavailableException(string message) : base(message) { }
}

internal static class AgentMsiCache
{
    internal static async Task<RollbackMsi> CreateRollbackCopy(Guid jobId, string installedVersion, CancellationToken ct)
    {
        var package = FindInstalledPackage();
        if (package is null || !Version.TryParse(package.Version, out var registered) ||
            !Version.TryParse(installedVersion, out var running) || registered != running)
            throw new RollbackPackageUnavailableException("Rollback package for the installed Agent is unavailable.");

        var destination = Path.Combine(AgentIdentity.Folder, $"agent-rollback-{jobId:N}.msi");
        File.Copy(package.LocalPackage, destination, overwrite: false);
        AgentIdentity.ProtectTrustedFile(destination);
        AgentIdentity.AssertTrusted(new FileInfo(destination));
        await using var input = File.OpenRead(destination);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
        return new(package.Version, destination, digest);
    }

    internal static InstalledAgentPackage? FindInstalledPackage()
    {
        var windowsInstaller = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var uninstall = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: false);
        if (uninstall is null) return null;

        foreach (var productCode in uninstall.GetSubKeyNames())
        {
            using var product = uninstall.OpenSubKey(productCode, writable: false);
            if (product is null || !string.Equals(product.GetValue("DisplayName") as string, "OmniDesk Agent", StringComparison.Ordinal)) continue;
            if (product.GetValue("WindowsInstaller") is not int { } installedByMsi || installedByMsi != 1) continue;
            if (!Guid.TryParse(productCode.Trim('{', '}'), out _)) continue;
            var version = product.GetValue("DisplayVersion") as string;
            var localPackage = product.GetValue("LocalPackage") as string;
            if (!Version.TryParse(version, out _) || string.IsNullOrWhiteSpace(localPackage)) continue;
            var fullPath = Path.GetFullPath(localPackage);
            if (!fullPath.StartsWith(windowsInstaller, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(fullPath), ".msi", StringComparison.OrdinalIgnoreCase)) continue;
            var file = new FileInfo(fullPath);
            if (!file.Exists || file.Length is < 1 or > 268435456 || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            return new(productCode, version, fullPath);
        }
        return null;
    }

    internal sealed record InstalledAgentPackage(string ProductCode, string Version, string LocalPackage);
}
