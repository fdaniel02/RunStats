using System.Globalization;

namespace RunStats.Features.Dashboard;

public record ActivityWeek(string? MonthLabel, IReadOnlyList<DateOnly> Days);

// Columns are weeks (Mon-Sun). The same grid as WorkoutTracker's. Days outside First to Last (the year's, when the
// first and last week reach into the years next to it) are part of a column but not of the grid's period.
public class ActivityGrid
{
    public DateOnly Start { get; }
    public DateOnly First { get; }
    public DateOnly Last { get; }
    // For screen readers: "2 days with a run in the last 16 weeks".
    public string Period { get; }
    public IReadOnlyList<ActivityWeek> Weeks { get; }

    // The last column is the current week.
    public ActivityGrid(DateOnly today, int weekCount)
        : this(Monday(today).AddDays(-7 * (weekCount - 1)), weekCount, null, null, $"the last {weekCount} weeks")
    {
    }

    ActivityGrid(DateOnly start, int weekCount, DateOnly? first, DateOnly? last, string period)
    {
        Start = start;
        First = first ?? start;
        Last = last ?? start.AddDays(7 * weekCount - 1);
        Period = period;

        var weeks = Enumerable.Range(0, weekCount)
            .Select(w => Enumerable.Range(7 * w, 7).Select(Start.AddDays).ToList())
            .ToList();
        Weeks = weeks.Select((days, i) => new ActivityWeek(MonthLabel(weeks, i), days)).ToList();
    }

    // From the week of 1 January to the week of 31 December.
    public static ActivityGrid ForYear(int year)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var start = Monday(first);
        return new ActivityGrid(start, (Monday(last).DayNumber - start.DayNumber) / 7 + 1, first, last, year.ToString(CultureInfo.InvariantCulture));
    }

    public bool Contains(DateOnly day) => day >= First && day <= Last;

    string? MonthLabel(List<List<DateOnly>> weeks, int index)
    {
        var days = weeks[index];
        if (days.Any(StartsMonth))
            return Format(days.First(StartsMonth));

        // The grid's start gets a label too, unless the next column's label would sit right next to it and overlap.
        var nextColumnStartsMonth = index + 1 < weeks.Count && weeks[index + 1].Any(StartsMonth);
        return index == 0 && !nextColumnStartsMonth ? Format(days[0]) : null;
    }

    bool StartsMonth(DateOnly day) => day.Day == 1 && Contains(day);

    static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    static string Format(DateOnly day) => day.ToString("MMM", CultureInfo.InvariantCulture);
}
