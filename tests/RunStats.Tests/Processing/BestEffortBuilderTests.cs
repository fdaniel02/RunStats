using RunStats.Data;
using RunStats.Processing;

namespace RunStats.Tests.Processing;

public class BestEffortBuilderTests
{
    // A sample a second from `from` on, the distance growing at the speed given for each part.
    static List<RunSample> Run(int from, double startMeters, params (int Seconds, double Speed)[] parts)
    {
        var samples = new List<RunSample>();
        var (offset, distance) = (from, startMeters);
        foreach (var (seconds, speed) in parts)
            for (var i = 0; i < seconds; i++)
            {
                samples.Add(new RunSample { OffsetSeconds = offset++, DistanceMeters = (float)distance });
                distance += speed;
            }
        samples.Add(new RunSample { OffsetSeconds = offset, DistanceMeters = (float)distance });
        return samples;
    }

    static Dictionary<int, double> Efforts(IEnumerable<RunSample> samples) =>
        BestEffortBuilder.Build(7, "running", samples).ToDictionary(e => e.TypeId, e => e.Seconds);

    [Fact]
    public void An_even_run_has_an_effort_for_each_distance_it_covers()
    {
        // 6 km at 3 m/s.
        var efforts = BestEffortBuilder.Build(7, "running", Run(0, 0, (2000, 3)));

        Assert.All(efforts, e => Assert.Equal(7, e.RunId));
        Assert.Equal([1, 2, 3], efforts.Select(e => e.TypeId));
        Assert.Equal(1000 / 3.0, efforts[0].Seconds, 3);
        Assert.Equal(1609.344 / 3, efforts[1].Seconds, 3);
        Assert.Equal(5000 / 3.0, efforts[2].Seconds, 3);
    }

    [Fact]
    public void The_fastest_stretch_can_be_anywhere_in_the_run()
    {
        // A slow kilometre, a fast one at 4 m/s, a slow one again.
        var efforts = Efforts(Run(0, 0, (400, 2.5), (250, 4), (400, 2.5)));

        Assert.Equal(250, efforts[1], 3);
    }

    [Fact]
    public void The_start_of_a_stretch_is_interpolated_between_samples()
    {
        // A sample every 4 s at 3 m/s: no two samples are exactly 1 km apart.
        var samples = Enumerable.Range(0, 100).Select(i => new RunSample { OffsetSeconds = 4 * i, DistanceMeters = 12f * i });

        Assert.Equal(1000 / 3.0, Efforts(samples)[1], 3);
    }

    [Fact]
    public void Pauses_count_towards_the_time()
    {
        // 600 m, a minute standing still with the watch paused (no samples), another 600 m.
        var samples = Run(0, 0, (200, 3)).Concat(Run(260, 600, (200, 3)));

        Assert.Equal(1000 / 3.0 + 60, Efforts(samples)[1], 3);
    }

    [Fact]
    public void A_gps_jump_is_not_a_record()
    {
        // 600 m, a 600 m jump within a second, then 600 m: 1.2 km run, not 1.8.
        var samples = Run(0, 0, (200, 3)).Concat(Run(201, 1200, (200, 3)));

        var efforts = Efforts(samples);
        Assert.Equal(1000 / 3.0 + 1, efforts[1], 3);
        Assert.False(efforts.ContainsKey(2));
    }

    [Fact]
    public void A_point_where_the_distance_goes_back_is_left_out()
    {
        var samples = Run(0, 0, (400, 3));
        samples[200].DistanceMeters -= 50;

        Assert.Equal(1000 / 3.0, Efforts(samples)[1], 3);
    }

    [Fact]
    public void Samples_are_used_in_time_order()
    {
        var samples = Run(0, 0, (400, 3));
        samples.Reverse();

        Assert.Equal(1000 / 3.0, Efforts(samples)[1], 3);
    }

    [Fact]
    public void A_run_shorter_than_a_kilometre_or_without_distance_has_none()
    {
        Assert.Empty(BestEffortBuilder.Build(7, "running", Run(0, 0, (333, 3))));
        Assert.Empty(BestEffortBuilder.Build(7, "running", Enumerable.Range(0, 600).Select(i => new RunSample { OffsetSeconds = i, SpeedMps = 3 })));
    }

    [Fact]
    public void A_treadmill_run_has_none()
    {
        Assert.Empty(BestEffortBuilder.Build(7, "treadmill_running", Run(0, 0, (2000, 3))));
        Assert.NotEmpty(BestEffortBuilder.Build(7, "trail_running", Run(0, 0, (2000, 3))));
    }
}
