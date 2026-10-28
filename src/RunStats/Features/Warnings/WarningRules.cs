using System.Globalization;
using RunStats.Data;
using RunStats.Processing;
using RunStats.Shared;

namespace RunStats.Features.Warnings;

public enum WarningKind { FastJump, VeryFastJump, VolumeJump, LongRunJump, HardRunsBackToBack, FormChange, BalanceShift }

public sealed record Warning(WarningKind Kind, string Message);

// StartLocal is only for the messages: the windows count in UTC.
public sealed record WarningRun(DateTime StartUtc, DateTime StartLocal, string TypeKey, double DurationSeconds, IReadOnlyList<RunPaceBand> PaceBands);

// The windows roll back from the moment of evaluation, so a warning doesn't vanish when a calendar week starts.
public static class WarningRules
{
    // The longest look back: the volume rule's 4 + 12 weeks.
    public static readonly TimeSpan History = TimeSpan.FromDays(16 * 7);

    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static List<Warning> Evaluate(IReadOnlyList<WarningRun> runs, DateTime asOfUtc, WarningSettings settings)
    {
        Warning?[] warnings =
        [
            FastJump(WarningKind.FastJump, runs, asOfUtc, settings.FastPaceSecondsPerKm, settings.FastRatio, settings.FastMinMinutes),
            FastJump(WarningKind.VeryFastJump, runs, asOfUtc, settings.VeryFastPaceSecondsPerKm, settings.VeryFastRatio, settings.VeryFastMinMinutes),
            VolumeJump(runs, asOfUtc, settings),
            LongRunJump(runs, asOfUtc, settings),
            HardRunsBackToBack(runs, asOfUtc, settings),
            FormChange(runs, asOfUtc, settings),
            BalanceShift(runs, asOfUtc, settings)
        ];
        return [.. warnings.OfType<Warning>()];
    }

    public static double FastSeconds(WarningRun run, int paceSecondsPerKm) =>
        run.PaceBands.Where(b => b.PaceSecondsPerKm + PaceBandBuilder.BandWidthSecondsPerKm <= paceSecondsPerKm).Sum(b => b.Seconds);

    static Warning? FastJump(WarningKind kind, IReadOnlyList<WarningRun> runs, DateTime asOf, int pace, double ratio, double minMinutes)
    {
        var current = Between(runs, asOf, 0, 7).Sum(r => FastSeconds(r, pace));
        var weeklyBefore = Between(runs, asOf, 7, 35).Sum(r => FastSeconds(r, pace)) / 4;
        return current >= minMinutes * 60 && current > ratio * weeklyBefore
            ? new Warning(kind, $"{Format.Hours(current)} faster than {Format.Pace(1000, pace)} in the last 7 days, " +
                $"against {Format.Hours(weeklyBefore)} a week in the 4 weeks before.")
            : null;
    }

    static Warning? VolumeJump(IReadOnlyList<WarningRun> runs, DateTime asOf, WarningSettings settings)
    {
        var current = Between(runs, asOf, 0, 28).Sum(r => r.DurationSeconds);
        var before = Between(runs, asOf, 28, 112).Sum(r => r.DurationSeconds) / 3;
        return current >= settings.VolumeMinMinutes * 60 && current > settings.VolumeRatio * before
            ? new Warning(WarningKind.VolumeJump, $"{Format.Hours(current)} of running in the last 4 weeks, " +
                $"against {Format.Hours(before)} per 4 weeks in the 12 weeks before.")
            : null;
    }

    static Warning? LongRunJump(IReadOnlyList<WarningRun> runs, DateTime asOf, WarningSettings settings)
    {
        var current = Between(runs, asOf, 0, 7).ToList();
        var before = Between(runs, asOf, 7, 35).ToList();
        // Without runs before there's nothing to compare with, e.g. after a break.
        if (current.Count == 0 || before.Count == 0)
            return null;
        var longest = current.Max(r => r.DurationSeconds);
        var longestBefore = before.Max(r => r.DurationSeconds);
        return longest >= settings.LongRunMinMinutes * 60 && longest > settings.LongRunRatio * longestBefore
            ? new Warning(WarningKind.LongRunJump, $"A {Format.Hours(longest)} run in the last 7 days; " +
                $"the longest in the 4 weeks before was {Format.Hours(longestBefore)}.")
            : null;
    }

    static Warning? HardRunsBackToBack(IReadOnlyList<WarningRun> runs, DateTime asOf, WarningSettings settings)
    {
        var hard = Between(runs, asOf, 0, 7)
            .Where(r => FastSeconds(r, settings.FastPaceSecondsPerKm) >= settings.HardRunFastMinutes * 60 || r.DurationSeconds >= settings.HardRunMinutes * 60)
            .OrderBy(r => r.StartUtc)
            .ToList();
        var pair = hard.Zip(hard.Skip(1))
            .LastOrDefault(p => (p.Second.StartUtc - p.First.StartUtc).TotalHours <= settings.HardRunsWithinHours);
        if (pair.First is null)
            return null;
        var hours = (pair.Second.StartUtc - pair.First.StartUtc).TotalHours;
        return new Warning(WarningKind.HardRunsBackToBack,
            $"Hard runs on {Day(pair.First)} and {Day(pair.Second)}, {hours.ToString("0", Culture)} h apart.");
    }

    static Warning? FormChange(IReadOnlyList<WarningRun> runs, DateTime asOf, WarningSettings settings)
    {
        // Treadmill speed comes from step counting, and trails change the stride.
        var recent = Bands(Between(runs, asOf, 0, 14).Where(r => r.TypeKey == "running"), settings.FormBandWidthSecondsPerKm);
        var baseline = Bands(Between(runs, asOf, 14, 70).Where(r => r.TypeKey == "running"), settings.FormBandWidthSecondsPerKm);
        var minSeconds = settings.FormBandMinMinutes * 60;
        var compared = recent.Keys.Intersect(baseline.Keys)
            .Select(pace => (Recent: recent[pace], Baseline: baseline[pace]))
            .Where(b => FlatSeconds(b.Recent) >= minSeconds && FlatSeconds(b.Baseline) >= minSeconds)
            .ToList();
        var stepChange = Change(compared, b => b.StepLengthMm, (r, b) => (r / b - 1) * 100);
        var cadenceChange = Change(compared, b => b.CadenceSpm, (r, b) => r - b);
        if (!(stepChange > settings.StepLengthIncreasePercent || cadenceChange < -settings.CadenceDropSpm))
            return null;
        var seconds = compared.Sum(b => FlatSeconds(b.Recent));
        return new Warning(WarningKind.FormChange,
            $"At the same pace, step length {Signed(stepChange, "0.0")}% and cadence {Signed(cadenceChange, "0.0")} spm in the last 2 weeks, " +
            $"against the 8 weeks before (over {Format.Hours(seconds)} at comparable paces).");
    }

    static Warning? BalanceShift(IReadOnlyList<WarningRun> runs, DateTime asOf, WarningSettings settings)
    {
        var recent = Mean(Between(runs, asOf, 0, 14).SelectMany(r => r.PaceBands), b => b.GroundContactBalanceLeftPercent);
        var baseline = Mean(Between(runs, asOf, 14, 70).SelectMany(r => r.PaceBands), b => b.GroundContactBalanceLeftPercent);
        var minSeconds = settings.BalanceMinMinutes * 60;
        if (recent is not { } r || baseline is not { } b || r.Seconds < minSeconds || b.Seconds < minSeconds
            || Math.Abs(r.Value - b.Value) <= settings.BalanceShiftPoints)
            return null;
        return new Warning(WarningKind.BalanceShift,
            $"Left/right balance {r.Value.ToString("0.0", Culture)}% left in the last 2 weeks, against {b.Value.ToString("0.0", Culture)}% in the 8 weeks before.");
    }

    // The runs that started within (asOf - toDays, asOf - fromDays].
    static IEnumerable<WarningRun> Between(IReadOnlyList<WarningRun> runs, DateTime asOf, int fromDays, int toDays) =>
        runs.Where(r => r.StartUtc > asOf.AddDays(-toDays) && r.StartUtc <= asOf.AddDays(-fromDays));

    // The pace bands of all runs, combined into bands of the given width.
    static Dictionary<int, List<RunPaceBand>> Bands(IEnumerable<WarningRun> runs, int width) =>
        runs.SelectMany(r => r.PaceBands)
            .Where(b => b.FlatSeconds > 0)
            .GroupBy(b => b.PaceSecondsPerKm / width * width)
            .ToDictionary(g => g.Key, g => g.ToList());

    static double FlatSeconds(List<RunPaceBand> bands) => bands.Sum(b => b.FlatSeconds);

    // The change from the baseline to the recent value in each band, averaged with the recent time as weight.
    static double? Change(List<(List<RunPaceBand> Recent, List<RunPaceBand> Baseline)> bands, Func<RunPaceBand, float?> metric,
        Func<double, double, double> change)
    {
        var (sum, weight) = (0.0, 0.0);
        foreach (var (recent, baseline) in bands)
            if (Mean(recent, metric) is { } r && Mean(baseline, metric) is { } b)
                (sum, weight) = (sum + change(r.Value, b.Value) * r.Seconds, weight + r.Seconds);
        return weight == 0 ? null : sum / weight;
    }

    // The average over the flat seconds of the bands that have the metric.
    static (double Value, double Seconds)? Mean(IEnumerable<RunPaceBand> bands, Func<RunPaceBand, float?> metric)
    {
        var (sum, seconds) = (0.0, 0.0);
        foreach (var band in bands)
            if (metric(band) is { } value && band.FlatSeconds > 0)
                (sum, seconds) = (sum + value * band.FlatSeconds, seconds + band.FlatSeconds);
        return seconds == 0 ? null : (sum / seconds, seconds);
    }

    static string Signed(double? value, string format) => value is { } v
        ? (v >= 0 ? "+" : "−") + Math.Abs(v).ToString(format, Culture)
        : Format.Missing;

    static string Day(WarningRun run) => Format.Date(DateOnly.FromDateTime(run.StartLocal));
}
