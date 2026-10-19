using System.Management;
using OmniDesk.Contracts;

namespace OmniDesk.Agent.Windows;

internal static class DiagnosticCollector
{
    public static DiagnosticResult Collect(string kind)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var now = DateTimeOffset.UtcNow;
        return kind switch
        {
            "diagnostics.processes" => new(kind, now, Processes()),
            "diagnostics.services" => new(kind, now, Services: Services()),
            "diagnostics.events" => new(kind, now, Events: Events()),
            _ => throw new InvalidOperationException("Unsupported diagnostic kind")
        };
    }

    private static ProcessDiagnostic[] Processes()
    {
        using var search = new ManagementObjectSearcher("SELECT ProcessId, Name, CreationDate FROM Win32_Process");
        search.Options.Timeout = TimeSpan.FromSeconds(12);
        using var rows = search.Get();
        return rows.Cast<ManagementObject>().Take(1000).Select(x =>
            {
                var processId = Convert.ToInt32(x["ProcessId"], System.Globalization.CultureInfo.InvariantCulture);
                var rawName = (string?)x["Name"] ?? "(sconosciuto)";
                var name = rawName[..Math.Min(rawName.Length, 260)];
                DateTimeOffset startedAt = default;
                try { if (x["CreationDate"] is string created) startedAt = ManagementDateTimeConverter.ToDateTime(created).ToUniversalTime(); }
                catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException) { }
                return new ProcessDiagnostic(processId, name, startedAt, ProcessControlPolicy.IsControllable(processId, name, startedAt));
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
    }

    private static ServiceDiagnostic[] Services()
    {
        using var search = new ManagementObjectSearcher("SELECT Name, DisplayName, State, StartMode FROM Win32_Service");
        search.Options.Timeout = TimeSpan.FromSeconds(12);
        using var rows = search.Get();
        return rows.Cast<ManagementObject>().Take(1000).Select(x => new ServiceDiagnostic(
                Clip((string?)x["Name"], 256), Clip((string?)x["DisplayName"], 256),
                Clip((string?)x["State"], 32), Clip((string?)x["StartMode"], 32)))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
    }

    private static EventDiagnostic[] Events()
    {
        using var search = new ManagementObjectSearcher("SELECT Logfile, TimeGenerated, EventCode, Type, SourceName, Message FROM Win32_NTLogEvent WHERE (Logfile='System' OR Logfile='Application') AND (Type='Error' OR Type='Warning')");
        search.Options.Timeout = TimeSpan.FromSeconds(15);
        using var rows = search.Get();
        var bounded = new List<EventDiagnostic>(500);
        foreach (ManagementObject x in rows)
        {
            if (bounded.Count == 500) break;
            var time = ManagementDateTimeConverter.ToDateTime((string)x["TimeGenerated"]!);
            bounded.Add(new EventDiagnostic(Clip((string?)x["Logfile"], 32), time,
                Convert.ToInt32(x["EventCode"], System.Globalization.CultureInfo.InvariantCulture),
                Clip((string?)x["Type"], 32), Clip((string?)x["SourceName"], 256), Clip((string?)x["Message"], 1000)));
        }
        return bounded.OrderByDescending(x => x.TimeCreated).Take(100).ToArray();
    }

    private static string Clip(string? value, int max) => string.IsNullOrWhiteSpace(value) ? "—" : value[..Math.Min(value.Length, max)];
}
