using System.Globalization;
using RunStats.Shared;

namespace RunStats.Features.Years;

public static class GoalLines
{
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string? Pace(GoalProgress? progress)
    {
        if (progress is null || progress.IsReached || progress.ExpectedMeters is not { } expected || expected <= 0)
            return null;
        var difference = progress.DoneMeters - expected;
        return $"{Format.Km(Math.Abs(difference))} km {(difference >= 0 ? "ahead of" : "behind")} the {Whole(expected)} km expected by today";
    }

    public static string? Weekly(GoalProgress? progress)
    {
        if (progress?.Weeks is not { } weeks)
            return null;
        var thisWeek = Format.Km(weeks.ThisWeekMeters);
        if (weeks.IsLast)
            return weeks.ThisWeekDays == 7 ? $"{thisWeek} km for the last week" : $"{thisWeek} km for the last {weeks.ThisWeekDays} days";
        var share = $"{Format.Km(weeks.FullWeekMeters)} km a week for the remaining {weeks.Count} weeks";
        if (weeks.ThisWeekDays == 7)
            return share;
        var days = weeks.ThisWeekDays == 1 ? "1 day" : $"{weeks.ThisWeekDays} days";
        return $"{share} (this one {thisWeek} km for {days})";
    }

    public static string? Monthly(GoalProgress? progress)
    {
        if (progress?.Months is not { } months)
            return null;
        return months.IsLast
            ? $"{Format.Km(months.MonthlyMeters)} km for the last month"
            : $"{Format.Km(months.MonthlyMeters)} km a month for the remaining {months.Count} months";
    }

    public static string? Daily(GoalProgress? progress)
    {
        if (progress?.DailyMeters is not { } daily)
            return null;
        var remaining = progress.DaysLeft == 1 ? "day" : $"{progress.DaysLeft} days";
        return $"{Format.Km(daily)} km a day for the remaining {remaining}";
    }

    public static string Whole(double meters) => (meters / 1000).ToString("0.#", Culture);
}
