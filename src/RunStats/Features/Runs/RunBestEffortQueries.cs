using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Features.Runs;

public sealed record OtherEffort(double Seconds, long RunId, DateOnly Date);

// When this run holds the best, Pr / YearBest is the fastest of the other runs instead (null if there is none), so the
// card can say by how much it is ahead.
public sealed record EffortComparison(OtherEffort? Other, bool IsBest);

public sealed record RunEffort(int TypeId, double Seconds, EffortComparison Pr, EffortComparison YearBest);

public interface IRunBestEffortQueries
{
    Task<IReadOnlyList<RunEffort>> LoadAsync(long runId, CancellationToken cancellationToken);
}

// The PR is the fastest effort of all runs, later ones included; the year best goes by the run's own (Garmin local) year.
// On a tie the earlier run holds the best.
public class RunBestEffortQueries(IDbContextFactory<AppDbContext> dbFactory) : IRunBestEffortQueries
{
    public async Task<IReadOnlyList<RunEffort>> LoadAsync(long runId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var start = await db.Runs.Where(r => r.Id == runId).Select(r => (DateTime?)r.StartLocal).SingleOrDefaultAsync(cancellationToken);
        var own = await db.RunBestEfforts.Where(e => e.RunId == runId).ToListAsync(cancellationToken);
        if (start is null || own.Count == 0)
            return [];

        var types = own.Select(e => e.TypeId).ToList();
        var others = await db.RunBestEfforts
            .Where(e => e.RunId != runId && types.Contains(e.TypeId))
            .Join(db.Runs, e => e.RunId, r => r.Id, (e, r) => new { e.TypeId, e.Seconds, r.Id, r.StartLocal })
            .ToListAsync(cancellationToken);

        EffortComparison Compare(RunBestEffort effort, bool sameYearOnly)
        {
            var ranked = others
                .Where(o => o.TypeId == effort.TypeId && (!sameYearOnly || o.StartLocal.Year == start.Value.Year))
                .Append(new { effort.TypeId, effort.Seconds, Id = runId, StartLocal = start.Value })
                .OrderBy(o => o.Seconds).ThenBy(o => o.StartLocal)
                .ToList();
            var isBest = ranked[0].Id == runId;
            var other = ranked.FirstOrDefault(o => o.Id != runId);
            return new EffortComparison(
                other is null ? null : new OtherEffort(other.Seconds, other.Id, DateOnly.FromDateTime(other.StartLocal)),
                isBest);
        }

        return
        [
            .. own.OrderBy(e => e.TypeId)
                .Select(e => new RunEffort(e.TypeId, e.Seconds, Compare(e, sameYearOnly: false), Compare(e, sameYearOnly: true)))
        ];
    }
}
