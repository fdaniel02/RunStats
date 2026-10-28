using System.ComponentModel.DataAnnotations;

namespace RunStats.Features.Warnings;

// The defaults were tuned on the history up to 2026-10: about ten warnings in 14 months, four of them in the three
// weeks before the ankle injury of 2026-09-29. Lower values warn so often that the warnings stop meaning anything.
public sealed class WarningSettings
{
    public const string Section = "Warnings";

    [Range(150, 450)] public int FastPaceSecondsPerKm { get; set; } = 310;
    [Range(150, 450)] public int VeryFastPaceSecondsPerKm { get; set; } = 255;

    // Fast and very fast minutes in the last 7 days against the weekly average of the 4 weeks before.
    [Range(1, 100)] public double FastRatio { get; set; } = 3;
    [Range(0, 1000)] public double FastMinMinutes { get; set; } = 45;
    [Range(1, 100)] public double VeryFastRatio { get; set; } = 2;
    [Range(0, 1000)] public double VeryFastMinMinutes { get; set; } = 8;

    // Running time in the last 4 weeks against the 4-week average of the 12 weeks before. Weekly totals jump too
    // much with 1–4 runs a week.
    [Range(1, 100)] public double VolumeRatio { get; set; } = 1.5;
    [Range(0, 10000)] public double VolumeMinMinutes { get; set; } = 400;

    // The longest run of the last 7 days against the longest of the 4 weeks before.
    [Range(1, 100)] public double LongRunRatio { get; set; } = 1.3;
    [Range(0, 1000)] public double LongRunMinMinutes { get; set; } = 75;

    // A run is hard with this many fast minutes or this long.
    [Range(0, 1000)] public double HardRunFastMinutes { get; set; } = 20;
    [Range(0, 1000)] public double HardRunMinutes { get; set; } = 75;
    [Range(1, 168)] public double HardRunsWithinHours { get; set; } = 72;

    // Form at the same pace: the last 2 weeks against the 8 weeks before, in bands of this width with enough time on
    // both sides.
    [Range(5, 60)] public int FormBandWidthSecondsPerKm { get; set; } = 15;
    [Range(0, 600)] public double FormBandMinMinutes { get; set; } = 5;
    [Range(0, 50)] public double StepLengthIncreasePercent { get; set; } = 4;
    [Range(0, 50)] public double CadenceDropSpm { get; set; } = 3;

    // Left/right balance on strap runs, over the same periods as the form.
    [Range(0, 20)] public double BalanceShiftPoints { get; set; } = 1.5;
    [Range(0, 600)] public double BalanceMinMinutes { get; set; } = 20;
}
