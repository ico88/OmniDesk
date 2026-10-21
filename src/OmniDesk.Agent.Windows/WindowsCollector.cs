using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using OmniDesk.Contracts;

namespace OmniDesk.Agent.Windows;
public sealed class WindowsCollector
{
    private static readonly JsonSerializerOptions DefenderJsonOptions = new(JsonSerializerDefaults.Web);
    private const string DefenderReadOnlyScript = """
        $ErrorActionPreference = 'Stop'
        function Get-SafeDate($value) {
          if ($null -eq $value) { return $null }
          try { $date = [datetime]$value; if ($date.Year -lt 1971 -or $date -gt [datetime]::UtcNow.AddMinutes(10)) { return $null }; return $date.ToUniversalTime().ToString('o') } catch { return $null }
        }
        $statusCommand = Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue
        if ($null -eq $statusCommand) {
          [pscustomobject]@{ observedAt = [DateTimeOffset]::UtcNow.ToString('o'); providerState = 'unsupported'; managementSource = 'unknown'; exclusionsAvailable = $false; exclusionsTruncated = $false; exclusions = $null } | ConvertTo-Json -Compress
          exit 0
        }
        try {
          $s = Get-MpComputerStatus
          $preference = $null
          $exclusionsAvailable = $false
          try { $preference = Get-MpPreference; $exclusionsAvailable = $true } catch { }
          $items = [System.Collections.Generic.List[object]]::new()
          $truncated = $false
          if ($exclusionsAvailable) {
            foreach ($value in @($preference.ExclusionPath)) {
              if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { continue }
              if ($items.Count -ge 256 -or ([string]$value).Length -gt 512) { $truncated = $true; continue }
              $items.Add([pscustomobject]@{ kind = 'path'; value = [string]$value })
            }
            foreach ($value in @($preference.ExclusionProcess)) {
              if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { continue }
              if ($items.Count -ge 256 -or ([string]$value).Length -gt 512) { $truncated = $true; continue }
              $items.Add([pscustomobject]@{ kind = 'process'; value = [string]$value })
            }
            foreach ($value in @($preference.ExclusionExtension)) {
              if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { continue }
              if ($items.Count -ge 256 -or ([string]$value).Length -gt 512) { $truncated = $true; continue }
              $items.Add([pscustomobject]@{ kind = 'extension'; value = [string]$value })
            }
          }
          [pscustomobject]@{
            observedAt = [DateTimeOffset]::UtcNow.ToString('o'); providerState = 'available'
            antivirusEnabled = if ($null -eq $s.AntivirusEnabled) { $null } else { [bool]$s.AntivirusEnabled }
            realTimeProtectionEnabled = if ($null -eq $s.RealTimeProtectionEnabled) { $null } else { [bool]$s.RealTimeProtectionEnabled }
            tamperProtection = if ($null -eq $s.IsTamperProtected) { $null } else { [bool]$s.IsTamperProtected }
            engineVersion = [string]$s.AMEngineVersion; signatureVersion = [string]$s.AntivirusSignatureVersion
            signatureUpdatedAt = Get-SafeDate $s.AntivirusSignatureLastUpdated
            quickScanStartTime = Get-SafeDate $s.QuickScanStartTime; fullScanStartTime = Get-SafeDate $s.FullScanStartTime
            managementSource = 'unknown'; exclusionsAvailable = $exclusionsAvailable; exclusionsTruncated = $truncated
            exclusions = if ($exclusionsAvailable) { [object[]]$items.ToArray() } else { $null }
          } | ConvertTo-Json -Compress -Depth 4
        } catch {
          [pscustomobject]@{ observedAt = [DateTimeOffset]::UtcNow.ToString('o'); providerState = 'error'; managementSource = 'unknown'; exclusionsAvailable = $false; exclusionsTruncated = $false; exclusions = $null } | ConvertTo-Json -Compress
        }
        """;
    private (ulong Idle, ulong Kernel, ulong User)? previous;
    public MetricsRequest Metrics()
    {
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (previous is { } p)
            {
                var total = kernel - p.Kernel + user - p.User;
                if (total > 0) cpu = Math.Clamp(100.0 * (1.0 - (double)(idle - p.Idle) / total), 0, 100);
            }
            previous = (idle, kernel, user);
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new InvalidOperationException("Cannot read physical memory metrics.");
        long received = 0, sent = 0;
        var networkAdapters = new List<NetworkAdapterItem>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var properties = adapter.GetIPProperties();
                var unicastAddresses = properties.UnicastAddresses
                    .Select(x => x.Address)
                    .Where(x => !System.Net.IPAddress.IsLoopback(x) && !x.Equals(System.Net.IPAddress.Any) && !x.Equals(System.Net.IPAddress.IPv6Any))
                    .ToArray();
                var gateways = properties.GatewayAddresses.Select(x => x.Address).ToArray();
                if (!NetworkAdapterSelection.IsActiveConnection(adapter.OperationalStatus, adapter.NetworkInterfaceType, unicastAddresses, gateways)) continue;
                var stats = adapter.GetIPStatistics(); received += stats.BytesReceived; sent += stats.BytesSent;
                var addresses = unicastAddresses
                    .Select(x => x.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(32)
                    .ToArray();
                networkAdapters.Add(new(Limit(adapter.Name, 256)!, adapter.NetworkInterfaceType.ToString(), adapter.GetPhysicalAddress().ToString(), addresses));
                if (networkAdapters.Count == 32) break;
            }
            catch (NetworkInformationException) { /* Adapter removed during snapshot. */ }
        }
        var disks = new List<DiskMetric>();
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
        {
            try { if (drive.IsReady && drive.TotalSize > 0) disks.Add(new(drive.Name, drive.TotalSize, drive.TotalFreeSpace)); }
            catch (IOException) { /* Offline drive is omitted, not represented as zero. */ }
        }
        return new(cpu, checked((long)memory.TotalPhysical), checked((long)memory.AvailablePhysical), received, sent,
            checked((long)(GetTickCount64() / 1000)), disks.ToArray(), typeof(WindowsCollector).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0], networkAdapters.ToArray(), CollectSmartStatus());
    }
    public InventoryRequest Inventory()
    {
        var warnings = new List<string>();
        Dictionary<string, string?> Query(string className, params string[] fields)
        {
            var result = fields.ToDictionary(x => x, _ => (string?)null);
            try
            {
                using var search = new ManagementObjectSearcher($"SELECT {string.Join(',', fields)} FROM {className}");
                using var collection = search.Get();
                foreach (ManagementObject item in collection)
                {
                    using (item) foreach (var field in fields) result[field] = Limit(item[field]?.ToString(), 500);
                    break;
                }
            }
            catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException) { warnings.Add($"{className}: unavailable"); }
            return result;
        }
        var hardware = Query("Win32_ComputerSystem", "Manufacturer", "Model");
        var bios = Query("Win32_BIOS", "SerialNumber"); var processor = Query("Win32_Processor", "Name");
        var software = new List<SoftwareItem>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is not string display || string.IsNullOrWhiteSpace(display)) continue;
                    software.Add(new(Limit(display, 500)!, Limit(entry.GetValue("DisplayVersion")?.ToString(), 200), Limit(entry.GetValue("Publisher")?.ToString(), 500)));
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { warnings.Add($"Software registry {view}: unavailable"); }
        }
        var distinct = software.Distinct().OrderBy(x => x.Name).ToArray();
        if (distinct.Length > 2000) warnings.Add("Software inventory truncated to 2000 entries");
        return new(RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(), hardware["Manufacturer"], hardware["Model"],
            NormalizeSerial(bios["SerialNumber"]), processor["Name"], Environment.ProcessorCount, distinct.Take(2000).ToArray(), warnings.ToArray(), DefenderSnapshot());
    }
    private static DefenderSnapshotRequest DefenderSnapshot()
    {
        DefenderSnapshotRequest Unavailable(string state) => new(DateTimeOffset.UtcNow, state, null, null, null, null, null, null, null, null,
            "unknown", false, false, null);
        try
        {
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var executable = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("RemoteSigned");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(DefenderReadOnlyScript);
            using var process = Process.Start(start);
            if (process is null) return Unavailable("unsupported");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return Unavailable("error");
            }
            var output = outputTask.GetAwaiter().GetResult();
            _ = errorTask.GetAwaiter().GetResult();
            if (output.Length > 256 * 1024) return Unavailable("error");
            return JsonSerializer.Deserialize<DefenderSnapshotRequest>(output, DefenderJsonOptions) ?? Unavailable("error");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or
            OperationCanceledException or JsonException or InvalidOperationException or ArgumentException)
        {
            return Unavailable(exception is System.ComponentModel.Win32Exception ? "unsupported" : "error");
        }
    }
    private static SmartDiskItem[]? CollectSmartStatus()
    {
        var models = new List<(string DeviceId, string Model)>();
        try
        {
            using var disks = new ManagementObjectSearcher("SELECT PNPDeviceID, Model FROM Win32_DiskDrive");
            using var rows = disks.Get();
            foreach (ManagementObject disk in rows)
            {
                using (disk)
                {
                    var deviceId = disk["PNPDeviceID"]?.ToString();
                    var model = disk["Model"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(deviceId) && !string.IsNullOrWhiteSpace(model)) models.Add((deviceId, Limit(model.Trim(), 256)!));
                }
            }
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException) { /* SMART provider may be unavailable. */ }

        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            using var search = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT InstanceName, Active, PredictFailure FROM MSStorageDriver_FailurePredictStatus"));
            using var rows = search.Get();
            var result = new List<SmartDiskItem>();
            foreach (ManagementObject item in rows)
            {
                using (item)
                {
                    var instance = item["InstanceName"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(instance)) continue;
                    var normalizedInstance = NormalizeDeviceId(instance);
                    var model = models.FirstOrDefault(disk => NormalizeDeviceId(disk.DeviceId).Equals(normalizedInstance, StringComparison.OrdinalIgnoreCase)).Model;
                    var name = string.IsNullOrWhiteSpace(model) ? SmartInstanceLabel(instance) : model;
                    var active = bool.TryParse(item["Active"]?.ToString(), out var enabled) && enabled;
                    var predictsFailure = bool.TryParse(item["PredictFailure"]?.ToString(), out var failure) && failure;
                    result.Add(new(Limit(name, 256)!, !active ? "unavailable" : predictsFailure ? "predicted_failure" : "no_failure_predicted"));
                    if (result.Count == 64) break;
                }
            }
            return result.Count == 0 ? null : result.ToArray();
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException) { return null; }
    }
    private static string NormalizeDeviceId(string value)
    {
        var normalized = value.Trim().TrimEnd('\\');
        var suffix = normalized.LastIndexOf('_');
        if (suffix >= 0 && normalized[(suffix + 1)..].All(char.IsDigit)) normalized = normalized[..suffix];
        return normalized;
    }
    private static string SmartInstanceLabel(string instance)
    {
        var segment = instance.Split('\\', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? "Disco fisico";
        return segment.Replace("Disk&Ven_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("&Prod_", " ", StringComparison.OrdinalIgnoreCase).Replace('_', ' ').Trim();
    }
    private static string? NormalizeSerial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var serial = value.Trim();
        return new[] { "To Be Filled By O.E.M.", "System Serial Number", "Default string", "Unknown", "None", "Not Specified" }
            .Contains(serial, StringComparer.OrdinalIgnoreCase) ? null : Limit(serial, 500);
    }
    private static string? Limit(string? s, int length) => s is null ? null : s[..Math.Min(s.Length, length)];
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
}
