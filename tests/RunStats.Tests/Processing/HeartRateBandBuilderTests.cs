using RunStats.Data;
using RunStats.Processing;

namespace RunStats.Tests.Processing;

public class HeartRateBandBuilderTests
{
    const int WarmUp = HeartRateBandBuilder.WarmUpSeconds;

    static RunSample Second(int offset, byte? heartRate = 152, double speed = 3.0, double altitude = 10) => new()
    {
        OffsetSeconds = offset,
        SpeedMps = (float)speed,
        DistanceMeters = (float)(offset * speed),
        AltitudeMeters = (float)altitude,
        HeartRate = heartRate
    };

    static List<RunSample> Steady(int from, int seconds, byte? heartRate = 152, double speed = 3.0) =>
        [.. Enumerable.Range(from, seconds).Select(i => Second(i, heartRate, speed))];

    [Fact]
    public void Seconds_after_the_warm_up_count_with_their_distance()
    {
        var band = Assert.Single(HeartRateBandBuilder.Build(7, "running", Steady(0, WarmUp + 100)));

        Assert.Equal((7L, (short)150, 100), (band.RunId, band.HeartRate, band.Seconds));
        Assert.Equal(300, band.DistanceMeters, 3);
    }

    [Fact]
    public void Bands_are_five_bpm_wide_and_in_heart_rate_order()
    {
        var samples = Steady(0, WarmUp).Concat(Steady(WarmUp, 50, heartRate: 159)).Concat(Steady(WarmUp + 50, 30, heartRate: 145)).ToList();

        var bands = HeartRateBandBuilder.Build(7, "running", samples);

        Assert.Equal([((short)145, 30), ((short)155, 50)], bands.Select(b => (b.HeartRate, b.Seconds)));
    }

    [Fact]
    public void Seconds_without_heart_rate_or_with_a_sensor_error_are_left_out()
    {
        var samples = Steady(0, WarmUp + 10).Concat(Steady(WarmUp + 10, 10, heartRate: null)).Concat(Steady(WarmUp + 20, 10, heartRate: 0)).ToList();

        Assert.Equal(10, Assert.Single(HeartRateBandBuilder.Build(7, "running", samples)).Seconds);
    }

    [Fact]
    public void Walking_and_standing_are_left_out()
    {
        // 1 m/s is 16:40/km.
        var samples = Steady(0, WarmUp + 10).Concat(Steady(WarmUp + 10, 60, speed: 1)).ToList();

        var band = Assert.Single(HeartRateBandBuilder.Build(7, "running", samples));

        // Smoothing blends the seconds around the change into the walk, so up to the 10 at running pace count.
        Assert.InRange(band.Seconds, 5, 10);
    }

    [Fact]
    public void Steep_seconds_are_left_out()
    {
        // 10% up after the warm-up.
        var samples = Steady(0, WarmUp)
            .Concat(Enumerable.Range(WarmUp, 120).Select(i => Second(i, altitude: 10 + (i - WarmUp) * 0.3)))
            .ToList();

        var bands = HeartRateBandBuilder.Build(7, "running", samples);

        // Only the first metres of the climb, before the slope is measured over enough distance.
        Assert.True(bands.Sum(b => b.Seconds) < 20);
    }

    [Fact]
    public void Treadmill_runs_have_no_bands() =>
        Assert.Empty(HeartRateBandBuilder.Build(7, "treadmill_running", Steady(0, WarmUp + 100)));

    [Fact]
    public void Short_runs_have_no_bands() =>
        Assert.Empty(HeartRateBandBuilder.Build(7, "running", Steady(0, WarmUp)));
}
