using RunStats.Data;

namespace RunStats.Features.Warnings;

// Before and Now are the averages over the bands compared, each band weighted by its recent time: a different mix of
// paces moves neither, so their difference is the change at the same pace.
public sealed record Comparison(double Before, double Now, double Seconds)
{
    public double Change => Now - Before;
    public double Percent => (Now / Before - 1) * 100;
}

// Form compares like with like: stride and cadence follow pace, so a metric is compared within the same pace band, over
// the bands with enough time on both sides.
public static class SamePace
{
    // Null without a band that has enough time and the metric on both sides.
    public static Comparison? Compare(
        IEnumerable<RunPaceBand> recent, IEnumerable<RunPaceBand> baseline, Func<RunPaceBand, float?> metric, int bandWidthSecondsPerKm, double minSeconds)
    {
        var recentBands = Bands(recent, metric, bandWidthSecondsPerKm);
        var baselineBands = Bands(baseline, metric, bandWidthSecondsPerKm);
        var (before, now, weight) = (0.0, 0.0, 0.0);
        foreach (var (pace, r) in recentBands)
            if (baselineBands.TryGetValue(pace, out var b) && r.Seconds >= minSeconds && b.Seconds >= minSeconds)
                (before, now, weight) = (before + b.Value * r.Seconds, now + r.Value * r.Seconds, weight + r.Seconds);
        return weight == 0 ? null : new Comparison(before / weight, now / weight, weight);
    }

    // The average over the flat seconds of the bands that have the metric.
    public static (double Value, double Seconds)? Mean(IEnumerable<RunPaceBand> bands, Func<RunPaceBand, float?> metric)
    {
        var (sum, seconds) = (0.0, 0.0);
        foreach (var band in bands)
            if (metric(band) is { } value && band.FlatSeconds > 0)
                (sum, seconds) = (sum + value * band.FlatSeconds, seconds + band.FlatSeconds);
        return seconds == 0 ? null : (sum / seconds, seconds);
    }

    // The 5 s/km bands combined into wider ones, each with the metric's average.
    static Dictionary<int, (double Value, double Seconds)> Bands(IEnumerable<RunPaceBand> bands, Func<RunPaceBand, float?> metric, int width) =>
        bands.GroupBy(b => b.PaceSecondsPerKm / width * width)
            .Select(g => (Pace: g.Key, Mean: Mean(g, metric)))
            .Where(g => g.Mean is not null)
            .ToDictionary(g => g.Pace, g => g.Mean!.Value);
}
