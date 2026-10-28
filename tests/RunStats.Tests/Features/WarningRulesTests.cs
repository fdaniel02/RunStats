using RunStats.Data;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class WarningRulesTests
{
    // Monday 28 September 2026, 22:00 UTC: the evening before the injury.
    static readonly DateTime AsOf = new(2026, 9, 28, 22, 0, 0, DateTimeKind.Utc);
    static readonly WarningSettings Settings = new();

    const short EasyPace = 335;
    const short FastPace = 300;
    const short VeryFastPace = 240;

    static RunPaceBand Band(short pace, double minutes, float cadence = 174, float step = 1020, float? balance = null) => new()
    {
        PaceSecondsPerKm = pace,
        Seconds = (int)(minutes * 60),
        FlatSeconds = (int)(minutes * 60),
        CadenceSpm = cadence,
        StepLengthMm = step,
        GroundContactBalanceLeftPercent = balance
    };

    // The fast minutes are faster than 5:10/km, the very fast ones (part of the fast ones) faster than 4:15/km.
    static WarningRun Run(double daysAgo, double minutes, double fast = 0, double veryFast = 0, string type = "running", float? balance = null)
    {
        List<RunPaceBand> bands = [Band(EasyPace, minutes - fast, balance: balance)];
        if (fast > veryFast)
            bands.Add(Band(FastPace, fast - veryFast, balance: balance));
        if (veryFast > 0)
            bands.Add(Band(VeryFastPace, veryFast, cadence: 178, step: 1400, balance: balance));
        return Run(daysAgo, minutes, type, [.. bands]);
    }

    static WarningRun Run(double daysAgo, double minutes, string type, params RunPaceBand[] bands)
    {
        var start = AsOf.AddDays(-daysAgo);
        return new WarningRun(start, start.AddHours(2), type, minutes * 60, bands);
    }

    // Three 45 min runs a week with a little tempo, as most of the history looks.
    static List<WarningRun> Usual(int weeks = 16, int fromWeek = 0) =>
        [.. Enumerable.Range(fromWeek, weeks).SelectMany(w => new[] { Run(w * 7 + 1, 45, fast: 2), Run(w * 7 + 3, 45), Run(w * 7 + 5, 45, fast: 3) })];

    static List<Warning> Evaluate(IEnumerable<WarningRun> runs, WarningSettings? settings = null) =>
        WarningRules.Evaluate([.. runs], AsOf, settings ?? Settings);

    [Fact]
    public void Usual_training_has_no_warnings() => Assert.Empty(Evaluate(Usual()));

    [Fact]
    public void No_runs_no_warnings() => Assert.Empty(Evaluate([]));

    [Fact]
    public void Fast_minutes_far_above_the_weeks_before_warn()
    {
        // The week of 2026-09-21: 68 fast minutes against ~5 a week.
        var runs = Usual(weeks: 15, fromWeek: 1).Append(Run(4, 61, fast: 38)).Append(Run(1, 57, fast: 30));

        var warning = Assert.Single(Evaluate(runs), w => w.Kind == WarningKind.FastJump);

        Assert.Equal("1 h 8 min faster than 5:10 /km in the last 7 days, against 5 min a week in the 4 weeks before.", warning.Message);
    }

    [Fact]
    public void Fast_minutes_need_the_minimum_even_after_weeks_without()
    {
        // 40 fast minutes against none: far more, but below the 45 min minimum.
        var runs = Enumerable.Range(1, 4).Select(w => Run(w * 7 + 2, 45)).Append(Run(2, 60, fast: 40));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.FastJump);
    }

    [Fact]
    public void Fast_minutes_need_the_ratio()
    {
        // 50 min against 20 a week: more, but not three times as much.
        var runs = Enumerable.Range(1, 4).Select(w => Run(w * 7 + 2, 60, fast: 20)).Append(Run(2, 70, fast: 50));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.FastJump);
    }

    [Fact]
    public void Very_fast_minutes_after_weeks_without_warn()
    {
        // The intervals of 2026-09-16: 9 min faster than 4:15/km, none in the month before.
        var runs = Usual(weeks: 15, fromWeek: 1).Append(Run(3, 45, fast: 9, veryFast: 9));

        var warning = Assert.Single(Evaluate(runs), w => w.Kind == WarningKind.VeryFastJump);

        Assert.Equal("9 min faster than 4:15 /km in the last 7 days, against 0 min a week in the 4 weeks before.", warning.Message);
    }

    [Fact]
    public void A_few_very_fast_minutes_dont_warn()
    {
        // Like July 2026: 6 strides-like minutes.
        var runs = Usual(weeks: 15, fromWeek: 1).Append(Run(3, 45, fast: 6, veryFast: 6));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.VeryFastJump);
    }

    [Fact]
    public void Running_time_far_above_the_months_before_warns()
    {
        // 12 weeks of two 40 min runs a week, then 4 weeks of three 45 min runs: 9 h against 5 h 20 min.
        var before = Enumerable.Range(4, 12).SelectMany(w => new[] { Run(w * 7 + 1, 40), Run(w * 7 + 4, 40) });
        var recent = Enumerable.Range(0, 4).SelectMany(w => new[] { Run(w * 7 + 1, 45), Run(w * 7 + 3, 45), Run(w * 7 + 5, 45) });

        var warning = Assert.Single(Evaluate(before.Concat(recent)), w => w.Kind == WarningKind.VolumeJump);

        Assert.Equal("9 h 0 min of running in the last 4 weeks, against 5 h 20 min per 4 weeks in the 12 weeks before.", warning.Message);
    }

    [Fact]
    public void One_big_week_is_no_volume_jump()
    {
        var runs = Usual().Append(Run(2, 60)).Append(Run(4, 60));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.VolumeJump);
    }

    [Fact]
    public void A_much_longer_run_than_in_the_weeks_before_warns()
    {
        var runs = Usual(weeks: 15, fromWeek: 1).Append(Run(2, 84));

        var warning = Assert.Single(Evaluate(runs), w => w.Kind == WarningKind.LongRunJump);

        Assert.Equal("A 1 h 24 min run in the last 7 days; the longest in the 4 weeks before was 45 min.", warning.Message);
    }

    [Theory]
    [InlineData(70)] // below the minimum for a long run
    [InlineData(55)] // not much longer than before
    public void Shorter_long_runs_dont_warn(double minutes)
    {
        var runs = Usual(weeks: 15, fromWeek: 1).Append(Run(2, minutes));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.LongRunJump);
    }

    [Fact]
    public void A_long_run_after_a_break_has_nothing_to_compare_with() =>
        Assert.DoesNotContain(Evaluate([Run(2, 100)]), w => w.Kind == WarningKind.LongRunJump);

    [Fact]
    public void Two_hard_runs_within_three_days_warn()
    {
        // 2026-09-24 and 2026-09-27: 10–11 km at ~4:55/km, 68 h apart.
        var start = new DateTime(2026, 9, 24, 15, 0, 0, DateTimeKind.Utc);
        var first = Run(0, 50, fast: 45) with { StartUtc = start, StartLocal = start.AddHours(2) };
        var second = Run(0, 54, fast: 50) with { StartUtc = start.AddHours(68), StartLocal = start.AddHours(70) };

        var warning = Assert.Single(Evaluate([first, second]), w => w.Kind == WarningKind.HardRunsBackToBack);

        Assert.Equal("Hard runs on 2026-09-24 and 2026-09-27, 68 h apart.", warning.Message);
    }

    [Fact]
    public void A_long_run_counts_as_hard()
    {
        var runs = new[] { Run(4, 80), Run(2, 50, fast: 25) };

        Assert.Contains(Evaluate(runs), w => w.Kind == WarningKind.HardRunsBackToBack);
    }

    [Theory]
    [InlineData(4, 25, 1, 25)] // hard runs 3 days apart, but more than 72 h
    [InlineData(4, 25, 3, 10)] // the second is easy
    public void Hard_runs_further_apart_or_an_easy_one_dont_warn(double firstDaysAgo, double firstFast, double secondDaysAgo, double secondFast)
    {
        var runs = new[] { Run(firstDaysAgo, 50, fast: firstFast), Run(secondDaysAgo - 0.1, 50, fast: secondFast) };

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.HardRunsBackToBack);
    }

    static List<WarningRun> FormBaseline(string type = "running") =>
        [.. Enumerable.Range(2, 8).SelectMany(w => new[] { Run(w * 7 + 1, type, Band(EasyPace, 30), Band(350, 10)), Run(w * 7 + 4, type, Band(EasyPace, 30)) })];

    static WarningRun Run(double daysAgo, string type, params RunPaceBand[] bands) => Run(daysAgo, bands.Sum(b => b.Seconds) / 60.0, type, bands);

    [Fact]
    public void Longer_steps_at_the_same_pace_warn()
    {
        var runs = FormBaseline().Append(Run(3, "running", Band(EasyPace, 30, step: 1071))).Append(Run(8, "running", Band(EasyPace, 30, step: 1071)));

        var warning = Assert.Single(Evaluate(runs), w => w.Kind == WarningKind.FormChange);

        Assert.Equal("At the same pace, step length +5.0% and cadence +0.0 spm in the last 2 weeks, against the 8 weeks before (over 1 h 0 min at comparable paces).",
            warning.Message);
    }

    [Fact]
    public void Lower_cadence_at_the_same_pace_warns()
    {
        var runs = FormBaseline().Append(Run(3, "running", Band(EasyPace, 30, cadence: 170)));

        Assert.Contains("cadence −4.0 spm", Assert.Single(Evaluate(runs), w => w.Kind == WarningKind.FormChange).Message);
    }

    [Fact]
    public void Intervals_with_long_steps_are_no_form_change()
    {
        // 2026-09-16 and 22: long steps at interval pace, which the weeks before had no time at; easy pace unchanged.
        var runs = FormBaseline()
            .Append(Run(6, "running", Band(EasyPace, 20), Band(VeryFastPace, 8, cadence: 176, step: 1370)))
            .Append(Run(12, "running", Band(EasyPace, 20), Band(225, 7, cadence: 178, step: 1450)));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.FormChange);
    }

    [Fact]
    public void Small_form_changes_dont_warn()
    {
        var runs = FormBaseline().Append(Run(3, "running", Band(EasyPace, 30, cadence: 172, step: 1050)));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.FormChange);
    }

    [Fact]
    public void Form_needs_enough_time_at_the_same_pace()
    {
        var runs = FormBaseline().Append(Run(3, "running", Band(EasyPace, 4, step: 1200)));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.FormChange);
    }

    [Theory]
    [InlineData("treadmill_running")]
    [InlineData("trail_running")]
    public void Treadmill_and_trail_runs_are_left_out_of_the_form(string type)
    {
        var runs = FormBaseline().Append(Run(3, type, Band(EasyPace, 30, step: 1200)));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.FormChange);
    }

    [Fact]
    public void Paces_within_one_comparison_band_are_compared()
    {
        // 5:35 and 5:40 are both in the 5:30–5:45 band.
        var runs = FormBaseline().Append(Run(3, "running", Band(340, 30, step: 1080)));

        Assert.Contains(Evaluate(runs), w => w.Kind == WarningKind.FormChange);
    }

    static List<WarningRun> StrapRuns(double fromDaysAgo, int count, float balance) =>
        [.. Enumerable.Range(0, count).Select(i => Run(fromDaysAgo + i * 3, 40, balance: balance))];

    [Fact]
    public void Balance_moving_to_one_side_warns()
    {
        var runs = StrapRuns(16, 10, 50.3f).Concat(StrapRuns(1, 2, 52.0f));

        var warning = Assert.Single(Evaluate(runs), w => w.Kind == WarningKind.BalanceShift);

        Assert.Equal("Left/right balance 52.0% left in the last 2 weeks, against 50.3% in the 8 weeks before.", warning.Message);
    }

    [Fact]
    public void Balance_to_the_right_warns_too() =>
        Assert.Contains(Evaluate(StrapRuns(16, 10, 50.3f).Concat(StrapRuns(1, 2, 48.5f))), w => w.Kind == WarningKind.BalanceShift);

    [Fact]
    public void Small_balance_changes_dont_warn() =>
        Assert.DoesNotContain(Evaluate(StrapRuns(16, 10, 50.3f).Concat(StrapRuns(1, 2, 51.2f))), w => w.Kind == WarningKind.BalanceShift);

    [Fact]
    public void Balance_needs_enough_strap_time_on_both_sides()
    {
        // 15 min of strap running recently: below the 20 min minimum.
        var runs = StrapRuns(16, 10, 50.3f).Append(Run(1, 15, balance: 53f)).Append(Run(3, 40));

        Assert.DoesNotContain(Evaluate(runs), w => w.Kind == WarningKind.BalanceShift);
    }

    [Fact]
    public void Runs_after_the_moment_of_evaluation_are_left_out()
    {
        var runs = Usual(weeks: 15, fromWeek: 1).Append(Run(-1, 61, fast: 60));

        Assert.Empty(Evaluate(runs));
    }

    [Fact]
    public void Windows_roll_from_the_moment_of_evaluation()
    {
        var runs = Usual(weeks: 15, fromWeek: 1).Append(Run(6.9, 70, fast: 60)).ToList();

        Assert.Contains(WarningRules.Evaluate(runs, AsOf, Settings), w => w.Kind == WarningKind.FastJump);
        Assert.DoesNotContain(WarningRules.Evaluate(runs, AsOf.AddDays(0.2), Settings), w => w.Kind == WarningKind.FastJump);
    }

    [Fact]
    public void The_settings_set_what_counts_as_fast()
    {
        // With 5:40/km as "fast", the easy band counts too.
        var settings = new WarningSettings { FastPaceSecondsPerKm = 340 };
        var runs = Enumerable.Range(1, 4).Select(w => Run(w * 7 + 2, 15)).Append(Run(2, 60));

        var warning = Assert.Single(Evaluate(runs, settings), w => w.Kind == WarningKind.FastJump);

        Assert.StartsWith("1 h 0 min faster than 5:40 /km", warning.Message);
    }

    [Fact]
    public void Fast_seconds_count_whole_bands_faster_than_the_pace()
    {
        var run = Run(0, 0, "running", Band(300, 1), Band(305, 2), Band(310, 4));

        Assert.Equal(180, WarningRules.FastSeconds(run, 310));
        Assert.Equal(60, WarningRules.FastSeconds(run, 305));
    }
}
