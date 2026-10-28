using RunStats.Data;

namespace RunStats.Processing;

// Sorts a run's seconds into pace bands and averages the form metrics in each.
public static class PaceBandBuilder
{
    public const int BandWidthSecondsPerKm = 5;
    // Slower is walking or standing at a crossing.
    public const int SlowestPaceSecondsPerKm = 450;
    // Faster is a GPS glitch.
    public const int FastestPaceSecondsPerKm = 150;
    // GPS speed jumps from second to second; a band should reflect the pace actually run.
    public const int SmoothingSeconds = 10;
    public const double SteepGrade = 0.04;
    // Over a shorter distance the altitude's own noise looks like a slope.
    public const double GradeDistanceMeters = 50;

    public static List<RunPaceBand> Build(long runId, IEnumerable<RunSample> samples)
    {
        var ordered = samples.OrderBy(s => s.OffsetSeconds).ToList();
        var speeds = SmoothedSpeeds(ordered);
        var steep = Steep(ordered);
        var bands = new SortedDictionary<short, Band>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (speeds[i] is not { } speed || speed <= 0)
                continue;
            var pace = 1000 / speed;
            if (pace < FastestPaceSecondsPerKm || pace >= SlowestPaceSecondsPerKm)
                continue;
            var key = (short)(Math.Floor(pace / BandWidthSecondsPerKm) * BandWidthSecondsPerKm);
            if (!bands.TryGetValue(key, out var band))
                bands[key] = band = new Band();
            band.Add(ordered[i], steep[i]);
        }
        return [.. bands.Select(b => b.Value.ToPaceBand(runId, b.Key))];
    }

    // The average over the seconds around each one (half before, half after); seconds without speed don't count.
    static double?[] SmoothedSpeeds(List<RunSample> samples)
    {
        var result = new double?[samples.Count];
        var (first, last, sum, count) = (0, -1, 0.0, 0);
        for (var i = 0; i < samples.Count; i++)
        {
            var from = samples[i].OffsetSeconds - SmoothingSeconds / 2;
            var to = samples[i].OffsetSeconds + (SmoothingSeconds - 1) / 2;
            while (last + 1 < samples.Count && samples[last + 1].OffsetSeconds <= to)
            {
                last++;
                if (samples[last].SpeedMps is { } added)
                    (sum, count) = (sum + added, count + 1);
            }
            for (; samples[first].OffsetSeconds < from; first++)
                if (samples[first].SpeedMps is { } removed)
                    (sum, count) = (sum - removed, count - 1);
            result[i] = samples[i].SpeedMps is null || count == 0 ? null : sum / count;
        }
        return result;
    }

    // The slope over the last GradeDistanceMeters. Without distance or altitude (or in the first metres) a second
    // counts as flat: treadmill runs have no slope, and most outdoor seconds are flat.
    static bool[] Steep(List<RunSample> samples)
    {
        var result = new bool[samples.Count];
        var points = Enumerable.Range(0, samples.Count)
            .Where(i => samples[i].DistanceMeters is not null && samples[i].AltitudeMeters is not null)
            .ToList();
        var start = 0;
        for (var p = 0; p < points.Count; p++)
        {
            var current = samples[points[p]];
            while (start + 1 < p && current.DistanceMeters!.Value - samples[points[start + 1]].DistanceMeters!.Value >= GradeDistanceMeters)
                start++;
            var from = samples[points[start]];
            var distance = current.DistanceMeters!.Value - from.DistanceMeters!.Value;
            if (distance >= GradeDistanceMeters)
                result[points[p]] = Math.Abs(current.AltitudeMeters!.Value - from.AltitudeMeters!.Value) / distance > SteepGrade;
        }
        return result;
    }

    sealed class Band
    {
        int seconds;
        int flatSeconds;
        Mean cadence, stepLength, groundContactTime, verticalOscillation, verticalRatio, heartRate, power, balance;

        public void Add(RunSample s, bool steep)
        {
            seconds++;
            if (steep)
                return;
            flatSeconds++;
            // Outside these ranges a value is a sensor error (a dropped strap reads 0), not running form.
            cadence.Add(s.CadenceSpm, 120, 240);
            stepLength.Add(s.StepLengthMm, 400, 2500);
            groundContactTime.Add(s.GroundContactTimeMs, 100, 450);
            verticalOscillation.Add(s.VerticalOscillationMm, 20, 200);
            verticalRatio.Add(s.VerticalRatioPercent, 2, 20);
            heartRate.Add(s.HeartRate, 40, 230);
            power.Add(s.PowerWatts, 1, 1000);
            balance.Add(s.GroundContactBalanceLeftPercent, 30, 70);
        }

        public RunPaceBand ToPaceBand(long runId, short paceSecondsPerKm) => new()
        {
            RunId = runId,
            PaceSecondsPerKm = paceSecondsPerKm,
            Seconds = seconds,
            FlatSeconds = flatSeconds,
            CadenceSpm = cadence.Value,
            StepLengthMm = stepLength.Value,
            GroundContactTimeMs = groundContactTime.Value,
            VerticalOscillationMm = verticalOscillation.Value,
            VerticalRatioPercent = verticalRatio.Value,
            HeartRate = heartRate.Value,
            PowerWatts = power.Value,
            GroundContactBalanceLeftPercent = balance.Value
        };
    }

    struct Mean
    {
        double sum;
        int count;

        public void Add(double? value, double min, double max)
        {
            if (value is { } v && v >= min && v <= max)
                (sum, count) = (sum + v, count + 1);
        }

        public readonly float? Value => count == 0 ? null : (float)(sum / count);
    }
}
