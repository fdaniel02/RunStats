namespace RunStats.Data;

// Converts to calendar dates in the user's time zone, independent of the server's (UTC in Docker).
public class Clock(TimeZoneInfo timeZone, TimeProvider time)
{
    public DateOnly Today => ToLocalDate(time.GetUtcNow().UtcDateTime);

    public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocalTime(utc));

    public DateTime ToLocalTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);

    public DateTime StartOfDayUtc(DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        // Where daylight saving starts at midnight (e.g. Chile), 00:00 doesn't exist and the day starts later.
        while (timeZone.IsInvalidTime(start))
            start = start.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(start, timeZone);
    }
}
