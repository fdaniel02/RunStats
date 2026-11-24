using RunStats.Features.Years;

namespace RunStats.Tests.Features;

public class GoalProgressTests
{
    [Fact]
    public void This_year_expects_the_goal_spread_over_the_days_so_far()
    {
        // 1 November 2026 is day 305 of 365; 60 days are left.
        var progress = GoalProgress.For(2026, 1_500_000, 1_200_000, new DateOnly(2026, 11, 1));

        Assert.Equal(1_500_000 * 305 / 365.0, progress.ExpectedMeters!.Value, 6);
        Assert.Equal(60, progress.DaysLeft);
        Assert.Equal(5_000, progress.DailyMeters!.Value, 6);
        Assert.False(progress.IsPast);
        Assert.False(progress.IsReached);
        Assert.Equal(0.8, progress.Fraction, 6);
    }

    [Fact]
    public void Weeks_split_what_was_left_on_monday_over_the_calendar_weeks_left()
    {
        // Sunday 1 November 2026: the week from 26 October is the first of 10, 67 days in all.
        // 320 km were left on Monday.
        var weeks = GoalProgress.For(2026, 1_500_000, 1_200_000, new DateOnly(2026, 11, 1), thisWeekMeters: 20_000).Weeks!;

        Assert.Equal(10, weeks.Count);
        Assert.Equal(320_000 * 7 / 67.0, weeks.FullWeekMeters, 6);
        Assert.Equal(7, weeks.ThisWeekDays);
        Assert.Equal(weeks.FullWeekMeters, weeks.ThisWeekMeters, 6);
        Assert.False(weeks.IsLast);
    }

    [Fact]
    public void A_short_last_week_needs_the_share_of_its_days()
    {
        var weeks = GoalProgress.For(2026, 1_500_000, 1_400_000, new DateOnly(2026, 12, 29), thisWeekMeters: 10_000).Weeks!;

        Assert.True(weeks.IsLast);
        Assert.Equal(4, weeks.ThisWeekDays);
        Assert.Equal(110_000, weeks.ThisWeekMeters, 6);
    }

    [Fact]
    public void A_week_that_started_last_year_counts_only_this_years_days_and_runs()
    {
        // Friday 1 January 2027: the week from 28 December 2026 has 3 days in 2027, and 25 km of which 5 km in 2027.
        var weeks = GoalProgress.For(2027, 1_460_000, 5_000, new DateOnly(2027, 1, 1), thisWeekMeters: 25_000).Weeks!;

        Assert.Equal(53, weeks.Count);
        Assert.Equal(28_000, weeks.FullWeekMeters, 6);
        Assert.Equal(3, weeks.ThisWeekDays);
        Assert.Equal(12_000, weeks.ThisWeekMeters, 6);
    }

    [Fact]
    public void Months_split_what_was_left_on_the_first_over_the_calendar_months_left()
    {
        var months = GoalProgress.For(2026, 1_500_000, 1_200_000, new DateOnly(2026, 11, 20), thisMonthMeters: 200_000).Months!;

        Assert.Equal(2, months.Count);
        Assert.Equal(250_000, months.MonthlyMeters, 6);
        Assert.False(months.IsLast);
    }

    [Fact]
    public void A_reached_goal_needs_no_more_weeks()
    {
        var progress = GoalProgress.For(2026, 1_000_000, 1_100_000, new DateOnly(2026, 11, 1));

        Assert.True(progress.IsReached);
        Assert.Null(progress.Weeks);
        Assert.Null(progress.DailyMeters);
        Assert.Null(progress.Months);
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
            Assert.Null(p.Weeks);
            Assert.Null(p.DailyMeters);
            Assert.Null(p.Months);
        });
    }

    [Fact]
    public void A_future_year_spreads_the_goal_over_all_its_weeks_and_ignores_this_weeks_runs()
    {
        // 2028 starts on a Saturday and ends on a Sunday.
        var progress = GoalProgress.For(2028, 1_464_000, 0, new DateOnly(2026, 11, 1), thisWeekMeters: 9_000, thisMonthMeters: 9_000);

        Assert.Equal(0, progress.ExpectedMeters);
        Assert.Equal(53, progress.Weeks!.Count);
        Assert.Equal(28_000, progress.Weeks.FullWeekMeters, 6);
        Assert.Equal(2, progress.Weeks.ThisWeekDays);
        Assert.Equal(8_000, progress.Weeks.ThisWeekMeters, 6);
        Assert.Equal(4_000, progress.DailyMeters!.Value, 6);
        Assert.Equal(12, progress.Months!.Count);
        Assert.Equal(122_000, progress.Months.MonthlyMeters, 6);
    }
}
