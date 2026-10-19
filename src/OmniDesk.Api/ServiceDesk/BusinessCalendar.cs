namespace OmniDesk.Api.ServiceDesk;

public sealed record BusinessTimeRange(TimeOnly Start, TimeOnly End);

public sealed record BusinessCalendarDefinition(
    string TimeZoneId,
    IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessTimeRange>> WeeklyHours,
    IReadOnlySet<DateOnly> Holidays);

/// <summary>Calculates business-time deadlines from immutable calendar data.</summary>
public static class BusinessCalendar
{
    private const int MaximumSearchDays = 36600;

    public static DateTimeOffset AddWorkingTime(
        DateTimeOffset start,
        TimeSpan workingTime,
        BusinessCalendarDefinition calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (workingTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(workingTime));
        if (string.IsNullOrWhiteSpace(calendar.TimeZoneId)) throw new ArgumentException("A time-zone ID is required.", nameof(calendar));
        ArgumentNullException.ThrowIfNull(calendar.WeeklyHours);
        ArgumentNullException.ThrowIfNull(calendar.Holidays);
        ValidateDefinition(calendar);
        if (workingTime == TimeSpan.Zero) return start.ToUniversalTime();

        var zone = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId);
        var cursor = start.ToUniversalTime();
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(cursor, zone).DateTime);

        for (var day = 0; day < MaximumSearchDays; day++)
        {
            if (!calendar.Holidays.Contains(localDate) && calendar.WeeklyHours.TryGetValue(localDate.DayOfWeek, out var ranges))
            {
                foreach (var range in ranges)
                {
                    var rangeStart = ToUtc(localDate, range.Start, zone, isEnd: false);
                    var rangeEnd = ToUtc(localDate, range.End, zone, isEnd: true);
                    if (rangeEnd <= cursor || rangeEnd <= rangeStart) continue;

                    var effectiveStart = rangeStart > cursor ? rangeStart : cursor;
                    var available = rangeEnd - effectiveStart;
                    if (workingTime <= available) return (effectiveStart + workingTime).ToUniversalTime();
                    workingTime -= available;
                    cursor = rangeEnd;
                }
            }

            if (localDate == DateOnly.MaxValue) break;
            localDate = localDate.AddDays(1);
        }

        throw new InvalidOperationException("The calendar has no reachable working time within the supported horizon.");
    }

    public static void ValidateDefinition(BusinessCalendarDefinition calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (string.IsNullOrWhiteSpace(calendar.TimeZoneId)) throw new ArgumentException("A time-zone ID is required.", nameof(calendar));
        ArgumentNullException.ThrowIfNull(calendar.WeeklyHours);
        ArgumentNullException.ThrowIfNull(calendar.Holidays);
        _ = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId);
        Validate(calendar.WeeklyHours);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone, bool isEnd)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // A nonexistent DST wall time advances to the first valid minute.
        for (var minute = 0; zone.IsInvalidTime(local); minute++)
        {
            if (minute >= 180) throw new InvalidOperationException("The time-zone gap exceeds the supported adjustment.");
            local = local.AddMinutes(1);
        }

        TimeSpan offset;
        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            // On a repeated fall-back hour, open at the first occurrence and close at the last.
            offset = isEnd ? offsets.Min() : offsets.Max();
        }
        else
        {
            offset = zone.GetUtcOffset(local);
        }
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static void Validate(IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessTimeRange>> weeklyHours)
    {
        foreach (var (day, ranges) in weeklyHours)
        {
            if (!Enum.IsDefined(day)) throw new ArgumentOutOfRangeException(nameof(weeklyHours), "Unknown weekday.");
            ArgumentNullException.ThrowIfNull(ranges);
            TimeOnly? priorEnd = null;
            foreach (var range in ranges)
            {
                ArgumentNullException.ThrowIfNull(range);
                if (range.Start >= range.End || priorEnd is not null && range.Start < priorEnd)
                    throw new ArgumentException("Working ranges must be positive, ordered, non-overlapping intervals within one day.", nameof(weeklyHours));
                priorEnd = range.End;
            }
        }
    }
}
