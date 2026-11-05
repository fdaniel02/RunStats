namespace RunStats.Features.Races;

public sealed record PredictedFinish(double Seconds, bool IsEstimate);

public static class RaceFinishPrediction
{
    // 21.1 and 42.2 in the race calendar are the half and the full marathon.
    const double SameDistanceRatio = 0.005;
    const double RiegelExponent = 1.06;

    static readonly string[] OffRoadTypes = ["Trail", "Obstacle"];

    // Road predictions say nothing about climbing or obstacles, so those races get none. Between two of Garmin's distances,
    // the curve through them (straight on log-log axes, like Riegel's formula) is the runner's own fatigue curve.
    public static PredictedFinish? For(double? distanceMeters, string? raceType, IReadOnlyDictionary<double, double> secondsByMeters)
    {
        if (distanceMeters is not > 0 || secondsByMeters.Count == 0 || OffRoadTypes.Contains(raceType, StringComparer.OrdinalIgnoreCase))
            return null;
        var distance = distanceMeters.Value;
        var known = secondsByMeters.OrderBy(p => p.Key).ToList();

        var same = known.FirstOrDefault(p => Math.Abs(p.Key - distance) <= p.Key * SameDistanceRatio);
        if (same.Key > 0)
            return new PredictedFinish(same.Value, false);

        var shorter = known.LastOrDefault(p => p.Key < distance);
        var longer = known.FirstOrDefault(p => p.Key > distance);
        if (shorter.Key > 0 && longer.Key > 0)
        {
            var exponent = Math.Log(longer.Value / shorter.Value) / Math.Log(longer.Key / shorter.Key);
            return new PredictedFinish(shorter.Value * Math.Pow(distance / shorter.Key, exponent), true);
        }
        var nearest = shorter.Key > 0 ? shorter : longer;
        return new PredictedFinish(nearest.Value * Math.Pow(distance / nearest.Key, RiegelExponent), true);
    }
}
