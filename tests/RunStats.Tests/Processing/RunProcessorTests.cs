using System.Text.Json;
using RunStats.Data;
using RunStats.Garmin;
using RunStats.Processing;

namespace RunStats.Tests.Processing;

public class RunProcessorTests
{
    const string FullSummary = """
        {"activityId":7,"summaryDTO":{
          "distance":10012.5,"duration":2700.25,"movingDuration":2690.0,"elapsedDuration":2750.0,
          "averageSpeed":3.708,"averageMovingSpeed":3.72,"avgGradeAdjustedSpeed":3.75,"maxSpeed":5.1,
          "minHR":95.0,"averageHR":162.4,"maxHR":181.0,"averageRunCadence":172.5,"maxRunCadence":190.0,
          "strideLength":128.4,"groundContactTime":241.2,"verticalOscillation":8.9,"verticalRatio":6.9,
          "averagePower":310.0,"maxPower":520.0,"normalizedPower":318.0,
          "elevationGain":42.0,"elevationLoss":40.0,"minElevation":-2.4,"maxElevation":18.6,
          "trainingEffect":3.4,"anaerobicTrainingEffect":1.2,"activityTrainingLoad":145.7,
          "calories":720.0,"steps":7750,"averageTemperature":-3.5,
          "startLatitude":52.37,"startLongitude":-4.89,"endLatitude":52.38,"endLongitude":-4.9,
          "startTimeGMT":"2026-10-25T09:30:00.0","trainingEffectLabel":"AEROBIC_BASE"}}
        """;

    static RunRawData Raw(string detail = FullSummary, string list = """{"activityId":7,"vO2MaxValue":52.0}""") =>
        new() { DetailJson = detail, ListJson = list };

    [Fact]
    public void Summary_fills_the_runs_stats()
    {
        var run = new Run { Id = 7 };

        RunProcessor.ApplySummary(run, Raw());

        Assert.Equal((10012.5, 2700.25, 2690.0, 2750.0), (run.DistanceMeters, run.DurationSeconds, run.MovingDurationSeconds, run.ElapsedDurationSeconds));
        Assert.Equal((3.708, 3.72, 3.75, 5.1), (run.AverageSpeedMps, run.AverageMovingSpeedMps, run.GradeAdjustedSpeedMps, run.MaxSpeedMps));
        Assert.Equal(((short)95, (short)162, (short)181), (run.MinHeartRate!.Value, run.AverageHeartRate!.Value, run.MaxHeartRate!.Value));
        Assert.Equal((172.5, 190.0, 128.4), (run.AverageCadenceSpm, run.MaxCadenceSpm, run.StrideLengthCm));
        Assert.Equal((241.2, 8.9, 6.9), (run.GroundContactTimeMs, run.VerticalOscillationCm, run.VerticalRatioPercent));
        Assert.Equal((310.0, 520.0, 318.0), (run.AveragePowerWatts, run.MaxPowerWatts, run.NormalizedPowerWatts));
        Assert.Equal((42.0, 40.0, -2.4, 18.6), (run.ElevationGainMeters, run.ElevationLossMeters, run.MinElevationMeters, run.MaxElevationMeters));
        Assert.Equal((3.4, 1.2, 145.7), (run.AerobicTrainingEffect, run.AnaerobicTrainingEffect, run.TrainingLoad));
        Assert.Equal((720.0, 7750, -3.5), (run.Calories, run.Steps, run.AverageTemperatureCelsius));
        Assert.Equal((52.37, -4.89, 52.38, -4.9), (run.StartLatitude, run.StartLongitude, run.EndLatitude, run.EndLongitude));
        Assert.Equal(52.0, run.Vo2Max);
    }

    [Fact]
    public void Missing_stats_are_null_and_distance_and_duration_keep_the_list_values()
    {
        var run = new Run { Id = 7, DistanceMeters = 5000, DurationSeconds = 1500, AverageHeartRate = 150, Vo2Max = 50 };

        RunProcessor.ApplySummary(run, Raw("""{"summaryDTO":{"averageHR":null}}""", """{"activityId":7}"""));

        Assert.Equal((5000.0, 1500.0), (run.DistanceMeters, run.DurationSeconds));
        Assert.Null(run.AverageHeartRate);
        Assert.Null(run.StrideLengthCm);
        Assert.Null(run.Steps);
        Assert.Null(run.Vo2Max);
    }

    [Theory]
    [InlineData("""{"activityId":7}""")]
    [InlineData("""{"summaryDTO":null}""")]
    public void Detail_without_a_summary_is_an_error(string detail)
    {
        var error = Assert.Throws<FormatException>(() => RunProcessor.ApplySummary(new Run(), Raw(detail)));

        Assert.Equal("The activity detail has no summary.", error.Message);
    }

    [Fact]
    public void Broken_json_is_an_error()
    {
        Assert.ThrowsAny<JsonException>(() => RunProcessor.ApplySummary(new Run(), Raw("{")));
    }

    [Fact]
    public void Laps_come_from_the_splits()
    {
        const string splits = """
            {"activityId":7,"lapDTOs":[
              {"lapIndex":1,"startTimeGMT":"2026-10-25T09:30:00.0","intensityType":"ACTIVE","distance":1000.0,"duration":270.5,
               "averageHR":150.0,"maxHR":160.0,"averageRunCadence":170.0,"strideLength":120.0,"averagePower":300.0,"calories":70.0},
              {"lapIndex":2,"startTimeGMT":"2026-10-25T09:34:30.5","distance":512.3,"duration":140.0}
            ],"eventDTOs":[]}
            """;

        var laps = RunProcessor.Laps(7, splits);

        Assert.Equal(2, laps.Count);
        var first = laps[0];
        Assert.Equal((7L, 1, "ACTIVE"), (first.RunId, first.LapIndex, first.IntensityType));
        Assert.Equal(new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc), first.StartUtc);
        Assert.Equal(DateTimeKind.Utc, first.StartUtc.Kind);
        Assert.Equal((1000.0, 270.5, (short)150, (short)160), (first.DistanceMeters, first.DurationSeconds, first.AverageHeartRate!.Value, first.MaxHeartRate!.Value));
        Assert.Equal((170.0, 120.0, 300.0, 70.0), (first.AverageCadenceSpm, first.StrideLengthCm, first.AveragePowerWatts, first.Calories));
        Assert.Equal(new DateTime(2026, 10, 25, 9, 34, 30, 500, DateTimeKind.Utc), laps[1].StartUtc);
        Assert.Null(laps[1].IntensityType);
        Assert.Null(laps[1].AverageHeartRate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"activityId":7}""")]
    [InlineData("""{"lapDTOs":null}""")]
    [InlineData("""{"lapDTOs":[]}""")]
    public void No_splits_means_no_laps(string? splits) => Assert.Empty(RunProcessor.Laps(7, splits));

    [Theory]
    [InlineData("""{"lapDTOs":[{"startTimeGMT":"2026-10-25T09:30:00.0"}]}""", "A lap has no index.")]
    [InlineData("""{"lapDTOs":[{"lapIndex":1}]}""", "A lap has no start time.")]
    public void Lap_without_index_or_start_is_an_error(string splits, string message)
    {
        var error = Assert.Throws<FormatException>(() => RunProcessor.Laps(7, splits));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Long_intensity_type_is_shortened()
    {
        var splits = $$"""{"lapDTOs":[{"lapIndex":1,"startTimeGMT":"2026-10-25T09:30:00.0","intensityType":"{{new string('X', 80)}}"}]}""";

        Assert.Equal(50, RunProcessor.Laps(7, splits)[0].IntensityType!.Length);
    }

    [Fact]
    public void Samples_are_stored_in_their_compact_types()
    {
        var fit = new FitSample(12, 624_769_324, -58_342_310, 1234.56, 3.21, -1.6, 171, 172.5, 1149.6, 241.5, 89.2, 6.95, 310, -12);

        var sample = Assert.Single(RunProcessor.Samples(7, [fit]));

        Assert.Equal((7L, 12, 624_769_324, -58_342_310), (sample.RunId, sample.OffsetSeconds, sample.LatitudeSemicircles!.Value, sample.LongitudeSemicircles!.Value));
        Assert.Equal((1234.56f, 3.21f, -1.6f), (sample.DistanceMeters!.Value, sample.SpeedMps!.Value, sample.AltitudeMeters!.Value));
        Assert.Equal(((byte)171, 172.5f, (short)1150), (sample.HeartRate!.Value, sample.CadenceSpm!.Value, sample.StepLengthMm!.Value));
        Assert.Equal((241.5f, 89.2f, 6.95f), (sample.GroundContactTimeMs!.Value, sample.VerticalOscillationMm!.Value, sample.VerticalRatioPercent!.Value));
        Assert.Equal(((short)310, (short)-12), (sample.PowerWatts!.Value, sample.TemperatureCelsius!.Value));
    }

    [Fact]
    public void Missing_and_impossible_sample_values_are_null()
    {
        var fit = new FitSample(0, null, null, null, null, null, 300, null, 40000, null, null, null, -40000, null);

        var sample = Assert.Single(RunProcessor.Samples(7, [fit]));

        Assert.Null(sample.LatitudeSemicircles);
        Assert.Null(sample.DistanceMeters);
        Assert.Null(sample.HeartRate);
        Assert.Null(sample.StepLengthMm);
        Assert.Null(sample.PowerWatts);
        Assert.Null(sample.TemperatureCelsius);
    }
}
