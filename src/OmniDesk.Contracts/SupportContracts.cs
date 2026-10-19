namespace OmniDesk.Contracts;

public sealed record SupportCreate(Guid RequestId, string Subject, string Description, string? ScreenshotBase64, bool ScreenshotConsent, string Category = "general");
public sealed record SupportIntake(string RequesterKey, SupportCreate Ticket);
public sealed record SupportRequesterReply(string RequesterKey, long ExpectedVersion, string Body);
public sealed record SupportRequesterMessageView(long Id, string Kind, string Body, string? FromState, string? ToState, DateTimeOffset CreatedAt);
public sealed record SupportRequesterConversation(Guid TicketId, long Version, string State, SupportRequesterMessageView[] Messages);
public sealed record SupportTicketView(Guid Id, long Number, Guid DeviceId, string Subject, string Description, string State,
    string? Resolution, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool HasScreenshot, string Category = "general", string Origin = "device");
public sealed record SupportEdit(long ExpectedVersion, string State, string? Resolution);
public sealed record TraySupportResponse(int Version, Guid RequestId, string? Error, SupportTicketView[] Tickets,
    SupportRequesterConversation? Conversation = null);
