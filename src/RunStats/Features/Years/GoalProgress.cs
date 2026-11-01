namespace RunStats.Features.Years;

// ExpectedMeters is the goal spread evenly over the year's days, today included; null for a past year. Weekly is what
// each remaining week still needs; null once the goal is reached or the year is over.
public sealed record GoalProgress(int Year, double GoalMeters, double DoneMeters, double? ExpectedMeters, double? WeeksLeft, double? WeeklyMeters)
{
    public bool IsPast => ExpectedMeters is null;
    public bool IsReached => DoneMeters >= GoalMeters;
    public double Fraction => Math.Clamp(DoneMeters / GoalMeters, 0, 1);
    public double? ExpectedFraction => ExpectedMeters / GoalMeters;

    public static GoalProgress For(int year, double goalMeters, double doneMeters, DateOnly today)
    {
        var days = DateTime.IsLeapYear(year) ? 366 : 365;
        var daysDone = DaysDone(year, days, today);
        if (daysDone == days)
            return new GoalProgress(year, goalMeters, doneMeters, null, null, null);
        var weeksLeft = (days - daysDone) / 7.0;
        var still = goalMeters - doneMeters;
        return new GoalProgress(year, goalMeters, doneMeters, goalMeters * daysDone / days, weeksLeft, still > 0 ? still / weeksLeft : null);
    }

    static int DaysDone(int year, int days, DateOnly today)
    {
        if (year < today.Year)
            return days;
        return year > today.Year ? 0 : today.DayOfYear;
    }
}
