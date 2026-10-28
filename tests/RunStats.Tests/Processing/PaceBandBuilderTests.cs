using RunStats.Data;
using RunStats.Processing;

namespace RunStats.Tests.Processing;

public class PaceBandBuilderTests
{
    // 3 m/s is 5:33/km, in the 5:30 band.
    static RunSample Second(int offset, double speed = 3.0, double? altitude = 10, float? cadence = 172) => new()
    {
        OffsetSeconds = offset,
        SpeedMps = (float)speed,
        DistanceMeters = (float)(offset * speed),
        AltitudeMeters = (float?)altitude,
        CadenceSpm = cadence,
        StepLengthMm = 1046,
        GroundContactTimeMs = 254,
        VerticalOscillationMm = 86,
        VerticalRatioPercent = 8.2f,
        HeartRate = 156,
        PowerWatts = 300,
        GroundContactBalanceLeftPercent = 50.3f
    };

    static List<RunSample> Steady(int seconds, double speed = 3.0, int from = 0) =>
        [.. Enumerable.Range(from, seconds).Select(i => Second(i, speed))];

    [Fact]
    public void Steady_run_is_one_band_with_its_averages()
    {
        var band = Assert.Single(PaceBandBuilder.Build(7, Steady(300)));

        Assert.Equal((7L, (short)330, 300, 300), (band.RunId, band.PaceSecondsPerKm, band.Seconds, band.FlatSeconds));
        Assert.Equal((172f, 1046f, 254f, 86f, 8.2f), (band.CadenceSpm!.Value, band.StepLengthMm!.Value, band.GroundContactTimeMs!.Value,
            band.VerticalOscillationMm!.Value, band.VerticalRatioPercent!.Value));
        Assert.Equal((156f, 300f, 50.3f), (band.HeartRate!.Value, band.PowerWatts!.Value, band.GroundContactBalanceLeftPercent!.Value));
    }

    [Fact]
    public void Bands_are_five_seconds_per_km_wide_and_in_pace_order()
    {
        // 4:58/km, then 5:02/km.
        var samples = Steady(100, 1000 / 298.0).Concat(Steady(100, 1000 / 302.0, from: 100)).ToList();

        var bands = PaceBandBuilder.Build(7, samples);

        Assert.Equal([(short)295, (short)300], bands.Select(b => b.PaceSecondsPerKm));
        Assert.Equal(200, bands.Sum(b => b.Seconds));
    }

    [Fact]
    public void Speed_is_smoothed_so_gps_jitter_stays_in_its_band()
    {
        var samples = Enumerable.Range(0, 120).Select(i => Second(i, i % 2 == 0 ? 2.7 : 3.3)).ToList();

        var bands = PaceBandBuilder.Build(7, samples);

        // Only the first and last seconds, with half a window, can land next to it.
        Assert.True(bands.Single(b => b.PaceSecondsPerKm == 330).Seconds >= 110);
        Assert.Equal(120, bands.Sum(b => b.Seconds));
    }

    [Theory]
    [InlineData(2.0)] // 8:20/km, walking
    [InlineData(0)] // standing
    [InlineData(7.0)] // 2:23/km, a GPS glitch
    public void Walking_stops_and_glitches_are_left_out(double speed) =>
        Assert.Empty(PaceBandBuilder.Build(7, Steady(60, speed)));

    [Fact]
    public void Seconds_without_speed_are_left_out()
    {
        var samples = Steady(60);
        samples[30].SpeedMps = null;

        Assert.Equal(59, Assert.Single(PaceBandBuilder.Build(7, samples)).Seconds);
    }

    [Fact]
    public void Steep_seconds_count_for_the_pace_but_not_for_the_form()
    {
        // 5% up after the first 300 m, so from 350 m on the slope over the last 50 m is steep.
        var samples = Steady(200);
        foreach (var s in samples)
            s.AltitudeMeters = Math.Max(0, s.DistanceMeters!.Value - 300) * 0.05f;
        // Steep seconds with a different cadence must not move the average.
        foreach (var s in samples.Where(s => s.DistanceMeters >= 350))
            s.CadenceSpm = 150;

        var band = Assert.Single(PaceBandBuilder.Build(7, samples));

        Assert.Equal(200, band.Seconds);
        Assert.InRange(band.FlatSeconds, 110, 120);
        Assert.Equal(172f, band.CadenceSpm);
    }

    [Fact]
    public void Gentle_slopes_are_flat()
    {
        var samples = Steady(200);
        foreach (var s in samples)
            s.AltitudeMeters = s.DistanceMeters!.Value * 0.03f;

        Assert.Equal(200, Assert.Single(PaceBandBuilder.Build(7, samples)).FlatSeconds);
    }

    [Fact]
    public void Without_altitude_every_second_is_flat()
    {
        var samples = Enumerable.Range(0, 60).Select(i => Second(i, altitude: null)).ToList();

        Assert.Equal(60, Assert.Single(PaceBandBuilder.Build(7, samples)).FlatSeconds);
    }

    [Fact]
    public void Sensor_errors_are_left_out_of_the_averages()
    {
        var samples = Steady(60);
        samples[10].CadenceSpm = 0;
        samples[11].CadenceSpm = 400;
        samples[12].CadenceSpm = 180;
        samples[13].GroundContactBalanceLeftPercent = 100;

        var band = Assert.Single(PaceBandBuilder.Build(7, samples));

        Assert.Equal(172 + 8.0 / 58, band.CadenceSpm!.Value, 3);
        Assert.Equal(50.3f, band.GroundContactBalanceLeftPercent);
        Assert.Equal(60, band.Seconds);
    }

    [Fact]
    public void Metrics_the_run_has_no_values_for_are_null()
    {
        var samples = Steady(60);
        foreach (var s in samples)
            (s.GroundContactBalanceLeftPercent, s.PowerWatts) = (null, null);

        var band = Assert.Single(PaceBandBuilder.Build(7, samples));

        Assert.Null(band.GroundContactBalanceLeftPercent);
        Assert.Null(band.PowerWatts);
        Assert.Equal(172f, band.CadenceSpm);
    }

    [Fact]
    public void Samples_are_sorted_by_time_first()
    {
        var samples = Steady(60, 1000 / 298.0).Concat(Steady(60, 1000 / 362.0, from: 60)).Reverse().ToList();

        var paces = PaceBandBuilder.Build(7, samples).Select(b => b.PaceSecondsPerKm).ToList();

        // Smoothing puts the few seconds of the change in the bands between.
        Assert.Equal(((short)295, (short)360), (paces[0], paces[^1]));
        Assert.Equal(paces.Order(), paces);
    }

    [Fact]
    public void No_samples_no_bands() => Assert.Empty(PaceBandBuilder.Build(7, []));
}
