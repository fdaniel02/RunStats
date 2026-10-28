using RunStats.Data;
using RunStats.Shared;

namespace RunStats.Features.Runs;

public sealed record SummaryTile(string Label, string Value, string? Detail = null);

public sealed record SummaryGroup(string Title, IReadOnlyList<SummaryTile> Tiles);

// The run's stats as tiles in groups; a stat Garmin didn't record (e.g. ascent on the treadmill) gets no tile, and a
// group without tiles is left out.
public static class RunSummary
{
    public static IReadOnlyList<SummaryGroup> Groups(Run run)
    {
        var groups = new List<SummaryGroup>();
        List<SummaryTile> tiles = [];

        void Group(string title, Action addTiles)
        {
            tiles = [];
            addTiles();
            if (tiles.Count > 0)
                groups.Add(new SummaryGroup(title, tiles));
        }

        void Add(string label, string value, string unit = "", params (string Label, string Value)[] details)
        {
            if (value == Format.Missing)
                return;
            var detail = string.Join(" · ", details.Where(d => d.Value != Format.Missing).Select(d => $"{d.Label} {d.Value}".Trim()));
            tiles.Add(new SummaryTile(label, unit.Length == 0 ? value : $"{value} {unit}", detail.Length == 0 ? null : detail));
        }

        static string Duration(double? seconds) => seconds is { } s ? Format.Duration(s) : Format.Missing;
        static string WithUnit(string value, string unit) => value == Format.Missing ? value : $"{value} {unit}";

        Group("Overview", () =>
        {
            Add("Distance", Format.Km(run.DistanceMeters), "km");
            Add("Time", Format.Duration(run.DurationSeconds), "",
                ("Moving", Duration(run.MovingDurationSeconds)), ("Elapsed", Duration(run.ElapsedDurationSeconds)));
            Add("Pace", Format.Pace(run.DistanceMeters, run.DurationSeconds), "",
                ("Max", WithUnit(Format.MaxPace(run.MaxSpeedMps), "/km")));
            Add("Speed", Format.Speed(run.AverageSpeedMps), "km/h", ("Max", WithUnit(Format.Speed(run.MaxSpeedMps), "km/h")));
        });
        Group("Effort", () =>
        {
            Add("Heart rate", Format.Whole(run.AverageHeartRate), "bpm",
                ("Max", Format.Whole(run.MaxHeartRate)), ("Min", Format.Whole(run.MinHeartRate)));
            if (run.AerobicTrainingEffect is not null || run.AnaerobicTrainingEffect is not null)
                tiles.Add(new SummaryTile("Training effect",
                    Format.Pair(Format.OneDecimal(run.AerobicTrainingEffect), Format.OneDecimal(run.AnaerobicTrainingEffect)), "Aerobic / anaerobic"));
            Add("Training load", Format.Whole(run.TrainingLoad));
            Add("VO2max", Format.Whole(run.Vo2Max));
            Add("Calories", Format.Whole(run.Calories), "kcal");
        });
        Group("Form", () =>
        {
            Add("Cadence", Format.Whole(run.AverageCadenceSpm), "spm", ("Max", Format.Whole(run.MaxCadenceSpm)));
            Add("Stride", Format.Meters(run.StrideLengthCm), "m");
            Add("Steps", Format.Whole(run.Steps));
            if (run.PaceVariationPercent is not null)
                // The treadmill's speed comes from step counting, which is smoother than GPS.
                tiles.Add(new SummaryTile("Pace variation", Format.Variation(run.PaceVariationPercent),
                    run.TypeKey == "treadmill_running" ? "Speed from step counting" : "Lower is more even"));
            if (run.GroundContactBalanceLeftPercent is { } left)
                tiles.Add(new SummaryTile("Left/right balance", $"{Format.OneDecimal(left)}% / {Format.OneDecimal(100 - left)}%", "Ground contact, left / right"));
        });
        Group("Conditions", () =>
        {
            Add("Weather", Format.Whole(run.WeatherTemperatureCelsius), "°C",
                ("Feels", WithUnit(Format.Whole(run.WeatherFeelsLikeCelsius), "°C")),
                ("", WithUnit(Format.Whole(run.WeatherHumidityPercent), "% humidity")),
                ("Wind", Wind(run.WeatherWindSpeedKmh, run.WeatherWindDirection)),
                ("", run.WeatherStation ?? Format.Missing));
            Add("Watch temperature", Format.Whole(run.AverageTemperatureCelsius), "°C", ("", WristDifference(run)));
            Add("Ascent / descent", Format.Pair(Format.Whole(run.ElevationGainMeters), Format.Whole(run.ElevationLossMeters)), "m");
        });
        return groups;
    }

    static string Wind(double? kmh, string? direction) => kmh is null
        ? Format.Missing
        : $"{Format.Whole(kmh)} km/h {direction}".Trim();

    // The watch sits on the wrist, warmed by the body: next to the air temperature that shows how much.
    static string WristDifference(Run run)
    {
        if (run.AverageTemperatureCelsius is not { } watch)
            return Format.Missing;
        if (run.WeatherTemperatureCelsius is not { } air)
            return "On the wrist";
        var difference = Math.Round(watch - air);
        return difference switch
        {
            > 0 => $"On the wrist, {Format.Whole(difference)} °C above the air",
            < 0 => $"On the wrist, {Format.Whole(-difference)} °C below the air",
            _ => "On the wrist, the same as the air"
        };
    }
}
