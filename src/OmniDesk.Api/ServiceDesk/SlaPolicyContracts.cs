namespace OmniDesk.Api.ServiceDesk;

public sealed record SlaCalendarInput(string TimeZoneId, Dictionary<DayOfWeek, BusinessTimeRange[]> WeeklyHours, DateOnly[] Holidays);
public sealed record SlaPolicyDefinition(int FirstResponseMinutes, int ResolutionMinutes, SlaCalendarInput Calendar)
{
    public string[] PauseStates { get; init; } = [];
}
public sealed record SlaPolicyCreate(string Name, Guid? SiteId, SlaPolicyDefinition Definition);
public sealed record SlaPolicyVersionCreate(long ExpectedVersion, string Name, SlaPolicyDefinition Definition);
