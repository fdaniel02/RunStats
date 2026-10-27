using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Shared;

namespace RunStats.Features.Dashboard;

public sealed record Totals(int Runs, double DistanceMeters, double DurationSeconds);

public sealed record WeekTotal(DateOnly Monday, double DistanceMeters, double DurationSeconds, bool IsCurrent);

public sealed record RecordRow(int TypeId, double Value, DateOnly Date, long? RunId);

// Races are shown one year at a time: RaceYear of RaceYears, the years that have races (newest first).
public sealed record DashboardData(
    Totals Year, Totals Month, IReadOnlyList<WeekTotal> Weeks, IReadOnlyList<RunRow> LastRuns,
    IReadOnlyList<RecordRow> Records, IReadOnlyList<int> RaceYears, int? RaceYear, IReadOnlyList<RunRow> Races);

public interface IDashboardQueries
{
    Task<DashboardData> LoadAsync(int? raceYear, CancellationToken cancellationToken);
}

// Dates follow the app's TimeZone setting ("this month" is Amsterdam's month); a run's own date is Garmin's local date.
public class DashboardQueries(IDbContextFactory<AppDbContext> dbFactory, Clock clock) : IDashboardQueries
{
    public const int WeekCount = 12;
    public const int LastRunCount = 10;

    public async Task<DashboardData> LoadAsync(int? raceYear, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var raceYears = await RaceYearsAsync(cancellationToken);
        var year = ShownRaceYear(raceYear, raceYears, today.Year);
        return new DashboardData(
            await TotalsSinceAsync(new DateOnly(today.Year, 1, 1), cancellationToken),
            await TotalsSinceAsync(new DateOnly(today.Year, today.Month, 1), cancellationToken),
            await WeeksAsync(cancellationToken),
            await LastRunsAsync(cancellationToken),
            await RecordsAsync(cancellationToken),
            raceYears,
            year,
            year is { } y ? await RacesAsync(y, cancellationToken) : []);
    }

    public async Task<Totals> TotalsSinceAsync(DateOnly from, CancellationToken cancellationToken)
    {
        var fromUtc = clock.StartOfDayUtc(from);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var totals = await db.Runs.Where(r => r.StartUtc >= fromUtc)
            .GroupBy(_ => 1)
            .Select(g => new Totals(g.Count(), g.Sum(r => r.DistanceMeters), g.Sum(r => r.DurationSeconds)))
            .SingleOrDefaultAsync(cancellationToken);
        return totals ?? new Totals(0, 0, 0);
    }

    // The last WeekCount weeks, Monday to Sunday, oldest first; weeks without runs are included with zeros.
    public async Task<List<WeekTotal>> WeeksAsync(CancellationToken cancellationToken)
    {
        var thisMonday = Monday(clock.Today);
        var firstMonday = thisMonday.AddDays(-7 * (WeekCount - 1));
        var fromUtc = clock.StartOfDayUtc(firstMonday);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db.Runs.Where(r => r.StartUtc >= fromUtc)
            .Select(r => new { r.StartUtc, r.DistanceMeters, r.DurationSeconds })
            .ToListAsync(cancellationToken);
        var byWeek = runs.ToLookup(r => Monday(clock.ToLocalDate(r.StartUtc)));
        return
        [
            .. Enumerable.Range(0, WeekCount).Select(i =>
            {
                var monday = firstMonday.AddDays(7 * i);
                var week = byWeek[monday];
                return new WeekTotal(monday, week.Sum(r => r.DistanceMeters), week.Sum(r => r.DurationSeconds), monday == thisMonday);
            })
        ];
    }

    public async Task<List<RunRow>> LastRunsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return Rows(await db.Runs.OrderByDescending(r => r.StartUtc).Take(LastRunCount).Select(RowColumns).ToListAsync(cancellationToken));
    }

    // The requested race year if it has races, otherwise this year, otherwise the latest year with races.
    static int? ShownRaceYear(int? requested, List<int> raceYears, int thisYear)
    {
        if (requested is { } year && raceYears.Contains(year))
            return year;
        if (raceYears.Contains(thisYear))
            return thisYear;
        return raceYears.Count > 0 ? raceYears[0] : null;
    }

    // By the race's own (Garmin local) date, the date the table shows.
    public async Task<List<int>> RaceYearsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.Where(r => r.IsRace).Select(r => r.StartLocal.Year).Distinct().OrderByDescending(y => y).ToListAsync(cancellationToken);
    }

    public async Task<List<RunRow>> RacesAsync(int year, CancellationToken cancellationToken)
    {
        // Unspecified like StartLocal: Garmin's local time, not UTC.
        var from = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var to = from.AddYears(1);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return Rows(await db.Runs.Where(r => r.IsRace && r.StartLocal >= from && r.StartLocal < to)
            .OrderByDescending(r => r.StartUtc).Select(RowColumns).ToListAsync(cancellationToken));
    }

    public async Task<List<RecordRow>> RecordsAsync(CancellationToken cancellationToken)
    {
        var known = RecordTypes.Known.ToList();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var records = await db.PersonalRecords.Where(r => known.Contains(r.TypeId)).OrderBy(r => r.TypeId).ToListAsync(cancellationToken);
        return [.. records.Select(r => new RecordRow(r.TypeId, r.Value, DateOnly.FromDateTime(r.AchievedAtLocal), r.RunId))];
    }

    sealed record RowValues(long Id, DateTime StartLocal, string TypeKey, string Name, double DistanceMeters, double DurationSeconds);

    static readonly System.Linq.Expressions.Expression<Func<Run, RowValues>> RowColumns =
        r => new RowValues(r.Id, r.StartLocal, r.TypeKey, r.Name, r.DistanceMeters, r.DurationSeconds);

    static List<RunRow> Rows(List<RowValues> rows) =>
        [.. rows.Select(r => new RunRow(r.Id, DateOnly.FromDateTime(r.StartLocal), r.TypeKey, r.Name, r.DistanceMeters, r.DurationSeconds))];

    public static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
