namespace OmniDesk.Contracts;

public sealed record SupportCreate(Guid RequestId, string Subject, string Description, string? ScreenshotBase64, bool ScreenshotConsent);
public sealed record SupportIntake(string RequesterKey, SupportCreate Ticket);
public sealed record SupportTicketView(Guid Id, long Number, Guid DeviceId, string Subject, string Description, string State,
    string? Resolution, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool HasScreenshot);
public sealed record SupportEdit(long ExpectedVersion, string State, string? Resolution);
public sealed record TraySupportResponse(int Version, Guid RequestId, string? Error, SupportTicketView[] Tickets);
