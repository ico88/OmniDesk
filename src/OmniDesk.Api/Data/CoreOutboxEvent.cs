using System.Diagnostics;
using System.Text.Json;

namespace OmniDesk.Api.Data;

public sealed class CoreOutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = "";
    public short SchemaVersion { get; set; } = 1;
    public string AggregateType { get; set; } = "";
    public Guid AggregateId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public static class CoreOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Add(AppDbContext db, string eventType, string aggregateType, Guid aggregateId,
        object payload, string? correlationId = null)
    {
        if (eventType.Length is < 1 or > 100 || aggregateType.Length is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(eventType));

        db.CoreOutboxEvents.Add(new CoreOutboxEvent
        {
            EventType = eventType,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
                : correlationId.Length <= 64 ? correlationId : correlationId[..64],
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        });
    }
}
