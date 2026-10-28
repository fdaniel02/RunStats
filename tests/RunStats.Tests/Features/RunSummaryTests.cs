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

    [Fact]
    public void Every_recorded_stat_gets_a_tile()
    {
        var tiles = RunSummary.Tiles(Full());

        Assert.Equal(
        [
            new SummaryTile("Distance", "7.5 km"),
            new SummaryTile("Time", "39:57", "Moving 39:54 · Elapsed 39:57"),
            new SummaryTile("Pace", "5:20 /km", "Max 4:27 /km"),
            new SummaryTile("Pace variation", "±3.2%", "Lower is more even"),
            new SummaryTile("Speed", "11.3 km/h", "Max 13.5 km/h"),
            new SummaryTile("Heart rate", "163 bpm", "Max 181 · Min 59"),
            new SummaryTile("Cadence", "178 spm", "Max 186"),
            new SummaryTile("Stride", "1.05 m"),
            new SummaryTile("Left/right balance", "50.3% / 49.7%", "Ground contact, left / right"),
            new SummaryTile("Ascent / descent", "10 / 11 m"),
            new SummaryTile("Calories", "521 kcal"),
            new SummaryTile("Training effect", "4.3 / 0.4", "Aerobic / anaerobic"),
            new SummaryTile("Training load", "180"),
            new SummaryTile("VO2max", "50"),
            new SummaryTile("Temperature", "21 °C"),
            new SummaryTile("Steps", "7102")
        ], tiles);
    }

    [Fact]
    public void Stats_garmin_did_not_record_get_no_tile()
    {
        Assert.Equal(["Distance", "Time", "Pace"], RunSummary.Tiles(Minimal()).Select(t => t.Label));
    }

    [Fact]
    public void Details_list_only_what_was_recorded()
    {
        var run = Minimal();
        run.AverageHeartRate = 140;
        run.MinHeartRate = 85;
        run.ElevationLossMeters = 4;

        var tiles = RunSummary.Tiles(run);

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

        Assert.Equal(new SummaryTile("Training effect", "3.4 / –", "Aerobic / anaerobic"), RunSummary.Tiles(run).Single(t => t.Label == "Training effect"));
    }

    [Fact]
    public void A_run_without_distance_has_no_pace()
    {
        var run = Minimal();
        run.DistanceMeters = 0;

        Assert.DoesNotContain("Pace", RunSummary.Tiles(run).Select(t => t.Label));
    }

    [Fact]
    public void On_the_treadmill_the_pace_variation_says_where_the_speed_comes_from()
    {
        var run = Full();
        run.TypeKey = "treadmill_running";

        Assert.Equal(new SummaryTile("Pace variation", "±3.2%", "Speed from step counting"), RunSummary.Tiles(run).Single(t => t.Label == "Pace variation"));
    }

    [Fact]
    public void Without_pace_variation_there_is_no_tile() =>
        Assert.DoesNotContain(RunSummary.Tiles(Minimal()), t => t.Label == "Pace variation");
}
