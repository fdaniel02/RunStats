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
        var recent = FormBands(Between(runs, asOf, 0, 14)).ToList();
        var baseline = FormBands(Between(runs, asOf, 14, 70)).ToList();
        var stride = Compare(recent, baseline, b => b.StepLengthMm, settings);
        var cadence = Compare(recent, baseline, b => b.CadenceSpm, settings);
        if (!(stride?.Percent > settings.StrideLengthIncreasePercent || cadence?.Change < -settings.CadenceDropSpm))
            return null;
        var seconds = Math.Max(stride?.Seconds ?? 0, cadence?.Seconds ?? 0);
        var strideText = stride is { } s
            ? $"stride length {FromTo(s.Before / 10, s.Now / 10)} cm ({Signed(s.Percent, "0.0")}%)"
            : $"stride length {Format.Missing}";
        var cadenceText = cadence is { } c ? $"cadence {FromTo(c.Before, c.Now)} spm" : $"cadence {Format.Missing}";
        return new Warning(WarningKind.FormChange,
            $"At the same pace, {strideText} and {cadenceText} in the last 2 weeks against the 8 weeks before " +
            $"(over {Format.Hours(seconds)} at comparable paces).");
    }

    static Warning? BalanceShift(IReadOnlyList<WarningRun> runs, DateTime asOf, WarningSettings settings)
    {
        var recent = SamePace.Mean(Between(runs, asOf, 0, 14).SelectMany(r => r.PaceBands), b => b.GroundContactBalanceLeftPercent);
        var baseline = SamePace.Mean(Between(runs, asOf, 14, 70).SelectMany(r => r.PaceBands), b => b.GroundContactBalanceLeftPercent);
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

    // Treadmill speed comes from step counting, and trails change the stride.
    public static IEnumerable<RunPaceBand> FormBands(IEnumerable<WarningRun> runs) =>
        runs.Where(r => r.TypeKey == "running").SelectMany(r => r.PaceBands);

    public static Comparison? Compare(IEnumerable<RunPaceBand> recent, IEnumerable<RunPaceBand> baseline, Func<RunPaceBand, float?> metric,
        WarningSettings settings) =>
        SamePace.Compare(recent, baseline, metric, settings.FormBandWidthSecondsPerKm, settings.FormBandMinMinutes * 60);

    static string FromTo(double before, double now) => $"{before.ToString("0.0", Culture)} → {now.ToString("0.0", Culture)}";

    static string Signed(double? value, string format)
    {
        if (value is not { } v)
            return Format.Missing;
        var sign = v >= 0 ? "+" : "−";
        return sign + Math.Abs(v).ToString(format, Culture);
    }

    static string Day(WarningRun run) => Format.Date(DateOnly.FromDateTime(run.StartLocal));
}
