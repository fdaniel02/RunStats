using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Features.Dashboard;
using RunStats.Shared;

namespace RunStats.Features.Years;

public sealed record MonthTotal(int Month, double DistanceMeters, double DurationSeconds, bool IsCurrent);

// Years are the ones that can be shown: those with runs, and this year even before its first run; newest first. Weeks
// are the activity grid's columns, with only the year's own days counted in the first and last. Runs are newest first.
public sealed record YearData(
    int Year, IReadOnlyList<int> Years, Totals Totals, double? GoalMeters, IReadOnlyList<WeekTotal> Weeks, IReadOnlyList<MonthTotal> Months,
    IReadOnlyList<RunDay> RunDays, IReadOnlyList<RecordRow> Records, IReadOnlyList<RunRow> Races, IReadOnlyList<RunRow> Runs);

public interface IYearQueries
{
    // Null for a year that can't be shown.
    Task<YearData?> LoadAsync(int year, CancellationToken cancellationToken);
}

// Everything goes by the run's own (Garmin local) date, the date the run tables show, so the totals add up to the runs
// listed.
public class YearQueries(IDbContextFactory<AppDbContext> dbFactory, Clock clock) : IYearQueries
{
    public async Task<YearData?> LoadAsync(int year, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var years = await db.Runs.Select(r => r.StartLocal.Year).Distinct().ToListAsync(cancellationToken);
        if (!years.Contains(today.Year))
            years.Add(today.Year);
        if (!years.Contains(year))
            return null;

        // Unspecified like StartLocal: Garmin's local time, not UTC.
        var from = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var to = from.AddYears(1);
        var inYear = db.Runs.Where(r => r.StartLocal >= from && r.StartLocal < to);
        var runs = await inYear.OrderByDescending(r => r.StartUtc).ThenByDescending(r => r.Id).ToRunRowsAsync(cancellationToken);
        var efforts = await db.RunBestEfforts
            .Join(inYear, e => e.RunId, r => r.Id, (e, r) => new { e.TypeId, e.Seconds, e.RunId, r.StartLocal })
            .ToListAsync(cancellationToken);

        var fastest = efforts.GroupBy(e => e.TypeId)
            .Select(g => g.OrderBy(e => e.Seconds).ThenBy(e => e.StartLocal).First())
            .Select(e => new RecordRow(e.TypeId, e.Seconds, DateOnly.FromDateTime(e.StartLocal), e.RunId));
        var longest = runs.Where(r => r.DistanceMeters > 0).OrderByDescending(r => r.DistanceMeters).ThenBy(r => r.Date).Take(1)
            .Select(r => new RecordRow(RecordTypes.LongestRun, r.DistanceMeters, r.Date, r.Id));

        return new YearData(
            year,
            [.. years.OrderDescending()],
            new Totals(runs.Count, runs.Sum(r => r.DistanceMeters), runs.Sum(r => r.DurationSeconds)),
            (await db.YearGoals.FindAsync([year], cancellationToken))?.DistanceMeters,
            Weeks(year, runs, today),
            [.. Enumerable.Range(1, 12).Select(m => Month(year, m, runs, today))],
            [
                .. runs.GroupBy(r => r.Date).OrderBy(g => g.Key)
                    .Select(g => new RunDay(g.Key, g.Select(r => r.TrainingEffectLabel).Aggregate((string?)null, TrainingEffect.Harder)))
            ],
            [.. fastest.Concat(longest).OrderBy(r => r.TypeId)],
            [.. runs.Where(r => r.IsRace)],
            runs);
    }

    static List<WeekTotal> Weeks(int year, List<RunRow> runs, DateOnly today)
    {
        var byWeek = runs.ToLookup(r => DashboardQueries.Monday(r.Date));
        var thisMonday = DashboardQueries.Monday(today);
        return
        [
            .. ActivityGrid.ForYear(year).Weeks.Select(w => w.Days[0]).Select(monday =>
                new WeekTotal(monday, byWeek[monday].Sum(r => r.DistanceMeters), byWeek[monday].Sum(r => r.DurationSeconds), monday == thisMonday))
        ];
    }

    static MonthTotal Month(int year, int month, List<RunRow> runs, DateOnly today)
    {
        var inMonth = runs.Where(r => r.Date.Month == month).ToList();
        return new MonthTotal(month, inMonth.Sum(r => r.DistanceMeters), inMonth.Sum(r => r.DurationSeconds), year == today.Year && month == today.Month);
    }
}
