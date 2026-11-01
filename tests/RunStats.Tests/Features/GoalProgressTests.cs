using RunStats.Features.Years;

namespace RunStats.Tests.Features;

public class GoalProgressTests
{
    [Fact]
    public void This_year_expects_the_goal_spread_over_the_days_so_far()
    {
        // 1 November 2026 is day 305 of 365; 60 days, 8.6 weeks, are left.
        var progress = GoalProgress.For(2026, 1_500_000, 1_200_000, new DateOnly(2026, 11, 1));

        Assert.Equal(1_500_000 * 305 / 365.0, progress.ExpectedMeters!.Value, 6);
        Assert.Equal(60 / 7.0, progress.WeeksLeft!.Value, 6);
        Assert.Equal(300_000 / (60 / 7.0), progress.WeeklyMeters!.Value, 6);
        Assert.False(progress.IsPast);
        Assert.False(progress.IsReached);
        Assert.Equal(0.8, progress.Fraction, 6);
    }

    [Fact]
    public void A_reached_goal_needs_no_more_weeks()
    {
        var progress = GoalProgress.For(2026, 1_000_000, 1_100_000, new DateOnly(2026, 11, 1));

        Assert.True(progress.IsReached);
        Assert.Null(progress.WeeklyMeters);
        Assert.Equal(1, progress.Fraction);
    }

    [Fact]
    public void On_the_last_day_and_after_the_year_is_over()
    {
        var lastDay = GoalProgress.For(2026, 1_500_000, 1_400_000, new DateOnly(2026, 12, 31));
        var past = GoalProgress.For(2025, 1_500_000, 1_400_000, new DateOnly(2026, 11, 1));

        Assert.All([lastDay, past], p =>
        {
            Assert.True(p.IsPast);
            Assert.Null(p.ExpectedMeters);
            Assert.Null(p.WeeklyMeters);
        });
    }

    [Fact]
    public void A_future_year_spreads_the_goal_over_all_its_weeks()
    {
        var progress = GoalProgress.For(2028, 1_464_000, 0, new DateOnly(2026, 11, 1));

        Assert.Equal(0, progress.ExpectedMeters);
        Assert.Equal(366 / 7.0, progress.WeeksLeft!.Value, 6);
        Assert.Equal(1_464_000 / (366 / 7.0), progress.WeeklyMeters!.Value, 6);
    }
}
