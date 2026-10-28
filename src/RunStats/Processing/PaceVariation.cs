using RunStats.Data;

namespace RunStats.Processing;

// How evenly a run and each lap were paced: the coefficient of variation of the speed, in percent. The speed is smoothed
// like for the pace bands, so GPS jitter doesn't count, and stops (slower than walking) are left out, so a traffic light
// isn't a swing.
public static class PaceVariation
{
    // Fewer seconds say little about how even the pace was.
    public const int MinSeconds = 60;

    public static void Apply(Run run, IReadOnlyList<RunLap> laps, IEnumerable<RunSample> samples)
    {
        var ordered = samples.OrderBy(s => s.OffsetSeconds).ToList();
        run.PaceVariationPercent = Percent(ordered);
        var byStart = laps.OrderBy(l => l.StartUtc).ToList();
        for (var i = 0; i < byStart.Count; i++)
        {
            var from = (byStart[i].StartUtc - run.StartUtc).TotalSeconds;
            var to = i + 1 < byStart.Count ? (byStart[i + 1].StartUtc - run.StartUtc).TotalSeconds : double.MaxValue;
            // Smoothed within the lap: across its edges the jog before an interval would blend into it.
            byStart[i].PaceVariationPercent = Percent([.. ordered.Where(s => s.OffsetSeconds >= from && s.OffsetSeconds < to)]);
        }
    }

    // The samples in time order.
    public static double? Percent(List<RunSample> samples)
    {
        var moving = PaceBandBuilder.SmoothedSpeeds(samples).OfType<double>().Where(s => s > 1000.0 / PaceBandBuilder.SlowestPaceSecondsPerKm).ToList();
        if (moving.Count < MinSeconds)
            return null;
        var mean = moving.Average();
        var deviation = Math.Sqrt(moving.Sum(s => (s - mean) * (s - mean)) / moving.Count);
        return deviation / mean * 100;
    }
}
