namespace RunStats.Features.Years;

// ExpectedMeters is the goal spread evenly over the year's days, today included; null for a past year. Weekly, Monthly
// and Daily are what each remaining week, month and day still need; null once the goal is reached or the year is over.
public sealed record GoalProgress(int Year, double GoalMeters, double DoneMeters, double? ExpectedMeters, int? DaysLeft, double? MonthsLeft,
    double? WeeklyMeters, double? MonthlyMeters, double? DailyMeters)
{
    public double? WeeksLeft => DaysLeft / 7.0;
    public bool IsPast => ExpectedMeters is null;
    public bool IsReached => DoneMeters >= GoalMeters;
    public double Fraction => Math.Clamp(DoneMeters / GoalMeters, 0, 1);
    public double? ExpectedFraction => ExpectedMeters / GoalMeters;

    public static GoalProgress For(int year, double goalMeters, double doneMeters, DateOnly today)
    {
        var days = DateTime.IsLeapYear(year) ? 366 : 365;
        var daysDone = DaysDone(year, days, today);
        if (daysDone == days)
            return new GoalProgress(year, goalMeters, doneMeters, null, null, null, null, null, null);
        var daysLeft = days - daysDone;
        var monthsLeft = daysLeft * 12.0 / days;
        var expected = goalMeters * daysDone / days;
        var still = goalMeters - doneMeters;
        return still > 0
            ? new GoalProgress(year, goalMeters, doneMeters, expected, daysLeft, monthsLeft, still / (daysLeft / 7.0), still / monthsLeft, still / daysLeft)
            : new GoalProgress(year, goalMeters, doneMeters, expected, daysLeft, monthsLeft, null, null, null);
    }

    static int DaysDone(int year, int days, DateOnly today)
    {
        if (year < today.Year)
            return days;
        return year > today.Year ? 0 : today.DayOfYear;
    }
}
