using Bunit;
using RunStats.Features.Trends;

namespace RunStats.Tests.Features;

public class PaceVariationChartTests : BunitContext
{
    static PaceVariationMonth Month(int month, double? median, int runs) => new(new DateOnly(2026, month, 1), median, runs);

    [Fact]
    public void Each_month_is_a_point_with_a_gap_where_there_were_no_runs()
    {
        var chart = Render<PaceVariationChart>(p => p.Add(c => c.Months, [Month(7, 3, 9), Month(8, null, 0), Month(9, 4, 10)]));

        // A scale up to 4%: 3% is three quarters up.
        Assert.Equal("M0.5 25 M2.5 0", chart.Find("path").GetAttribute("d"));
        Assert.Equal(["bottom: 75%", "bottom: 100%"], chart.FindAll(".dot").Select(d => d.GetAttribute("style")));
        Assert.Equal(
            ["2026-07: ±3.0%, the median of 9 outdoor runs", "2026-08: no outdoor runs", "2026-09: ±4.0%, the median of 10 outdoor runs"],
            chart.FindAll(".slot").Select(s => s.GetAttribute("aria-label")));
    }

    [Fact]
    public void The_table_has_the_newest_month_first()
    {
        var chart = Render<PaceVariationChart>(p => p.Add(c => c.Months, [Month(9, 3.24, 1), Month(10, null, 0)]));

        var rows = chart.FindAll("tbody tr").Select(r => r.QuerySelectorAll("td").Select(c => c.TextContent).ToList()).ToList();
        Assert.Equal([["2026-10", "–", "0"], ["2026-09", "3.2", "1"]], rows);
    }

    [Fact]
    public void Without_outdoor_runs_there_is_no_chart()
    {
        var chart = Render<PaceVariationChart>(p => p.Add(c => c.Months, []));

        Assert.Contains("No outdoor runs yet.", chart.Markup);
        Assert.Empty(chart.FindAll("figure"));
    }
}
