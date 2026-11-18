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

    public static string? Weekly(GoalProgress? progress) =>
        progress?.WeeklyMeters is { } weekly ? $"{Format.Km(weekly)} km a week for the remaining {OneDecimal(progress.WeeksLeft!.Value)} weeks" : null;

    public static string? Monthly(GoalProgress? progress) =>
        progress?.MonthlyMeters is { } monthly ? $"{Format.Km(monthly)} km a month for the remaining {OneDecimal(progress.MonthsLeft!.Value)} months" : null;

    public static string? Daily(GoalProgress? progress)
    {
        if (progress?.DailyMeters is not { } daily)
            return null;
        var remaining = progress.DaysLeft == 1 ? "day" : $"{progress.DaysLeft} days";
        return $"{Format.Km(daily)} km a day for the remaining {remaining}";
    }

    public static string Whole(double meters) => (meters / 1000).ToString("0.#", Culture);

    static string OneDecimal(double value) => value.ToString("0.#", Culture);
}
