using RunStats.Data;
using RunStats.Features.Runs;

namespace RunStats.Tests.Features;

public class RunChartDataTests
{
    const int Semicircles = 600_000_000; // ≈ 50.29°

    // One sample per second at a steady speed, unless a test says otherwise.
    static List<RunSample> Steady(int seconds, double speed = 3, byte heartRate = 150, float cadence = 170, short stride = 1050, float altitude = 5)
    {
        var samples = new List<RunSample>();
        double distance = 0;
        for (var i = 0; i < seconds; i++)
        {
            samples.Add(new RunSample
            {
                OffsetSeconds = i,
                DistanceMeters = (float)distance,
                SpeedMps = (float)speed,
                HeartRate = heartRate,
                CadenceSpm = cadence,
                StepLengthMm = stride,
                AltitudeMeters = altitude,
                LatitudeSemicircles = Semicircles + i,
                LongitudeSemicircles = Semicircles / 10
            });
            distance += speed;
        }
        return samples;
    }

    [Fact]
    public void A_steady_run_gives_its_values_along_the_distance()
    {
        var points = RunChartData.Points(Steady(1000), hasRoute: true, maxPoints: 100);

        // The first of the 100 stretches lies within the left-out start.
        Assert.Equal(99, points.Count);
        Assert.Equal(points.OrderBy(p => p.Km).Select(p => p.Km), points.Select(p => p.Km));
        Assert.Equal(points.OrderBy(p => p.Seconds).Select(p => p.Seconds), points.Select(p => p.Seconds));
        Assert.InRange(points[^1].Km, 2.95, 3.0);
        Assert.All(points, p =>
        {
            Assert.Equal(1000 / 3.0, p.PaceSecondsPerKm!.Value, 3);
            Assert.Equal((150d, 5d), (p.HeartRate, p.ElevationMeters));
            Assert.Equal(170, p.CadenceSpm!.Value, 3);
            Assert.Equal(1.05, p.StrideMeters!.Value, 3);
        });
    }

    [Fact]
    public void Positions_are_in_degrees()
    {
        var first = RunChartData.Points(Steady(100), hasRoute: true, maxPoints: 10)[0];

        Assert.Equal(50.29, first.Latitude!.Value, 2);
        Assert.Equal(5.03, first.Longitude!.Value, 2);
    }

    [Fact]
    public void A_short_run_has_a_point_per_sample_at_most()
    {
        Assert.Equal(20, RunChartData.Points(Steady(30), hasRoute: true).Count);
    }

    [Fact]
    public void The_first_ten_seconds_are_left_out_of_every_chart()
    {
        var samples = Steady(100);
        for (var i = 0; i < RunChartData.StartSkipSeconds; i++)
        {
            samples[i].SpeedMps = 0.5f;
            samples[i].CadenceSpm = 60;
            samples[i].HeartRate = 60;
        }

        var points = RunChartData.Points(samples, hasRoute: true, maxPoints: 1000);

        Assert.Equal(90, points.Count);
        Assert.Equal((0.03, 10d), (Math.Round(points[0].Km, 3), points[0].Seconds));
        Assert.All(points, p => Assert.Equal((150d, 170d), (p.HeartRate, Math.Round(p.CadenceSpm!.Value, 3))));
    }

    [Fact]
    public void The_start_is_the_first_sample_not_offset_zero()
    {
        var samples = Steady(100);
        foreach (var sample in samples)
            sample.OffsetSeconds += 5;

        Assert.Equal(15, RunChartData.Points(samples, hasRoute: true, maxPoints: 1000)[0].Seconds);
    }

    [Fact]
    public void Each_point_has_the_time_since_the_start()
    {
        var points = RunChartData.Points(Steady(1000), hasRoute: true, maxPoints: 99);

        Assert.All(points, p => Assert.Equal(p.Km * 1000 / 3, p.Seconds, 3));
    }

    [Fact]
    public void Elevation_is_smoothed_over_half_a_minute()
    {
        var samples = Steady(600);
        for (var i = 0; i < samples.Count; i++)
            samples[i].AltitudeMeters = i % 2 == 0 ? 4.5f : 5.5f;

        var elevations = RunChartData.Points(samples, hasRoute: true, maxPoints: 1000).Select(p => p.ElevationMeters!.Value).ToList();

        Assert.All(elevations, e => Assert.InRange(e, 4.95, 5.05));
    }

    [Fact]
    public void The_pace_follows_the_watchs_speed_without_smoothing()
    {
        var samples = Steady(600);
        samples[300].SpeedMps = 4;

        var points = RunChartData.Points(samples, hasRoute: true, maxPoints: 600);

        Assert.Equal(250, points.Single(p => Math.Abs(p.Km - 0.9) < 1e-6).PaceSecondsPerKm!.Value, 3);
    }

    [Fact]
    public void A_one_second_cadence_spike_is_smoothed_away()
    {
        var samples = Steady(600);
        samples[300].CadenceSpm = 320;

        var cadences = RunChartData.Points(samples, hasRoute: true, maxPoints: 600).Select(p => p.CadenceSpm!.Value).ToList();

        // Spread over 15 seconds: 180 at most instead of 320.
        Assert.InRange(cadences.Max(), 179, 180.1);
    }

    [Fact]
    public void Standing_still_does_not_count_toward_the_pace()
    {
        var samples = Steady(300);
        for (var i = 100; i < 160; i++)
        {
            samples[i].SpeedMps = 0;
            samples[i].DistanceMeters = samples[99].DistanceMeters;
        }
        for (var i = 160; i < 300; i++)
            samples[i].DistanceMeters -= 60 * 3;

        var points = RunChartData.Points(samples, hasRoute: true, maxPoints: 1000);

        // On a distance axis the stop is one point; its pace comes only from the seconds around it that moved.
        Assert.All(points, p => Assert.InRange(p.PaceSecondsPerKm!.Value, 1000 / 3.0 - 0.01, 1000 / RunChartData.MovingSpeedMps));
    }

    [Fact]
    public void Walking_slower_than_the_moving_speed_leaves_a_gap_in_the_pace()
    {
        var samples = Steady(400);
        for (var i = 100; i < 400; i++)
        {
            var walking = i < 220;
            samples[i].SpeedMps = walking ? 0.5f : 3;
            samples[i].DistanceMeters = samples[i - 1].DistanceMeters + samples[i - 1].SpeedMps;
        }

        var walk = RunChartData.Points(samples, hasRoute: true, maxPoints: 1000)
            .Where(p => p.Km > 0.31 && p.Km < 0.35).ToList();

        Assert.NotEmpty(walk);
        Assert.All(walk, p => Assert.Null(p.PaceSecondsPerKm));
        Assert.All(walk, p => Assert.NotNull(p.HeartRate));
    }

    [Fact]
    public void A_stop_does_not_pull_the_cadence_down_around_it()
    {
        var samples = Steady(300);
        for (var i = 100; i < 105; i++)
            samples[i].CadenceSpm = 0;

        Assert.All(RunChartData.Points(samples, hasRoute: true, maxPoints: 1000), p => Assert.Equal(170, p.CadenceSpm!.Value, 3));
    }

    [Fact]
    public void Without_a_route_there_is_no_elevation()
    {
        Assert.All(RunChartData.Points(Steady(100), hasRoute: false), p => Assert.Null(p.ElevationMeters));
    }

    [Fact]
    public void Missing_sensors_leave_their_values_empty()
    {
        var samples = Steady(100);
        foreach (var sample in samples)
        {
            sample.HeartRate = null;
            sample.StepLengthMm = null;
        }

        Assert.All(RunChartData.Points(samples, hasRoute: true), p => Assert.Equal((null, null), (p.HeartRate, p.StrideMeters)));
    }

    [Fact]
    public void Samples_without_a_distance_are_left_out()
    {
        var samples = Steady(100);
        samples.Insert(20, new RunSample { OffsetSeconds = 20, HeartRate = 60 });

        Assert.Equal(150, RunChartData.Points(samples, hasRoute: true)[0].HeartRate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Too_few_samples_give_no_points(int count)
    {
        Assert.Empty(RunChartData.Points(Steady(count), hasRoute: true));
    }

    [Fact]
    public void A_run_that_never_moved_gives_no_points()
    {
        Assert.Empty(RunChartData.Points(Steady(100, speed: 0), hasRoute: true));
    }

    [Fact]
    public void Samples_are_taken_in_time_order()
    {
        var samples = Steady(100);
        samples.Reverse();

        Assert.Equal(RunChartData.Points(Steady(100), hasRoute: true), RunChartData.Points(samples, hasRoute: true));
    }

    [Fact]
    public void The_balance_is_smoothed_and_empty_without_the_strap()
    {
        var samples = Steady(120);
        foreach (var s in samples)
            s.GroundContactBalanceLeftPercent = s.OffsetSeconds == 60 ? 70 : 50.4f;

        var points = RunChartData.Points(samples, hasRoute: true);

        Assert.All(points, p => Assert.InRange(p.BalanceLeftPercent!.Value, 50.3, 52));
        Assert.All(RunChartData.Points(Steady(120), hasRoute: true), p => Assert.Null(p.BalanceLeftPercent));
    }
}
