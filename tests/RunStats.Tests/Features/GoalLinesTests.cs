using RunStats.Features.Years;

namespace RunStats.Tests.Features;

public class GoalLinesTests
{
    [Fact]
    public void Weeks_and_months_give_the_share_of_each_one_left()
    {
        // Friday 20 November 2026: the week from 16 November is the first of 7, 46 days in all, the last one Monday to Thursday.
        var progress = GoalProgress.For(2026, 1_500_000, 1_200_000, new DateOnly(2026, 11, 20), thisWeekMeters: 60_000, thisMonthMeters: 400_000);

        Assert.Equal("54.8 km a week for the remaining 7 weeks", GoalLines.Weekly(progress));
        Assert.Equal("350.0 km a month for the remaining 2 months", GoalLines.Monthly(progress));
    }

    [Fact]
    public void The_short_last_week_and_the_last_month_say_so()
    {
        var progress = GoalProgress.For(2026, 1_500_000, 1_400_000, new DateOnly(2026, 12, 29), thisWeekMeters: 10_000, thisMonthMeters: 80_000);

        Assert.Equal("110.0 km for the last 4 days", GoalLines.Weekly(progress));
        Assert.Equal("180.0 km for the last month", GoalLines.Monthly(progress));
    }

    [Fact]
    public void A_full_last_week_is_the_last_week()
    {
        // 31 December 2028 is a Sunday.
        var progress = GoalProgress.For(2028, 1_500_000, 1_430_000, new DateOnly(2028, 12, 26));

        Assert.Equal("70.0 km for the last week", GoalLines.Weekly(progress));
    }

    [Fact]
    public void A_short_first_week_gives_its_own_share()
    {
        var progress = GoalProgress.For(2027, 1_460_000, 5_000, new DateOnly(2027, 1, 1), thisWeekMeters: 25_000);

        Assert.Equal("28.0 km a week for the remaining 53 weeks (this one 12.0 km for 3 days)", GoalLines.Weekly(progress));
    }

    [Fact]
    public void A_reached_goal_has_no_lines()
    {
        var progress = GoalProgress.For(2026, 1_000_000, 1_100_000, new DateOnly(2026, 11, 1));

        Assert.Null(GoalLines.Weekly(progress));
        Assert.Null(GoalLines.Monthly(progress));
    }
}
