using Microsoft.EntityFrameworkCore;
using RunStats.Data;
using RunStats.Shared;

namespace RunStats.Features.Pace;

public sealed record PaceReference(int TypeId, double Meters, double? PredictedSeconds, double? RecordSeconds, DateOnly? RecordDate, long? RecordRunId);

public sealed record PaceData(IReadOnlyList<PaceReference> References, DateOnly? PredictedOn);

public interface IPaceQueries
{
    Task<PaceData> LoadAsync(CancellationToken cancellationToken);
}

public class PaceQueries(IDbContextFactory<AppDbContext> dbFactory) : IPaceQueries
{
    public async Task<PaceData> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var predictions = await db.RacePredictions.AsNoTracking().ToDictionaryAsync(p => p.TypeId, cancellationToken);
        var records = await db.PersonalRecords.AsNoTracking().ToDictionaryAsync(r => r.TypeId, cancellationToken);

        var references = new List<PaceReference>();
        foreach (var (typeId, meters) in RecordTypes.Distances.OrderBy(d => d.Value))
        {
            var prediction = predictions.GetValueOrDefault(typeId);
            var record = records.GetValueOrDefault(typeId);
            if (prediction is not null || record is not null)
                references.Add(new PaceReference(typeId, meters, prediction?.Seconds, record?.Value,
                    record is null ? null : DateOnly.FromDateTime(record.AchievedAtLocal), record?.RunId));
        }
        return new PaceData(references, predictions.Count == 0 ? null : predictions.Values.Max(p => p.PredictedOn));
    }
}
