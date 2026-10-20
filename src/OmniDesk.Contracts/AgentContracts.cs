namespace OmniDesk.Contracts;
public sealed record EnrollmentRequest(string Token, string Name, string PublicKeyPem);
public sealed record EnrollmentResponse(Guid DeviceId);
public sealed record DiskMetric(string Name, long TotalBytes, long FreeBytes);
public sealed record MetricsRequest(double? CpuPercent, long TotalMemoryBytes, long AvailableMemoryBytes,
    long NetworkReceivedBytes, long NetworkSentBytes, long UptimeSeconds, DiskMetric[] Disks, string? AgentVersion = null,
    NetworkAdapterItem[]? NetworkAdapters = null);
public sealed record SoftwareItem(string Name, string? Version, string? Publisher);
public sealed record InventoryRequest(string OperatingSystem, string Architecture, string? Manufacturer,
    string? Model, string? SerialNumber, string? Processor, int LogicalProcessors, SoftwareItem[] Software, string[] Warnings);
public sealed record NetworkAdapterItem(string Name, string Type, string MacAddress, string[] IpAddresses);
