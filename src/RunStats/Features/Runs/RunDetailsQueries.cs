using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Features.Runs;

// Polyline is the simplified route the heatmap uses too; null for runs without GPS.
public sealed record RunDetailsData(Run Run, IReadOnlyList<RunLap> Laps, string? Polyline, IReadOnlyList<ChartPoint> ChartPoints);

public interface IRunDetailsQueries
{
    Task<RunDetailsData?> LoadAsync(long id, CancellationToken cancellationToken);
}

public class RunDetailsQueries(IDbContextFactory<AppDbContext> dbFactory) : IRunDetailsQueries
{
    public async Task<RunDetailsData?> LoadAsync(long id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null)
            return null;
        var laps = await db.RunLaps.AsNoTracking().Where(l => l.RunId == id).OrderBy(l => l.LapIndex).ToListAsync(cancellationToken);
        var polyline = await db.RunTracks.Where(t => t.RunId == id).Select(t => t.EncodedPolyline).SingleOrDefaultAsync(cancellationToken);
        var samples = await db.RunSamples.AsNoTracking().Where(s => s.RunId == id).ToListAsync(cancellationToken);
        return new RunDetailsData(run, laps, polyline, RunChartData.Points(samples, hasRoute: polyline is not null));
    }
}
