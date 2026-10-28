using Bunit;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class FormChartsTests : BunitContext
{
    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Tiles_show_the_change_in_units_and_percent_before_and_now()
    {
        var tiles = Render<FormTiles>(p => p.Add(c => c.Tiles,
        [
            new FormTile(FormMetric.StrideLength, new Comparison(102.0, 106.7, 3600)),
            new FormTile(FormMetric.Cadence, new Comparison(174.2, 175.5, 3600)),
            new FormTile(FormMetric.VerticalRatioWatch, new Comparison(8.42, 8.38, 3600)),
            new FormTile(FormMetric.GroundContactStrap, null)
        ]));

        Assert.Equal(
            [["Stride length", "+4.7 cm+4.6%", "102.0 → 106.7 cm", "over 1 h 0 min at comparable paces"],
             ["Cadence", "+1.3 spm+0.7%", "174.2 → 175.5 spm", "over 1 h 0 min at comparable paces"],
             ["Vertical ratio (watch)", "0.0 pt", "8.4 → 8.4 %", "over 1 h 0 min at comparable paces"],
             ["Ground contact (strap)", "–", "Not enough running at comparable paces"]],
            tiles.FindAll(".tile").Select(t => t.Children.Select(Text).ToList()));
    }

    [Theory]
    [InlineData(1.25, "0.0", "+1.3")]
    [InlineData(-0.04, "0.0", "0.0")]
    [InlineData(0, "0", "0")]
    [InlineData(-2, "0", "−2")]
    public void Changes_are_signed_unless_they_round_to_zero(double value, string format, string text) =>
        Assert.Equal(text, FormTiles.Signed(value, format));

    // The long-term average is 100; each month is that plus its change.
    static FormSeries Series(FormMetric metric, params double?[] changes) =>
        new(metric, [.. changes.Select((c, i) => new FormMonth(new DateOnly(2026, 6, 1).AddMonths(i), c is { } v ? new Comparison(100, 100 + v, 7200) : null, c is null ? 600 : 7200))]);

    [Fact]
    public void Each_metric_gets_a_line_with_gaps_for_thin_months()
    {
        var chart = Render<FormChart>(p => p.Add(c => c.Series,
            [Series(FormMetric.StrideLength, 1, -1, null, 1), Series(FormMetric.HeartRate, 2, 0, -2, -4)]));

        var figures = chart.FindAll("figure");
        Assert.Equal(["Stride length cm", "Heart rate bpm · lower is fitter; heat raises it"], figures.Select(f => Text(f.QuerySelector("figcaption")!)));
        // A minimum range of ±2 cm: +1 is three quarters up, −1 a quarter.
        Assert.Equal("M0.5 25 L1.5 75 M3.5 25", figures[0].QuerySelector("path")!.GetAttribute("d"));
        Assert.Equal(3, figures[0].QuerySelectorAll(".dot").Length);
    }

    [Fact]
    public void The_zero_line_is_marked_and_ticks_are_signed()
    {
        var chart = Render<FormChart>(p => p.Add(c => c.Series, [Series(FormMetric.StrideLength, 1, -1)]));

        Assert.Equal(["−2", "−1", "0", "+1", "+2"], chart.FindAll(".gridline span").Select(s => s.TextContent));
        Assert.Equal("0", chart.Find(".gridline.zero span").TextContent);
    }

    [Fact]
    public void Months_say_now_the_long_term_value_and_the_change()
    {
        var chart = Render<FormChart>(p => p.Add(c => c.Series,
        [
            new FormSeries(FormMetric.StrideLength,
            [
                new FormMonth(new DateOnly(2026, 9, 1), new Comparison(102.1, 101.0, 17220), 17220),
                new FormMonth(new DateOnly(2026, 10, 1), null, 1200)
            ]),
            new FormSeries(FormMetric.VerticalRatioWatch, [new FormMonth(new DateOnly(2026, 9, 1), new Comparison(8.4, 8.5, 7200), 7200)])
        ]));

        Assert.Equal(
            ["2026-09: stride length 101.0 cm against the long-term 102.1 cm at the same pace (−1.1 cm, −1.1%), over 4 h 47 min",
             "2026-10: not enough running at comparable paces",
             "2026-09: vertical ratio (watch) 8.5 % against the long-term 8.4 % at the same pace (+0.1 pt), over 2 h 0 min"],
            chart.FindAll(".slot").Select(s => s.GetAttribute("aria-label")));
    }

    [Fact]
    public void The_table_has_a_column_per_metric_and_the_newest_month_first()
    {
        var chart = Render<FormChart>(p => p.Add(c => c.Series,
            [Series(FormMetric.StrideLength, 1, null), Series(FormMetric.Cadence, -0.5, 1)]));

        Assert.Equal(["Month", "Stride length (cm)", "Cadence (spm)"], chart.FindAll("thead th").Select(h => h.TextContent));
        var rows = chart.FindAll("tbody tr").Select(r => r.QuerySelectorAll("td").Select(c => c.TextContent).ToList()).ToList();
        Assert.Equal([["2026-07", "–", "+1.0"], ["2026-06", "+1.0", "−0.5"]], rows);
    }

    [Fact]
    public void Without_months_there_are_no_charts()
    {
        var chart = Render<FormChart>(p => p.Add(c => c.Series, [new FormSeries(FormMetric.StrideLength, [])]));

        Assert.Empty(chart.FindAll("figure"));
        Assert.Empty(chart.FindAll("table"));
    }

    [Fact]
    public void Balance_has_a_dot_per_strap_run_around_even()
    {
        var chart = Render<BalanceChart>(p => p.Add(c => c.Runs,
            [new BalanceRun(1, new DateOnly(2026, 7, 11), 49.7), new BalanceRun(2, new DateOnly(2026, 9, 22), 52.5)]));

        Assert.Equal(["bottom: 47%", "bottom: 75%"], chart.FindAll(".dot").Select(d => d.GetAttribute("style")));
        Assert.Equal("2026-09-22: 52.5% left, 47.5% right", chart.FindAll(".slot")[1].GetAttribute("aria-label"));
        Assert.Equal("50%", chart.Find(".gridline.even span").TextContent);
        Assert.Equal("2026-07-11 – 2026-09-22 · above 50% leans left", Text(chart.Find(".range")));
    }

    [Fact]
    public void Without_strap_runs_the_balance_says_why() =>
        Assert.Contains("only the strap measures left/right balance", Render<BalanceChart>(p => p.Add(c => c.Runs, [])).Markup);
}
