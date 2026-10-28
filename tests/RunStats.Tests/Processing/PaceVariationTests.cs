using RunStats.Data;
using RunStats.Processing;

namespace RunStats.Tests.Processing;

public class PaceVariationTests
{
    static readonly DateTime Start = new(2026, 9, 22, 15, 0, 0, DateTimeKind.Utc);

    static List<RunSample> Seconds(int from, int count, Func<int, double> speed) =>
        [.. Enumerable.Range(from, count).Select(i => new RunSample { OffsetSeconds = i, SpeedMps = (float)speed(i) })];

    [Fact]
    public void An_even_pace_has_no_variation() =>
        Assert.Equal(0, PaceVariation.Percent(Seconds(0, 300, _ => 3.0))!.Value, 6);

    [Fact]
    public void Alternating_minutes_vary_by_their_spread()
    {
        // A minute at 2.7 m/s, a minute at 3.3: ±10% around 3.0, a little less after smoothing.
        var samples = Seconds(0, 600, i => i / 60 % 2 == 0 ? 2.7 : 3.3);

        Assert.InRange(PaceVariation.Percent(samples)!.Value, 9, 10);
    }

    [Fact]
    public void Jitter_from_second_to_second_is_smoothed_away()
    {
        var samples = Seconds(0, 300, i => i % 2 == 0 ? 2.7 : 3.3);

        Assert.True(PaceVariation.Percent(samples) < 1);
    }

    [Fact]
    public void Stops_are_left_out()
    {
        // Three minutes at an even pace with a minute standing at a crossing in the middle.
        var samples = Seconds(0, 90, _ => 3.0).Concat(Seconds(90, 60, _ => 0)).Concat(Seconds(150, 90, _ => 3.0)).ToList();

        Assert.True(PaceVariation.Percent(samples) < 6);
        Assert.True(PaceVariation.Percent(samples) < PaceVariation.Percent(Seconds(0, 90, _ => 3.0).Concat(Seconds(90, 60, _ => 2.3)).Concat(Seconds(150, 90, _ => 3.0)).ToList()));
    }

    [Fact]
    public void Less_than_a_minute_of_running_has_no_variation() =>
        Assert.Null(PaceVariation.Percent(Seconds(0, 59, _ => 3.0)));

    [Fact]
    public void Each_lap_gets_the_variation_of_its_own_seconds()
    {
        var run = new Run { StartUtc = Start };
        // An even warm-up, then 90 s fast and 90 s slow in one lap.
        var samples = Seconds(0, 300, _ => 2.8).Concat(Seconds(300, 180, i => i < 390 ? 4.2 : 2.8)).ToList();
        List<RunLap> laps =
        [
            new() { LapIndex = 2, StartUtc = Start.AddSeconds(300) },
            new() { LapIndex = 1, StartUtc = Start }
        ];

        PaceVariation.Apply(run, laps, samples);

        Assert.Equal(0, laps[1].PaceVariationPercent!.Value, 6);
        Assert.InRange(laps[0].PaceVariationPercent!.Value, 15, 20);
        Assert.InRange(run.PaceVariationPercent!.Value, 10, 20);
    }

    [Fact]
    public void A_lap_is_smoothed_without_the_seconds_of_the_lap_before()
    {
        // A slow jog, then a lap at an even fast pace: smoothed across the edge, its first seconds would look slow.
        var run = new Run { StartUtc = Start };
        var samples = Seconds(0, 120, _ => 2.0).Concat(Seconds(120, 90, _ => 4.5)).ToList();
        List<RunLap> laps = [new() { LapIndex = 1, StartUtc = Start }, new() { LapIndex = 2, StartUtc = Start.AddSeconds(120) }];

        PaceVariation.Apply(run, laps, samples);

        Assert.Equal(0, laps[1].PaceVariationPercent!.Value, 6);
    }

    [Fact]
    public void Without_samples_nothing_is_known()
    {
        var run = new Run { StartUtc = Start, PaceVariationPercent = 5 };
        List<RunLap> laps = [new() { LapIndex = 1, StartUtc = Start, PaceVariationPercent = 5 }];

        PaceVariation.Apply(run, laps, []);

        Assert.Null(run.PaceVariationPercent);
        Assert.Null(laps[0].PaceVariationPercent);
    }
}
