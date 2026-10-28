using RunStats.Data;
using RunStats.Features.Runs;

namespace RunStats.Tests.Features;

public class RunSummaryTests
{
    static Run Minimal() => new() { DistanceMeters = 5100, DurationSeconds = 1808 };

    static Run Full()
    {
        var run = Minimal();
        run.DistanceMeters = 7500;
        run.DurationSeconds = 2397;
        run.MovingDurationSeconds = 2394;
        run.ElapsedDurationSeconds = 2397;
        run.AverageSpeedMps = 3.13;
        run.MaxSpeedMps = 3.75;
        run.AverageHeartRate = 163;
        run.MaxHeartRate = 181;
        run.MinHeartRate = 59;
        run.AverageCadenceSpm = 178;
        run.MaxCadenceSpm = 186;
        run.StrideLengthCm = 105;
        run.ElevationGainMeters = 10;
        run.ElevationLossMeters = 11;
        run.Calories = 521;
        run.AerobicTrainingEffect = 4.3;
        run.AnaerobicTrainingEffect = 0.4;
        run.TrainingEffectLabel = "TEMPO";
        run.TrainingLoad = 180.4;
        run.Vo2Max = 50;
        run.AverageTemperatureCelsius = 21;
        run.Steps = 7102;
        run.PaceVariationPercent = 3.24;
        run.GroundContactBalanceLeftPercent = 50.34;
        return run;
    }

    static List<SummaryTile> Tiles(Run run) => [.. RunSummary.Groups(run).SelectMany(g => g.Tiles)];

    static Run WithWeather()
    {
        var run = Full();
        run.WeatherTemperatureCelsius = 11.1;
        run.WeatherFeelsLikeCelsius = 9.6;
        run.WeatherHumidityPercent = 82;
        run.WeatherWindSpeedKmh = 12.96;
        run.WeatherWindDirection = "S";
        run.WeatherStation = "Volkel";
        return run;
    }

    [Fact]
    public void Every_recorded_stat_gets_a_tile_in_its_group()
    {
        var groups = RunSummary.Groups(WithWeather());

        Assert.Equal(["Overview", "Effort", "Form", "Conditions"], groups.Select(g => g.Title));
        Assert.Equal(
        [
            new SummaryTile("Distance", "7.5 km"),
            new SummaryTile("Time", "39:57", "Moving 39:54 · Elapsed 39:57"),
            new SummaryTile("Pace", "5:20 /km", "Max 4:27 /km"),
            new SummaryTile("Speed", "11.3 km/h", "Max 13.5 km/h")
        ], groups[0].Tiles);
        Assert.Equal(
        [
            new SummaryTile("Heart rate", "163 bpm", "Max 181 · Min 59"),
            new SummaryTile("Training effect", "4.3 / 0.4", "Aerobic / anaerobic"),
            new SummaryTile("Training load", "180"),
            new SummaryTile("VO2max", "50"),
            new SummaryTile("Calories", "521 kcal")
        ], groups[1].Tiles);
        Assert.Equal(
        [
            new SummaryTile("Cadence", "178 spm", "Max 186"),
            new SummaryTile("Stride", "1.05 m"),
            new SummaryTile("Steps", "7102"),
            new SummaryTile("Pace variation", "±3.2%", "Lower is more even"),
            new SummaryTile("Left/right balance", "50.3% / 49.7%", "Ground contact, left / right")
        ], groups[2].Tiles);
        Assert.Equal(
        [
            new SummaryTile("Weather", "11 °C", "Feels 10 °C · 82 % humidity · Wind 13 km/h S · Volkel"),
            new SummaryTile("Watch temperature", "21 °C", "On the wrist, 10 °C above the air"),
            new SummaryTile("Ascent / descent", "10 / 11 m")
        ], groups[3].Tiles);
    }

    [Fact]
    public void A_group_without_tiles_is_left_out() =>
        Assert.Equal(["Overview"], RunSummary.Groups(Minimal()).Select(g => g.Title));

    [Fact]
    public void Without_weather_the_watch_temperature_stands_alone()
    {
        Assert.Equal(new SummaryTile("Watch temperature", "21 °C", "On the wrist"), Tiles(Full()).Single(t => t.Label == "Watch temperature"));
        Assert.DoesNotContain(Tiles(Full()), t => t.Label == "Weather");
    }

    [Theory]
    [InlineData(25.4, "On the wrist, 4 °C below the air")]
    [InlineData(21.2, "On the wrist, the same as the air")]
    public void The_watch_temperature_says_how_far_it_is_from_the_air(double air, string detail)
    {
        var run = WithWeather();
        run.WeatherTemperatureCelsius = air;

        Assert.Equal(detail, Tiles(run).Single(t => t.Label == "Watch temperature").Detail);
    }

    [Fact]
    public void Weather_details_list_only_what_the_station_reported()
    {
        var run = Minimal();
        run.WeatherTemperatureCelsius = 4;
        run.WeatherWindSpeedKmh = 20;

        Assert.Equal(new SummaryTile("Weather", "4 °C", "Wind 20 km/h"), Tiles(run).Single(t => t.Label == "Weather"));
    }

    [Fact]
    public void Stats_garmin_did_not_record_get_no_tile()
    {
        Assert.Equal(["Distance", "Time", "Pace"], Tiles(Minimal()).Select(t => t.Label));
    }

    [Fact]
    public void Details_list_only_what_was_recorded()
    {
        var run = Minimal();
        run.AverageHeartRate = 140;
        run.MinHeartRate = 85;
        run.ElevationLossMeters = 4;

        var tiles = Tiles(run);

        Assert.Equal(new SummaryTile("Time", "30:08"), tiles.Single(t => t.Label == "Time"));
        Assert.Equal(new SummaryTile("Pace", "5:55 /km"), tiles.Single(t => t.Label == "Pace"));
        Assert.Equal(new SummaryTile("Heart rate", "140 bpm", "Min 85"), tiles.Single(t => t.Label == "Heart rate"));
        Assert.Equal(new SummaryTile("Ascent / descent", "– / 4 m"), tiles.Single(t => t.Label == "Ascent / descent"));
    }

    [Fact]
    public void One_training_effect_is_enough_for_the_tile()
    {
        var run = Minimal();
        run.AerobicTrainingEffect = 3.4;

        Assert.Equal(new SummaryTile("Training effect", "3.4 / –", "Aerobic / anaerobic"), Tiles(run).Single(t => t.Label == "Training effect"));
    }

    [Fact]
    public void A_run_without_distance_has_no_pace()
    {
        var run = Minimal();
        run.DistanceMeters = 0;

        Assert.DoesNotContain("Pace", Tiles(run).Select(t => t.Label));
    }

    [Fact]
    public void On_the_treadmill_the_pace_variation_says_where_the_speed_comes_from()
    {
        var run = Full();
        run.TypeKey = "treadmill_running";

        Assert.Equal(new SummaryTile("Pace variation", "±3.2%", "Speed from step counting"), Tiles(run).Single(t => t.Label == "Pace variation"));
    }

    [Fact]
    public void Without_pace_variation_there_is_no_tile() =>
        Assert.DoesNotContain(Tiles(Minimal()), t => t.Label == "Pace variation");
}
