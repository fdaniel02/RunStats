using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Shared;

namespace RunStats.Features.Races;

public sealed record UpcomingRaceRow(
    Guid NotionPageId, string Name, DateOnly Date, TimeOnly? StartTime, double? DistanceMeters, bool IsRegistered, string? City, string? Link, double? TargetSeconds,
    PredictedFinish? Prediction);

public interface IUpcomingRaceQueries
{
    Task<IReadOnlyList<UpcomingRaceRow>> LoadAsync(CancellationToken cancellationToken);
}

public class UpcomingRaceQueries(IDbContextFactory<AppDbContext> dbFactory, Clock clock) : IUpcomingRaceQueries
{
    public async Task<IReadOnlyList<UpcomingRaceRow>> LoadAsync(CancellationToken cancellationToken)
    {
        // The filter also applies here, because the copy is only as new as the last sync.
        var today = clock.Today;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var races = await db.UpcomingRaces.AsNoTracking().Where(r => r.Date >= today)
            .OrderBy(r => r.Date).ThenBy(r => r.StartTime).ThenBy(r => r.Name).ToListAsync(cancellationToken);
        var predictions = await db.RacePredictions.AsNoTracking().ToListAsync(cancellationToken);
        var secondsByMeters = predictions
            .Where(p => RecordTypes.Distances.ContainsKey(p.TypeId))
            .ToDictionary(p => RecordTypes.Distances[p.TypeId], p => p.Seconds);
        return
        [
            .. races.Select(r => new UpcomingRaceRow(r.NotionPageId, r.Name, r.Date, r.StartTime, r.DistanceMeters, r.IsRegistered, r.City, r.Link, r.TargetSeconds,
                RaceFinishPrediction.For(r.DistanceMeters, r.Type, secondsByMeters)))
        ];
    }
}
