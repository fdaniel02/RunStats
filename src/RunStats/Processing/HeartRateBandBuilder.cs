using RunStats.Data;

namespace RunStats.Processing;

// Sorts a run's seconds into heart-rate bands with the distance run in each, leaving out the seconds where heart rate
// says little about fitness: the warm-up, slopes, and walking or stopping.
public static class HeartRateBandBuilder
{
    public const int BandWidthBpm = 5;
    // Heart rate lags behind the pace while warming up.
    public const int WarmUpSeconds = 600;
    // Outside this range a value is a sensor error (a dropped strap reads 0).
    const int LowestHeartRate = 40;
    const int HighestHeartRate = 230;

    public static List<RunHeartRateBand> Build(long runId, string typeKey, IEnumerable<RunSample> samples)
    {
        // The treadmill's speed is the watch's own guess, up to 30% off.
        if (typeKey == "treadmill_running")
            return [];
        var ordered = samples.OrderBy(s => s.OffsetSeconds).ToList();
        var speeds = PaceBandBuilder.SmoothedSpeeds(ordered);
        var steep = PaceBandBuilder.Steep(ordered);
        var bands = new SortedDictionary<short, RunHeartRateBand>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var sample = ordered[i];
            if (sample.OffsetSeconds < WarmUpSeconds || steep[i] || speeds[i] is not { } speed || speed <= 0
                || sample.HeartRate is not (>= LowestHeartRate and <= HighestHeartRate) || !PaceBandBuilder.IsRunning(1000 / speed))
                continue;
            var key = (short)(sample.HeartRate.Value / BandWidthBpm * BandWidthBpm);
            if (!bands.TryGetValue(key, out var band))
                bands[key] = band = new RunHeartRateBand { RunId = runId, HeartRate = key };
            band.Seconds++;
            band.DistanceMeters += speed;
        }
        return [.. bands.Values];
    }
}
