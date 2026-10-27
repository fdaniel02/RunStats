using System.Globalization;

namespace RunStats.Features.Dashboard;

public record ActivityWeek(string? MonthLabel, IReadOnlyList<DateOnly> Days);

// Columns are weeks (Mon-Sun); the last column is the current week. The same grid as WorkoutTracker's.
public class ActivityGrid
{
    public DateOnly Start { get; }
    public IReadOnlyList<ActivityWeek> Weeks { get; }

    public ActivityGrid(DateOnly today, int weekCount)
    {
        var currentMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        Start = currentMonday.AddDays(-7 * (weekCount - 1));

        var weeks = Enumerable.Range(0, weekCount)
            .Select(w => Enumerable.Range(7 * w, 7).Select(Start.AddDays).ToList())
            .ToList();
        Weeks = weeks.Select((days, i) => new ActivityWeek(MonthLabel(weeks, i), days)).ToList();
    }

    static string? MonthLabel(List<List<DateOnly>> weeks, int index)
    {
        var days = weeks[index];
        if (days.Any(IsFirstOfMonth))
            return Format(days.First(IsFirstOfMonth));

        // The grid's start gets a label too, unless the next column's label would sit right next to it and overlap.
        var nextColumnStartsMonth = index + 1 < weeks.Count && weeks[index + 1].Any(IsFirstOfMonth);
        return index == 0 && !nextColumnStartsMonth ? Format(days[0]) : null;
    }

    static bool IsFirstOfMonth(DateOnly day) => day.Day == 1;

    static string Format(DateOnly day) => day.ToString("MMM", CultureInfo.InvariantCulture);
}
