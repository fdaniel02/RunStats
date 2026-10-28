using RunStats.Data;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class SamePaceTests
{
    static RunPaceBand Band(short pace, double minutes, float? step) =>
        new() { PaceSecondsPerKm = pace, Seconds = (int)(minutes * 60), FlatSeconds = (int)(minutes * 60), StepLengthMm = step };

    static Comparison? Compare(IEnumerable<RunPaceBand> recent, IEnumerable<RunPaceBand> baseline) =>
        SamePace.Compare(recent, baseline, b => b.StepLengthMm, 15, 5 * 60);

    [Fact]
    public void Before_and_now_are_weighted_by_the_recent_time_at_each_pace()
    {
        // 30 min at 5:30 (1000 → 1020 mm), 10 min at 6:00 (920 → 966 mm).
        var recent = new[] { Band(330, 30, 1020), Band(360, 10, 966) };
        var baseline = new[] { Band(335, 60, 1000), Band(360, 60, 920) };

        var comparison = Compare(recent, baseline)!;

        Assert.Equal((30 * 1000 + 10 * 920) / 40.0, comparison.Before, 6);
        Assert.Equal((30 * 1020 + 10 * 966) / 40.0, comparison.Now, 6);
        Assert.Equal(40 * 60, comparison.Seconds);
        Assert.Equal(comparison.Now - comparison.Before, comparison.Change, 6);
        Assert.Equal((comparison.Now / comparison.Before - 1) * 100, comparison.Percent, 6);
    }

    [Fact]
    public void A_different_mix_of_paces_is_no_change()
    {
        // Mostly fast recently, mostly slow before; at each pace the same.
        var recent = new[] { Band(300, 50, 1100), Band(360, 10, 950) };
        var baseline = new[] { Band(300, 10, 1100), Band(360, 50, 950) };

        Assert.Equal(0, Compare(recent, baseline)!.Change, 6);
    }

    [Fact]
    public void Paces_without_the_other_side_or_enough_time_are_left_out()
    {
        var recent = new[] { Band(330, 30, 1020), Band(240, 10, 1400), Band(360, 4, 1000) };
        var baseline = new[] { Band(330, 60, 1000), Band(360, 60, 920) };

        var comparison = Compare(recent, baseline)!;

        Assert.Equal((1000.0, 1020.0, 30 * 60.0), (comparison.Before, comparison.Now, comparison.Seconds));
    }

    [Fact]
    public void Bands_without_the_metric_dont_count() =>
        Assert.Null(Compare([Band(330, 30, null)], [Band(330, 60, 1000)]));

    [Fact]
    public void The_mean_is_over_the_flat_seconds()
    {
        RunPaceBand[] bands = [Band(330, 30, 1000), Band(335, 10, 1040), new RunPaceBand { PaceSecondsPerKm = 340, Seconds = 600, FlatSeconds = 0, StepLengthMm = 2000 }];

        Assert.Equal((1010, 2400), SamePace.Mean(bands, b => b.StepLengthMm));
    }
}
