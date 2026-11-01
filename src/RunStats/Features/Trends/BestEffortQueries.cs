using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Features.Trends;

// Effort is null for a month without a run over the distance.
public sealed record EffortMonth(DateOnly Month, MonthEffort? Effort);

public sealed record MonthEffort(double Seconds, long RunId, DateOnly Date, string RunName);

// Best is the fastest of all months. Months are the same for every distance.
public sealed record EffortSeries(int TypeId, MonthEffort Best, IReadOnlyList<EffortMonth> Months);

public interface IBestEffortQueries
{
    Task<IReadOnlyList<EffortSeries>> LoadAsync(CancellationToken cancellationToken);
}

// Months go by the run's own (Garmin local) date, from the first month with an effort to this month.
public class BestEffortQueries(IDbContextFactory<AppDbContext> dbFactory, Clock clock) : IBestEffortQueries
{
    public async Task<IReadOnlyList<EffortSeries>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var efforts = await db.RunBestEfforts
            .Join(db.Runs, e => e.RunId, r => r.Id, (e, r) => new { e.TypeId, e.Seconds, r.Id, r.StartLocal, r.Name })
            .ToListAsync(cancellationToken);
        if (efforts.Count == 0)
            return [];

        static DateOnly MonthOf(DateTime local) => new(local.Year, local.Month, 1);
        var today = clock.Today;
        var months = new List<DateOnly>();
        for (var month = MonthOf(efforts.Min(e => e.StartLocal)); month <= new DateOnly(today.Year, today.Month, 1); month = month.AddMonths(1))
            months.Add(month);

        return
        [
            .. efforts.GroupBy(e => e.TypeId).OrderBy(g => g.Key).Select(type =>
            {
                var fastest = type.GroupBy(e => MonthOf(e.StartLocal))
                    .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Seconds).ThenBy(e => e.StartLocal)
                        .Select(e => new MonthEffort(e.Seconds, e.Id, DateOnly.FromDateTime(e.StartLocal), e.Name)).First());
                return new EffortSeries(
                    type.Key,
                    fastest.Values.OrderBy(e => e.Seconds).ThenBy(e => e.Date).First(),
                    [.. months.Select(m => new EffortMonth(m, fastest.GetValueOrDefault(m)))]);
            })
        ];
    }
}
