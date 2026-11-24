using RunStats.Features.Dashboard;

namespace RunStats.Features.Years;

// ExpectedMeters is the goal spread evenly over the year's days, today included; null for a past year. Weeks and Months
// split what is left of the goal at the start of the current week or month over the calendar weeks or months left, and
// Daily over the days left; all null once the goal is reached or the year is over.
public sealed record GoalProgress(int Year, double GoalMeters, double DoneMeters, double? ExpectedMeters, int? DaysLeft,
    GoalWeeks? Weeks, GoalMonths? Months, double? DailyMeters)
{
    public bool IsPast => ExpectedMeters is null;
    public bool IsReached => DoneMeters >= GoalMeters;
    public double Fraction => Math.Clamp(DoneMeters / GoalMeters, 0, 1);
    public double? ExpectedFraction => ExpectedMeters / GoalMeters;

    public static GoalProgress For(int year, double goalMeters, double doneMeters, DateOnly today,
        double thisWeekMeters = 0, double thisMonthMeters = 0)
    {
        var days = DateTime.IsLeapYear(year) ? 366 : 365;
        var daysDone = DaysDone(year, days, today);
        if (daysDone == days)
            return new GoalProgress(year, goalMeters, doneMeters, null, null, null, null, null);
        var daysLeft = days - daysDone;
        var expected = goalMeters * daysDone / days;
        if (doneMeters >= goalMeters)
            return new GoalProgress(year, goalMeters, doneMeters, expected, daysLeft, null, null, null);
        var from = year == today.Year ? today : new DateOnly(year, 1, 1);
        return new GoalProgress(year, goalMeters, doneMeters, expected, daysLeft,
            GoalWeeks.For(goalMeters, doneMeters, from, year == today.Year ? thisWeekMeters : 0),
            GoalMonths.For(goalMeters, doneMeters, from, year == today.Year ? thisMonthMeters : 0),
            (goalMeters - doneMeters) / daysLeft);
    }

    static int DaysDone(int year, int days, DateOnly today)
    {
        if (year < today.Year)
            return days;
        return year > today.Year ? 0 : today.DayOfYear;
    }
}

// A short week at either end of the year gets the share of its days, so FullWeekMeters is what a 7-day week needs.
public sealed record GoalWeeks(double FullWeekMeters, int Count, int ThisWeekDays, double ThisWeekMeters)
{
    public bool IsLast => Count == 1;

    public static GoalWeeks For(double goalMeters, double doneMeters, DateOnly today, double thisWeekMeters)
    {
        var newYear = new DateOnly(today.Year, 1, 1);
        var lastDay = new DateOnly(today.Year, 12, 31);
        var monday = DashboardQueries.Monday(today);
        // In a week that started last year, only this year's runs count, and those are all of doneMeters.
        var thisWeekInYear = monday < newYear ? doneMeters : thisWeekMeters;
        var start = monday < newYear ? newYear : monday;
        var sunday = monday.AddDays(6);
        var thisWeekDays = (sunday > lastDay ? lastDay : sunday).DayNumber - start.DayNumber + 1;
        var daysLeft = lastDay.DayNumber - start.DayNumber + 1;
        var still = goalMeters - (doneMeters - thisWeekInYear);
        var count = (DashboardQueries.Monday(lastDay).DayNumber - monday.DayNumber) / 7 + 1;
        return new GoalWeeks(still * 7 / daysLeft, count, thisWeekDays, still * thisWeekDays / daysLeft);
    }
}

public sealed record GoalMonths(double MonthlyMeters, int Count)
{
    public bool IsLast => Count == 1;

    public static GoalMonths For(double goalMeters, double doneMeters, DateOnly today, double thisMonthMeters)
    {
        var count = 12 - today.Month + 1;
        return new GoalMonths((goalMeters - (doneMeters - thisMonthMeters)) / count, count);
    }
}
