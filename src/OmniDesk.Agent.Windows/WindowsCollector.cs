using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OmniDesk.Contracts;

namespace OmniDesk.Agent.Windows;
public sealed class WindowsCollector
{
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
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try { var stats = adapter.GetIPStatistics(); received += stats.BytesReceived; sent += stats.BytesSent; }
            catch (NetworkInformationException) { /* Adapter removed during snapshot. */ }
        }
        var disks = new List<DiskMetric>();
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
        {
            try { if (drive.IsReady && drive.TotalSize > 0) disks.Add(new(drive.Name, drive.TotalSize, drive.TotalFreeSpace)); }
            catch (IOException) { /* Offline drive is omitted, not represented as zero. */ }
        }
        return new(cpu, checked((long)memory.TotalPhysical), checked((long)memory.AvailablePhysical), received, sent,
            checked((long)(GetTickCount64() / 1000)), disks.ToArray());
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
            bios["SerialNumber"], processor["Name"], Environment.ProcessorCount, distinct.Take(2000).ToArray(), warnings.ToArray());
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
