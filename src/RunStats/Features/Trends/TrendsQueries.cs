using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Dashboard;
using RunStats.Features.Warnings;
using RunStats.Processing;

namespace RunStats.Features.Trends;

// Warnings are the rules evaluated at the end of the week (now for the current week): what that week's running set off,
// not warnings left over from the week before.
public sealed record LoadWeek(DateOnly Monday, double Seconds, double FastSeconds, double VeryFastSeconds, IReadOnlyList<WarningKind> Warnings, bool IsCurrent);

// One warning on consecutive days; the message is the first day's. To is today while it's still active.
public sealed record WarningEpisode(WarningKind Kind, DateOnly From, DateOnly To, string Message);

public sealed record TrendsData(
    IReadOnlyList<Warning> Active, IReadOnlyList<WarningEpisode> Episodes, IReadOnlyList<LoadWeek> Weeks,
    double? TypicalPaceSecondsPerKm, int FastPaceSecondsPerKm, int VeryFastPaceSecondsPerKm);

public interface ITrendsQueries
{
    Task<TrendsData> LoadAsync(CancellationToken cancellationToken);
}

public class TrendsQueries(
    IDbContextFactory<AppDbContext> dbFactory, IWarningQueries warnings, IOptions<WarningSettings> settings, Clock clock, TimeProvider time)
    : ITrendsQueries
{
    public const int WeekCount = 26;
    public const int TypicalPaceDays = 56;

    public async Task<TrendsData> LoadAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var thisMonday = DashboardQueries.Monday(today);
        var firstMonday = thisMonday.AddDays(-7 * (WeekCount - 1));
        var days = await warnings.DailyAsync(firstMonday, today, cancellationToken);
        return new TrendsData(
            await warnings.ActiveAsync(cancellationToken),
            Episodes(days, today),
            await WeeksAsync(firstMonday, thisMonday, cancellationToken),
            await TypicalPaceAsync(today, cancellationToken),
            settings.Value.FastPaceSecondsPerKm,
            settings.Value.VeryFastPaceSecondsPerKm);
    }

    // Newest first.
    public static List<WarningEpisode> Episodes(IReadOnlyList<DayWarnings> days, DateOnly today)
    {
        var episodes = new List<WarningEpisode>();
        var open = new Dictionary<WarningKind, WarningEpisode>();
        foreach (var day in days.OrderBy(d => d.Date))
            foreach (var warning in day.Warnings)
            {
                if (open.TryGetValue(warning.Kind, out var episode) && episode.To == day.Date.AddDays(-1))
                    open[warning.Kind] = episode with { To = day.Date };
                else
                {
                    if (episode is not null)
                        episodes.Add(episode);
                    open[warning.Kind] = new WarningEpisode(warning.Kind, day.Date, day.Date, warning.Message);
                }
            }
        episodes.AddRange(open.Values);
        return [.. episodes.OrderByDescending(e => e.From).ThenBy(e => e.Kind)];
    }

    async Task<List<LoadWeek>> WeeksAsync(DateOnly firstMonday, DateOnly thisMonday, CancellationToken cancellationToken)
    {
        var fromUtc = clock.StartOfDayUtc(firstMonday);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db.Runs.AsNoTracking()
            .Where(r => r.StartUtc >= fromUtc)
            .Select(r => new WarningRun(r.StartUtc, r.StartLocal, r.TypeKey, r.DurationSeconds, r.PaceBands.ToList()))
            .ToListAsync(cancellationToken);
        var byWeek = runs.ToLookup(r => DashboardQueries.Monday(clock.ToLocalDate(r.StartUtc)));
        var now = time.GetUtcNow().UtcDateTime;
        var ends = Enumerable.Range(0, WeekCount)
            .Select(i => firstMonday.AddDays(7 * i) == thisMonday ? now : clock.StartOfDayUtc(firstMonday.AddDays(7 * (i + 1))))
            .ToList();
        var weekWarnings = await warnings.AtAsync(ends, cancellationToken);
        return
        [
            .. Enumerable.Range(0, WeekCount).Select(i =>
            {
                var monday = firstMonday.AddDays(7 * i);
                var week = byWeek[monday].ToList();
                return new LoadWeek(
                    monday,
                    week.Sum(r => r.DurationSeconds),
                    week.Sum(r => WarningRules.FastSeconds(r, settings.Value.FastPaceSecondsPerKm)),
                    week.Sum(r => WarningRules.FastSeconds(r, settings.Value.VeryFastPaceSecondsPerKm)),
                    [.. weekWarnings[i].Select(w => w.Kind).Order()],
                    monday == thisMonday);
            })
        ];
    }

    // The median pace of the outdoor runs' seconds: shows when the fast setting no longer fits.
    async Task<double?> TypicalPaceAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var fromUtc = clock.StartOfDayUtc(today.AddDays(-TypicalPaceDays));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var bands = await db.RunPaceBands.AsNoTracking()
            .Where(b => db.Runs.Any(r => r.Id == b.RunId && r.TypeKey == "running" && r.StartUtc >= fromUtc))
            .GroupBy(b => b.PaceSecondsPerKm)
            .Select(g => new { Pace = g.Key, Seconds = g.Sum(b => b.Seconds) })
            .OrderBy(b => b.Pace)
            .ToListAsync(cancellationToken);
        var half = bands.Sum(b => (double)b.Seconds) / 2;
        var counted = 0.0;
        foreach (var band in bands)
        {
            counted += band.Seconds;
            if (counted >= half)
                return band.Pace + PaceBandBuilder.BandWidthSecondsPerKm / 2.0;
        }
        return null;
    }
}
