using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Shared;

namespace RunStats.Features.Runs;

// From and To are inclusive, by the run's own (Garmin local) date.
public sealed record RunFilter(DateOnly? From = null, DateOnly? To = null, string? TypeKey = null, bool RacesOnly = false);

// Filter is the filter applied; TypeKeys and HasRaces are the choices. The totals cover every page.
public sealed record RunListData(
    IReadOnlyList<string> TypeKeys, bool HasRaces, RunFilter Filter,
    int Count, double DistanceMeters, double DurationSeconds,
    int Page, int PageCount, IReadOnlyList<RunRow> Runs);

public interface IRunListQueries
{
    Task<RunListData> LoadAsync(RunFilter filter, int page, CancellationToken cancellationToken);
}

public class RunListQueries(IDbContextFactory<AppDbContext> dbFactory) : IRunListQueries
{
    public const int PageSize = 50;

    public async Task<RunListData> LoadAsync(RunFilter filter, int page, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var typeKeys = await db.Runs.Select(r => r.TypeKey).Distinct().OrderBy(t => t).ToListAsync(cancellationToken);
        var hasRaces = await db.Runs.AnyAsync(r => r.IsRace, cancellationToken);
        var shown = Shown(filter, typeKeys, hasRaces);

        var selected = db.Runs.AsQueryable();
        if (shown.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            selected = selected.Where(r => r.StartLocal >= start);
        }
        if (shown.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            selected = selected.Where(r => r.StartLocal < end);
        }
        if (shown.TypeKey is not null)
            selected = selected.Where(r => r.TypeKey == shown.TypeKey);
        if (shown.RacesOnly)
            selected = selected.Where(r => r.IsRace);

        var totals = await selected.GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Distance = g.Sum(r => r.DistanceMeters), Duration = g.Sum(r => r.DurationSeconds) })
            .SingleOrDefaultAsync(cancellationToken);
        var count = totals?.Count ?? 0;
        var pageCount = Math.Max(1, (count + PageSize - 1) / PageSize);
        var shownPage = Math.Clamp(page, 1, pageCount);
        // Id breaks ties, so a run can't show up on two pages.
        var runs = await selected.OrderByDescending(r => r.StartUtc).ThenByDescending(r => r.Id)
            .Skip((shownPage - 1) * PageSize).Take(PageSize)
            .ToRunRowsAsync(cancellationToken);
        return new RunListData(typeKeys, hasRaces, shown, count, totals?.Distance ?? 0, totals?.Duration ?? 0, shownPage, pageCount, runs);
    }

    // Filters that can't match (an old bookmark, a typo) are dropped; dates the wrong way round are swapped.
    static RunFilter Shown(RunFilter filter, List<string> typeKeys, bool hasRaces)
    {
        var (from, to) = filter is { From: { } f, To: { } t } && f > t ? (t, f) : (filter.From, filter.To);
        var typeKey = filter.TypeKey is not null && typeKeys.Contains(filter.TypeKey) ? filter.TypeKey : null;
        return new RunFilter(from, to, typeKey, filter.RacesOnly && hasRaces);
    }
}
