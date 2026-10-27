using RunStats.Shared;

namespace RunStats.Tests.Shared;

public class ChartScaleTests
{
    [Theory]
    [InlineData(33.1, 40, 10)]
    [InlineData(3.2, 4, 1)]
    [InlineData(40, 40, 10)]
    [InlineData(41, 60, 20)]
    [InlineData(7, 8, 2)]
    [InlineData(0.6, 0.6, 0.2)]
    [InlineData(150, 150, 50)]
    public void Axis_ends_on_a_round_step_above_the_largest_value(double largest, double max, double step)
    {
        var (actualMax, actualStep) = ChartScale.Nice(largest);

        Assert.Equal(max, actualMax, 6);
        Assert.Equal(step, actualStep, 6);
        Assert.True(actualMax / actualStep <= ChartScale.MaxIntervals + 1e-9);
    }

    [Fact]
    public void Nothing_to_show_still_gives_an_axis() => Assert.Equal((1.0, 1.0), ChartScale.Nice(0));

    [Fact]
    public void Ticks_run_from_zero_to_the_max() => Assert.Equal([0, 10, 20, 30, 40], ChartScale.Ticks(40, 10));

    [Fact]
    public void Decimal_ticks_stay_round() => Assert.Equal([0, 0.2, 0.4, 0.6], ChartScale.Ticks(0.6, 0.2));

    [Theory]
    [InlineData(152, 181, 150, 190, 10)]
    [InlineData(0.93, 1.18, 0.9, 1.2, 0.1)]
    [InlineData(2, 7, 2, 8, 2)]
    public void Range_rounds_around_the_values(double lowest, double highest, double min, double max, double step)
    {
        var (actualMin, actualMax, actualStep) = ChartScale.Range(lowest, highest);

        Assert.Equal((min, max, step), (Math.Round(actualMin, 6), Math.Round(actualMax, 6), Math.Round(actualStep, 6)));
    }

    [Fact]
    public void Range_uses_given_steps_when_one_fits()
    {
        Assert.Equal((285d, 360d, 15d), ChartScale.Range(290, 347, [15, 30, 60]));
    }

    [Fact]
    public void Range_falls_back_to_round_steps_past_the_given_ones()
    {
        Assert.Equal((0d, 1000d, 500d), ChartScale.Range(10, 990, [15, 30, 60]));
    }

    [Fact]
    public void A_flat_range_still_has_room()
    {
        Assert.Equal((4d, 6d, 0.5), ChartScale.Range(5, 5));
    }
}
