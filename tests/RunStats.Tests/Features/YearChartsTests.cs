using Bunit;
using RunStats.Features.Years;

namespace RunStats.Tests.Features;

public class YearChartsTests : BunitContext
{
    const string Empty = "No runs in 2026.";

    // 5 October 2026 was a Monday.
    static RunTime Run(int day, int hour, int minute, double minutes) => new(new DateTime(2026, 10, day, hour, minute, 0), minutes * 60);

    IRenderedComponent<DistanceChart> Distances(params DistanceCount[] counts) =>
        Render<DistanceChart>(p => p.Add(c => c.Counts, counts).Add(c => c.EmptyText, Empty));

    IRenderedComponent<WeekdayTimeHeatmap> Heatmap(params RunTime[] runs) =>
        Render<WeekdayTimeHeatmap>(p => p.Add(c => c.Runs, runs).Add(c => c.EmptyText, Empty));

    IRenderedComponent<TimeOfDayChart> TimeOfDay(params RunTime[] runs) =>
        Render<TimeOfDayChart>(p => p.Add(c => c.Runs, runs).Add(c => c.EmptyText, Empty));

    [Fact]
    public void Without_runs_every_chart_says_so()
    {
        Assert.Equal(Empty, Distances().Find(".empty").TextContent);
        Assert.Equal(Empty, Heatmap().Find(".empty").TextContent);
        Assert.Equal(Empty, TimeOfDay().Find(".empty").TextContent);
    }

    [Fact]
    public void Distance_bars_show_their_runs_relative_to_the_most()
    {
        var chart = Distances(new DistanceCount(1, 0), new DistanceCount(2, 1), new DistanceCount(3, 4));

        var slots = chart.FindAll(".slot");
        Assert.Equal(["1 km: 0 runs", "2 km: 1 run", "3 km: 4 runs"], slots.Select(s => s.GetAttribute("aria-label")));
        Assert.Null(slots[0].QuerySelector(".bar"));
        Assert.False(slots[0].HasAttribute("tabindex"));
        Assert.Equal("height: 25%", slots[1].QuerySelector(".bar")!.GetAttribute("style"));
        Assert.Equal(["1", "4"], chart.FindAll(".value").Select(v => v.TextContent));
        Assert.Equal(["1", "2", "3"], chart.FindAll(".x").Select(x => x.TextContent));
    }

    [Fact]
    public void Long_distance_axes_label_only_round_kms()
    {
        var counts = Enumerable.Range(1, 26).Select(km => new DistanceCount(km, 1)).ToArray();

        Assert.Equal(["1", "5", "10", "15", "20", "25"], Distances(counts).FindAll(".x").Select(x => x.TextContent));
    }

    [Fact]
    public void The_distance_table_lists_only_distances_with_runs()
    {
        var chart = Distances(new DistanceCount(1, 0), new DistanceCount(2, 3));

        Assert.Equal(["23"], chart.FindAll(".as-table tbody tr").Select(r => r.TextContent));
    }

    [Fact]
    public void The_heatmap_has_a_row_per_weekday_starting_on_monday()
    {
        var chart = Heatmap(Run(6, 18, 0, 30));

        Assert.Equal(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], chart.FindAll(".day").Select(d => d.TextContent));
        Assert.Equal(["Tue 18:00–18:30: 1 run"], chart.FindAll(".cell[tabindex]").Select(c => c.GetAttribute("aria-label")));
    }

    [Fact]
    public void Heatmap_cells_get_darker_with_more_runs()
    {
        var chart = Heatmap(Run(5, 9, 0, 30), Run(12, 9, 0, 60), Run(19, 9, 0, 60), Run(26, 9, 0, 60));

        Assert.Equal(["cell level-4 tip-right", "cell level-3 tip-left"], chart.FindAll(".cell[tabindex]").Select(c => c.ClassName));
    }

    [Fact]
    public void The_heatmap_table_has_a_row_per_slot_and_a_column_per_weekday()
    {
        var chart = Heatmap(Run(6, 18, 0, 30));

        Assert.Equal(8, chart.FindAll(".as-table th").Count);
        Assert.Equal(["18:00–18:30", "18:30–19:00"], chart.FindAll(".as-table tbody tr td:first-child").Select(c => c.TextContent));
        Assert.Equal("1", chart.Find(".as-table tbody tr td:nth-child(3)").TextContent);
    }

    [Fact]
    public void The_curve_has_a_point_and_a_tip_per_slot()
    {
        var chart = TimeOfDay(Run(5, 9, 0, 30), Run(6, 9, 0, 60));

        Assert.Equal(["09:00–09:30: 2 runs", "09:30–10:00: 1 run"], chart.FindAll(".slot").Select(s => s.GetAttribute("aria-label")));
        Assert.Equal("M0.5 50 L1.5 75", chart.Find(".line").GetAttribute("d"));
        Assert.Equal("M.5 100 L0.5 50 L1.5 75 L1.5 100 Z", chart.Find(".fill").GetAttribute("d"));
        Assert.Equal("0 0 2 100", chart.Find("svg").GetAttribute("viewBox"));
    }

    [Fact]
    public void A_quiet_year_has_whole_runs_on_the_scale()
    {
        var chart = TimeOfDay(Run(5, 9, 0, 30));

        Assert.Equal(["0", "1", "2", "3", "4"], chart.FindAll(".gridline span").Select(s => s.TextContent));
    }
}
