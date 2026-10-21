namespace OmniDesk.Contracts;
public sealed record EnrollmentRequest(string Token, string Name, string PublicKeyPem);
public sealed record EnrollmentResponse(Guid DeviceId);
public sealed record DiskMetric(string Name, long TotalBytes, long FreeBytes);
public sealed record MetricsRequest(double? CpuPercent, long TotalMemoryBytes, long AvailableMemoryBytes,
    long NetworkReceivedBytes, long NetworkSentBytes, long UptimeSeconds, DiskMetric[] Disks, string? AgentVersion = null,
    NetworkAdapterItem[]? NetworkAdapters = null, SmartDiskItem[]? SmartDisks = null);
public sealed record SmartDiskItem(string Name, string Status);
public sealed record SoftwareItem(string Name, string? Version, string? Publisher);
public sealed record InventoryRequest(string OperatingSystem, string Architecture, string? Manufacturer,
    string? Model, string? SerialNumber, string? Processor, int LogicalProcessors, SoftwareItem[] Software, string[] Warnings,
    DefenderSnapshotRequest? Defender = null);
public sealed record NetworkAdapterItem(string Name, string Type, string MacAddress, string[] IpAddresses);
public sealed record DefenderExclusionItem(string Kind, string Value);
public sealed record DefenderSnapshotRequest(DateTimeOffset ObservedAt, string ProviderState,
    bool? AntivirusEnabled, bool? RealTimeProtectionEnabled, bool? TamperProtection,
    string? EngineVersion, string? SignatureVersion, DateTimeOffset? SignatureUpdatedAt,
    DateTimeOffset? QuickScanStartTime, DateTimeOffset? FullScanStartTime,
    string ManagementSource, bool ExclusionsAvailable, bool ExclusionsTruncated,
    DefenderExclusionItem[]? Exclusions);
