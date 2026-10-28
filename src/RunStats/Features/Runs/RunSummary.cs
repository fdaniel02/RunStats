using RunStats.Data;
using RunStats.Shared;

namespace RunStats.Features.Runs;

public sealed record SummaryTile(string Label, string Value, string? Detail = null);

// The run's stats as tiles; a stat Garmin didn't record (e.g. ascent on the treadmill) gets no tile.
public static class RunSummary
{
    public static IReadOnlyList<SummaryTile> Tiles(Run run)
    {
        var tiles = new List<SummaryTile>();

        void Add(string label, string value, string unit = "", params (string Label, string Value)[] details)
        {
            if (value == Format.Missing)
                return;
            var detail = string.Join(" · ", details.Where(d => d.Value != Format.Missing).Select(d => $"{d.Label} {d.Value}"));
            tiles.Add(new SummaryTile(label, unit.Length == 0 ? value : $"{value} {unit}", detail.Length == 0 ? null : detail));
        }

        static string Duration(double? seconds) => seconds is { } s ? Format.Duration(s) : Format.Missing;
        static string WithUnit(string value, string unit) => value == Format.Missing ? value : $"{value} {unit}";

        Add("Distance", Format.Km(run.DistanceMeters), "km");
        Add("Time", Format.Duration(run.DurationSeconds), "",
            ("Moving", Duration(run.MovingDurationSeconds)), ("Elapsed", Duration(run.ElapsedDurationSeconds)));
        Add("Pace", Format.Pace(run.DistanceMeters, run.DurationSeconds), "",
            ("Max", WithUnit(Format.MaxPace(run.MaxSpeedMps), "/km")));
        if (run.PaceVariationPercent is not null)
            // The treadmill's speed comes from step counting, which is smoother than GPS.
            tiles.Add(new SummaryTile("Pace variation", Format.Variation(run.PaceVariationPercent),
                run.TypeKey == "treadmill_running" ? "Speed from step counting" : "Lower is more even"));
        Add("Speed", Format.Speed(run.AverageSpeedMps), "km/h", ("Max", WithUnit(Format.Speed(run.MaxSpeedMps), "km/h")));
        Add("Heart rate", Format.Whole(run.AverageHeartRate), "bpm",
            ("Max", Format.Whole(run.MaxHeartRate)), ("Min", Format.Whole(run.MinHeartRate)));
        Add("Cadence", Format.Whole(run.AverageCadenceSpm), "spm", ("Max", Format.Whole(run.MaxCadenceSpm)));
        Add("Stride", Format.Meters(run.StrideLengthCm), "m");
        Add("Ascent / descent", Format.Pair(Format.Whole(run.ElevationGainMeters), Format.Whole(run.ElevationLossMeters)), "m");
        Add("Calories", Format.Whole(run.Calories), "kcal");
        if (run.AerobicTrainingEffect is not null || run.AnaerobicTrainingEffect is not null)
            tiles.Add(new SummaryTile("Training effect",
                Format.Pair(Format.OneDecimal(run.AerobicTrainingEffect), Format.OneDecimal(run.AnaerobicTrainingEffect)), "Aerobic / anaerobic"));
        Add("Primary benefit", TrainingEffect.Name(run.TrainingEffectLabel) ?? Format.Missing);
        Add("Training load", Format.Whole(run.TrainingLoad));
        Add("VO2max", Format.Whole(run.Vo2Max));
        Add("Temperature", Format.Whole(run.AverageTemperatureCelsius), "°C");
        Add("Steps", Format.Whole(run.Steps));
        return tiles;
    }
}
