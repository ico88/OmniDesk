namespace OmniDesk.Contracts;
public sealed record UpdateSelection(Guid UpdateId, int Revision);
public sealed record UpdateJobRequest(Guid RequestId, UpdateSelection[]? Updates = null);
public sealed record AgentUpdatePackage(string Version, string Sha256, long Size);
public sealed record AgentUpdateRequest(Guid RequestId);
public sealed record UpdateLease(Guid Id, Guid LeaseId, string Kind, UpdateSelection[] Updates, DateTimeOffset LeaseExpiresAt, AgentUpdatePackage? AgentUpdate = null);
public sealed record UpdateItem(Guid UpdateId, int Revision, string Title, string[] Kb, bool Downloaded, int? ResultCode = null);
public sealed record UpdateResultRequest(Guid LeaseId, bool Succeeded, bool RebootRequired, UpdateItem[] Updates, string? Error = null);
