using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Processing;

// Unlike personal records, a distance Garmin doesn't report keeps its last prediction instead of being removed.
public class RacePredictionUpdate(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task ApplyAsync(IReadOnlyList<RacePrediction> current, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.RacePredictions.ToDictionaryAsync(p => p.TypeId, cancellationToken);
        foreach (var prediction in current)
        {
            if (stored.TryGetValue(prediction.TypeId, out var existing))
            {
                existing.Seconds = prediction.Seconds;
                existing.PredictedOn = prediction.PredictedOn;
            }
            else
            {
                db.RacePredictions.Add(prediction);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
