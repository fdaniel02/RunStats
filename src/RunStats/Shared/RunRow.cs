using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Shared;

// A run as the tables show it; Date is Garmin's local date of the run.
public sealed record RunRow(long Id, DateOnly Date, string TypeKey, string Name, double DistanceMeters, double DurationSeconds,
    RunRowMetrics? Metrics = null, bool IsRace = false);

public sealed record RunRowMetrics(
    double? AverageSpeedMps, double? MaxSpeedMps, short? AverageHeartRate, short? MaxHeartRate,
    double? AverageCadenceSpm, double? MaxCadenceSpm, double? StrideLengthCm, double? Calories,
    double? ElevationGainMeters, double? ElevationLossMeters);

public static class RunRowQueries
{
    public static async Task<List<RunRow>> ToRunRowsAsync(this IQueryable<Run> runs, CancellationToken cancellationToken)
    {
        var rows = await runs.Select(r => new
        {
            r.Id, r.StartLocal, r.TypeKey, r.Name, r.DistanceMeters, r.DurationSeconds, r.IsRace,
            Metrics = new RunRowMetrics(r.AverageSpeedMps, r.MaxSpeedMps, r.AverageHeartRate, r.MaxHeartRate,
                r.AverageCadenceSpm, r.MaxCadenceSpm, r.StrideLengthCm, r.Calories, r.ElevationGainMeters, r.ElevationLossMeters)
        }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new RunRow(r.Id, DateOnly.FromDateTime(r.StartLocal), r.TypeKey, r.Name, r.DistanceMeters, r.DurationSeconds, r.Metrics, r.IsRace))];
    }
}
