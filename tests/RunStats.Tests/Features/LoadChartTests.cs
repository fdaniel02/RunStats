using Bunit;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class LoadChartTests : BunitContext
{
    static readonly DateOnly ThisMonday = new(2026, 10, 26);

    static LoadWeek Week(int weeksAgo, double minutes, double fast = 0, double veryFast = 0, params WarningKind[] warnings) =>
        new(ThisMonday.AddDays(-7 * weeksAgo), minutes * 60, fast * 60, veryFast * 60, warnings, weeksAgo == 0);

    IRenderedComponent<LoadChart> Render(params LoadWeek[] weeks) => Render<LoadChart>(p => p
        .Add(c => c.Weeks, weeks)
        .Add(c => c.FastPaceSecondsPerKm, 310)
        .Add(c => c.VeryFastPaceSecondsPerKm, 255));

    [Fact]
    public void Each_week_stacks_slower_fast_and_very_fast_time()
    {
        var chart = Render(Week(1, 140, fast: 104, veryFast: 12), Week(0, 35));

        var bars = chart.FindAll(".bar");
        Assert.Equal(["segment easy", "segment fast", "segment very-fast"], bars[0].QuerySelectorAll(".segment").Select(s => s.ClassName));
        Assert.Equal(["flex-grow: 2160", "flex-grow: 5520", "flex-grow: 720"], bars[0].QuerySelectorAll(".segment").Select(s => s.GetAttribute("style")));
        Assert.Equal(["segment easy"], bars[1].QuerySelectorAll(".segment").Select(s => s.ClassName));
        // 140 min of a 3 h scale.
        Assert.Equal("height: 77.78%", bars[0].GetAttribute("style"));
    }

    [Fact]
    public void Weeks_with_a_warning_have_a_marker_and_say_which()
    {
        var chart = Render(Week(1, 140, fast: 104, veryFast: 12, WarningKind.FastJump, WarningKind.HardRunsBackToBack), Week(0, 35));

        var slots = chart.FindAll(".slot");
        Assert.NotNull(slots[0].QuerySelector(".marker"));
        Assert.Null(slots[1].QuerySelector(".marker"));
        Assert.Equal("2026-10-19 – 2026-10-25: 2 h 20 min, fast 1 h 44 min, very fast 12 min. Warnings: Fast running, Hard runs back to back",
            slots[0].GetAttribute("aria-label"));
        Assert.Equal("This week (from 2026-10-26): 35 min, fast 0 min, very fast 0 min", slots[1].GetAttribute("aria-label"));
        Assert.Contains("current", slots[1].ClassName);
    }

    [Fact]
    public void The_legend_names_the_paces()
    {
        var chart = Render(Week(0, 35));

        Assert.Equal(
            ["Very fast (faster than 4:15 /km)", "Fast (faster than 5:10 /km)", "Slower", "!Week with a warning"],
            chart.FindAll(".legend li").Select(l => l.TextContent.Trim()));
    }

    [Fact]
    public void Months_are_labelled_at_their_first_week()
    {
        var chart = Render(Week(4, 30), Week(3, 30), Week(0, 30));

        Assert.Equal(["Oct"], chart.FindAll(".x").Select(x => x.TextContent));
    }

    [Fact]
    public void The_table_has_every_week_newest_first()
    {
        var chart = Render(Week(1, 140, fast: 104, veryFast: 12, WarningKind.FastJump), Week(0, 35));

        var rows = chart.FindAll(".as-table tbody tr").Select(r => r.QuerySelectorAll("td").Select(c => c.TextContent).ToList()).ToList();
        Assert.Equal(["This week (from 2026-10-26)", "35 min", "0 min", "0 min", ""], rows[0]);
        Assert.Equal(["2026-10-19 – 2026-10-25", "2 h 20 min", "1 h 44 min", "12 min", "Fast running"], rows[1]);
    }

    [Fact]
    public void Without_runs_there_is_no_chart()
    {
        var chart = Render(Week(1, 0), Week(0, 0));

        Assert.Contains("No runs in the last 2 weeks.", chart.Markup);
        Assert.Empty(chart.FindAll(".bar"));
    }
}
