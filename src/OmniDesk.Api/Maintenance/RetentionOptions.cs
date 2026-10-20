namespace OmniDesk.Api.Maintenance;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";
    public int DeliveredOutboxDays { get; set; } = 180;
    public int ProcessedEmailIntakesDays { get; set; } = 180;
    public int PendingEmailIntakesDays { get; set; } = 30;
    public int ResolvedTicketAttachmentsDays { get; set; }
    public int ResolvedTicketContentDays { get; set; }
    public int AuditEventsDays { get; set; }
}
