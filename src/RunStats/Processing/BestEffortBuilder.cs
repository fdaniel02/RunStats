using RunStats.Data;
using RunStats.Shared;

namespace RunStats.Processing;

// A run's fastest stretch over each record distance, from the samples' distance. Like the samples' offsets, the time
// includes pauses.
public static class BestEffortBuilder
{
    // Above a sprinter's top speed.
    public const double MaxSpeedMps = 12;

    public static List<RunBestEffort> Build(long runId, string typeKey, IEnumerable<RunSample> samples)
    {
        // The treadmill's samples have the watch's own distance, up to 30% off the distance corrected after the run.
        if (typeKey == "treadmill_running")
            return [];
        var points = Points(samples);
        return
        [
            .. RecordTypes.Distances
                .Select(d => (TypeId: d.Key, Seconds: Fastest(points, d.Value)))
                .Where(e => e.Seconds is not null)
                .Select(e => new RunBestEffort { RunId = runId, TypeId = e.TypeId, Seconds = e.Seconds!.Value })
        ];
    }

    // In time order. A point whose distance goes back (the watch correcting itself) is left out, so the distance
    // only grows, and a GPS jump faster than anyone runs adds no distance.
    static List<(int Offset, double Distance)> Points(IEnumerable<RunSample> samples)
    {
        var points = new List<(int Offset, double Distance)>();
        var jumped = 0.0;
        foreach (var sample in samples.Where(s => s.DistanceMeters is not null).OrderBy(s => s.OffsetSeconds))
        {
            var distance = sample.DistanceMeters!.Value - jumped;
            if (points.Count > 0 && distance < points[^1].Distance)
                continue;
            if (points.Count > 0 && distance - points[^1].Distance > MaxSpeedMps * (sample.OffsetSeconds - points[^1].Offset))
            {
                jumped += distance - points[^1].Distance;
                distance = points[^1].Distance;
            }
            points.Add((sample.OffsetSeconds, distance));
        }
        return points;
    }

    // For each point, the stretch of exactly `meters` that ends there; its start is interpolated between the two
    // points around it, so the time isn't up to a second too long.
    static double? Fastest(List<(int Offset, double Distance)> points, double meters)
    {
        double? best = null;
        var start = 0;
        for (var end = 1; end < points.Count; end++)
        {
            var from = points[end].Distance - meters;
            if (from < points[0].Distance)
                continue;
            while (start + 1 < end && points[start + 1].Distance <= from)
                start++;
            var (a, b) = (points[start], points[start + 1]);
            var startOffset = a.Offset + (from - a.Distance) / (b.Distance - a.Distance) * (b.Offset - a.Offset);
            var seconds = points[end].Offset - startOffset;
            if (best is null || seconds < best)
                best = seconds;
        }
        return best;
    }
}
