using RunStats.Data;

namespace RunStats.Features.Runs;

// The run's samples averaged over one stretch of distance; null where nothing was recorded there.
public sealed record ChartPoint(
    double Km, double? PaceSecondsPerKm, double? HeartRate, double? ElevationMeters,
    double? CadenceSpm, double? StrideMeters, double? Latitude, double? Longitude);

public static class RunChartData
{
    public const int MaxPoints = 400;

    // Slower than 16:40 /km counts as standing still: no pace there, so a stop at a crossing doesn't stretch the scale.
    public const double MovingSpeedMps = 1.0;

    // GPS speed, cadence and stride jump from second to second; a centred window of this many seconds evens them out.
    public const int SmoothingSeconds = 15;

    const double SemicirclesToDegrees = 180.0 / 2147483648.0;

    // Without a route (the treadmill) the barometer's altitude means nothing, so there's no elevation.
    public static IReadOnlyList<ChartPoint> Points(IReadOnlyList<RunSample> samples, bool hasRoute, int maxPoints = MaxPoints)
    {
        var ordered = samples.Where(s => s.DistanceMeters is not null).OrderBy(s => s.OffsetSeconds).ToList();
        if (ordered.Count < 2 || ordered[^1].DistanceMeters is not > 0)
            return [];
        var speeds = Smoothed(ordered, s => s.SpeedMps);
        // Zero while standing still: left out, so a stop doesn't pull the average down.
        var cadences = Smoothed(ordered, s => s.CadenceSpm > 0 ? s.CadenceSpm : null);
        var strides = Smoothed(ordered, s => s.StepLengthMm > 0 ? s.StepLengthMm / 1000.0 : null);
        var stretch = ordered[^1].DistanceMeters!.Value / maxPoints;
        return
        [
            .. Enumerable.Range(0, ordered.Count)
                .GroupBy(i => Math.Min((int)(ordered[i].DistanceMeters!.Value / stretch), maxPoints - 1))
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var indexes = g.ToList();
                    var samplesHere = indexes.Select(i => ordered[i]).ToList();
                    var moving = indexes.Select(i => speeds[i]).Where(v => v >= MovingSpeedMps).Select(v => v!.Value).ToList();
                    var position = samplesHere.LastOrDefault(s => s.LatitudeSemicircles is not null && s.LongitudeSemicircles is not null);
                    return new ChartPoint(
                        samplesHere.Average(s => s.DistanceMeters!.Value) / 1000,
                        moving.Count > 0 ? 1000 / moving.Average() : null,
                        Mean(samplesHere.Select(s => (double?)s.HeartRate)),
                        hasRoute ? Mean(samplesHere.Select(s => (double?)s.AltitudeMeters)) : null,
                        Mean(indexes.Select(i => cadences[i])),
                        Mean(indexes.Select(i => strides[i])),
                        position?.LatitudeSemicircles * SemicirclesToDegrees,
                        position?.LongitudeSemicircles * SemicirclesToDegrees);
                })
        ];
    }

    // The mean of the values within SmoothingSeconds around each sample; null where none was recorded around it.
    static double?[] Smoothed(List<RunSample> samples, Func<RunSample, double?> value)
    {
        var half = SmoothingSeconds / 2;
        var result = new double?[samples.Count];
        int first = 0, next = 0, count = 0;
        double sum = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            var offset = samples[i].OffsetSeconds;
            for (; next < samples.Count && samples[next].OffsetSeconds <= offset + half; next++)
            {
                if (value(samples[next]) is { } v)
                {
                    sum += v;
                    count++;
                }
            }
            for (; samples[first].OffsetSeconds < offset - half; first++)
            {
                if (value(samples[first]) is { } v)
                {
                    sum -= v;
                    count--;
                }
            }
            result[i] = count > 0 ? sum / count : null;
        }
        return result;
    }

    static double? Mean(IEnumerable<double?> values)
    {
        var present = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return present.Count > 0 ? present.Average() : null;
    }
}
