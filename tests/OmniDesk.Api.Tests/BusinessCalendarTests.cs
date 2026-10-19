using OmniDesk.Api.ServiceDesk;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class BusinessCalendarTests
{
    [Fact]
    public void Adds_working_time_across_weekend_and_holiday()
    {
        var calendar = WeekdayCalendar("UTC", holidays: new HashSet<DateOnly> { new(2026, 1, 19) });
        var deadline = BusinessCalendar.AddWorkingTime(
            new DateTimeOffset(2026, 1, 16, 16, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(2), calendar);

        Assert.Equal(new DateTimeOffset(2026, 1, 20, 10, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public void Advances_invalid_spring_forward_wall_time_to_first_valid_minute()
    {
        var sunday = new Dictionary<DayOfWeek, IReadOnlyList<BusinessTimeRange>>
        {
            [DayOfWeek.Sunday] = [new(new TimeOnly(2, 0), new TimeOnly(4, 0))]
        };
        var calendar = new BusinessCalendarDefinition("America/New_York", sunday, new HashSet<DateOnly>());

        var deadline = BusinessCalendar.AddWorkingTime(
            new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1), calendar);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public void Counts_repeated_fall_back_business_interval_using_both_occurrences()
    {
        var sunday = new Dictionary<DayOfWeek, IReadOnlyList<BusinessTimeRange>>
        {
            [DayOfWeek.Sunday] = [new(new TimeOnly(1, 0), new TimeOnly(2, 0))]
        };
        var calendar = new BusinessCalendarDefinition("America/New_York", sunday, new HashSet<DateOnly>());

        var deadline = BusinessCalendar.AddWorkingTime(
            new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(2), calendar);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public void Rejects_overlapping_ranges()
    {
        var ranges = new Dictionary<DayOfWeek, IReadOnlyList<BusinessTimeRange>>
        {
            [DayOfWeek.Monday] =
            [
                new(new TimeOnly(9, 0), new TimeOnly(12, 0)),
                new(new TimeOnly(11, 0), new TimeOnly(13, 0))
            ]
        };
        var calendar = new BusinessCalendarDefinition("UTC", ranges, new HashSet<DateOnly>());

        Assert.Throws<ArgumentException>(() => BusinessCalendar.AddWorkingTime(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1), calendar));
    }

    private static BusinessCalendarDefinition WeekdayCalendar(string timeZoneId, IReadOnlySet<DateOnly>? holidays = null)
    {
        var hours = new Dictionary<DayOfWeek, IReadOnlyList<BusinessTimeRange>>();
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            hours[day] = [new(new TimeOnly(9, 0), new TimeOnly(17, 0))];
        return new BusinessCalendarDefinition(timeZoneId, hours, holidays ?? new HashSet<DateOnly>());
    }
}
