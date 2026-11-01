using Bunit;
using RunStats.Features.Trends;

namespace RunStats.Tests.Features;

public class BestEffortChartTests : BunitContext
{
    static readonly MonthEffort July = new(1470, 11, new DateOnly(2026, 7, 10), "Tempo");
    static readonly MonthEffort September = new(1500, 12, new DateOnly(2026, 9, 2), "Evening run");

    static EffortSeries FiveKm => new(3, July,
    [
        new EffortMonth(new DateOnly(2026, 7, 1), July),
        new EffortMonth(new DateOnly(2026, 8, 1), null),
        new EffortMonth(new DateOnly(2026, 9, 1), September)
    ]);

    static EffortSeries OneKm => new(1, new MonthEffort(280, 13, new DateOnly(2026, 9, 2), "Evening run"),
    [
        new EffortMonth(new DateOnly(2026, 7, 1), null),
        new EffortMonth(new DateOnly(2026, 8, 1), null),
        new EffortMonth(new DateOnly(2026, 9, 1), new MonthEffort(280, 13, new DateOnly(2026, 9, 2), "Evening run"))
    ]);

    IRenderedComponent<BestEffortChart> Render(params EffortSeries[] series) => Render<BestEffortChart>(p => p.Add(c => c.Series, series));

    [Fact]
    public void A_tab_per_distance_with_5_km_shown_first()
    {
        var chart = Render(OneKm, FiveKm);

        Assert.Equal(["1 km", "5 km"], chart.FindAll(".tabs label").Select(l => l.TextContent));
        Assert.Equal(["effort-1", "effort-3"], chart.FindAll("input.distance").Select(i => i.Id));
        Assert.Equal(["effort-3"], chart.FindAll("input.distance").Where(i => i.HasAttribute("checked")).Select(i => i.Id));
    }

    [Fact]
    public void Without_5_km_the_first_distance_is_shown()
    {
        var chart = Render(OneKm);

        Assert.True(chart.Find("#effort-1").HasAttribute("checked"));
    }

    [Fact]
    public void Months_with_an_effort_link_their_run_and_others_leave_a_gap()
    {
        var panel = Render(FiveKm).Find(".panel.d3");

        var slots = panel.QuerySelectorAll(".slot");
        Assert.Equal(3, slots.Length);
        Assert.Equal(["runs/11", "runs/12"], panel.QuerySelectorAll(".slot a.dot").Select(a => a.GetAttribute("href")));
        Assert.Equal("Jul 2026 · 24:30 · Tempo (2026-07-10)", slots[0].QuerySelector("a.dot")!.GetAttribute("aria-label"));
        Assert.Equal("Aug 2026: no run over 5 km", slots[1].QuerySelector(".tip")!.TextContent);
        Assert.Equal("M0.5 0 M2.5 100", panel.QuerySelector("path.line")!.GetAttribute("d"));
    }

    [Fact]
    public void Faster_is_higher_and_the_best_is_a_line()
    {
        var panel = Render(FiveKm).Find(".panel.d3");

        var dots = panel.QuerySelectorAll("a.dot").Select(a => a.GetAttribute("style")).ToList();
        Assert.Equal(["bottom: 100%", "bottom: 0%"], dots);
        Assert.Equal("bottom: 100%", panel.QuerySelector(".best")!.GetAttribute("style"));
        Assert.Equal(["24:30", "24:40", "24:50", "25:00"], panel.QuerySelectorAll(".gridline span").Select(s => s.TextContent));
    }

    [Fact]
    public void The_caption_names_the_best_and_links_it()
    {
        var caption = Render(FiveKm).Find(".panel.d3 figcaption");

        Assert.Equal("Fastest per month · best 24:30 on 2026-07-10",
            string.Join(" ", caption.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal("runs/11", caption.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void The_table_lists_the_months_with_an_effort_newest_first()
    {
        var rows = Render(FiveKm).FindAll(".panel.d3 .as-table tbody tr");

        Assert.Equal(["2026-09", "2026-07"], rows.Select(r => r.QuerySelector("td")!.TextContent));
        Assert.Equal("runs/12", rows[0].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void Quarters_are_labelled_over_a_few_years_and_only_years_beyond()
    {
        static EffortSeries Months(int count) => new(1, July,
            [.. Enumerable.Range(0, count).Select(i => new EffortMonth(new DateOnly(2023, 1, 1).AddMonths(i), July))]);

        Assert.Equal(["Jan 23", "Apr 23", "Jul 23", "Oct 23"], Render(Months(12)).FindAll(".x").Select(x => x.TextContent));
        Assert.Equal(["2023", "2024", "2025", "2026"], Render(Months(40)).FindAll(".x").Select(x => x.TextContent));
    }
}
