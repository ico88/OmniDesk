namespace OmniDesk.Api.Maintenance;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";
    public int DeliveredOutboxDays { get; set; } = 180;
}
