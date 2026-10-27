using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Features.Heatmap;

public sealed record TrackRow(long RunId, DateOnly Date, string TypeKey, string Name, double DistanceMeters, string Polyline);

// Year and TypeKey are the filters applied (null: all); Years and TypeKeys are the choices, only those that have tracks.
public sealed record HeatmapData(
    IReadOnlyList<int> Years, IReadOnlyList<string> TypeKeys, int? Year, string? TypeKey, IReadOnlyList<TrackRow> Tracks);

public interface IHeatmapQueries
{
    Task<HeatmapData> LoadAsync(int? year, string? typeKey, CancellationToken cancellationToken);
}

// Years follow the run's own (Garmin local) date, like everywhere else on the screens.
public class HeatmapQueries(IDbContextFactory<AppDbContext> dbFactory) : IHeatmapQueries
{
    public async Task<HeatmapData> LoadAsync(int? year, string? typeKey, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var withTracks = db.Runs.Where(r => r.Track != null);
        var years = await withTracks.Select(r => r.StartLocal.Year).Distinct().OrderByDescending(y => y).ToListAsync(cancellationToken);
        var typeKeys = await withTracks.Select(r => r.TypeKey).Distinct().OrderBy(t => t).ToListAsync(cancellationToken);
        // A filter that matches nothing on offer (an old bookmark, a typo) is dropped rather than showing an empty map.
        int? shownYear = year is { } y && years.Contains(y) ? y : null;
        var shownType = typeKey is not null && typeKeys.Contains(typeKey) ? typeKey : null;

        var selected = withTracks;
        if (shownYear is { } from)
        {
            var start = new DateTime(from, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var end = start.AddYears(1);
            selected = selected.Where(r => r.StartLocal >= start && r.StartLocal < end);
        }
        if (shownType is not null)
            selected = selected.Where(r => r.TypeKey == shownType);
        var rows = await selected.OrderBy(r => r.StartUtc)
            .Select(r => new { r.Id, r.StartLocal, r.TypeKey, r.Name, r.DistanceMeters, r.Track!.EncodedPolyline })
            .ToListAsync(cancellationToken);
        var tracks = rows.Select(r => new TrackRow(r.Id, DateOnly.FromDateTime(r.StartLocal), r.TypeKey, r.Name, r.DistanceMeters, r.EncodedPolyline)).ToList();
        return new HeatmapData(years, typeKeys, shownYear, shownType, tracks);
    }
}
