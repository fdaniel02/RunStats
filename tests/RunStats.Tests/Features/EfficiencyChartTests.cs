using Bunit;
using RunStats.Features.Trends;

namespace RunStats.Tests.Features;

public class EfficiencyChartTests : BunitContext
{
    static EfficiencyData Data(params EfficiencyMonth[] months) => new(150, 160, 30, months);

    static readonly EfficiencyMonth[] Months =
    [
        new(new DateOnly(2026, 7, 1), 3 * 3600 + 600, 6, 360, 24.4),
        new(new DateOnly(2026, 8, 1), 0, 0, null, null),
        new(new DateOnly(2026, 9, 1), 900, 1, null, 15),
        new(new DateOnly(2026, 10, 1), 2 * 3600, 4, 330, null)
    ];

    IRenderedComponent<EfficiencyChart> Render(EfficiencyData data) => Render<EfficiencyChart>(p => p.Add(c => c.Data, data));

    [Fact]
    public void Each_month_describes_its_pace_time_and_temperature()
    {
        var slots = Render(Data(Months)).FindAll(".slot");

        Assert.Equal(
        [
            "2026-07: 6:00 /km at 150–160 bpm, over 3 h 10 min in 6 runs, 24 °C",
            "2026-08: no running at 150–160 bpm",
            "2026-09: 15 min in 1 run, too little for a pace",
            "2026-10: 5:30 /km at 150–160 bpm, over 2 h 0 min in 4 runs"
        ], slots.Select(s => s.GetAttribute("aria-label")));
    }

    [Fact]
    public void Seconds_in_the_band_are_not_rounded_to_no_minutes()
    {
        var slot = Render(Data(Months[0], new EfficiencyMonth(new DateOnly(2026, 8, 1), 20, 1, null, null))).FindAll(".slot")[1];

        Assert.Equal("2026-08: under 1 min in 1 run, too little for a pace", slot.GetAttribute("aria-label"));
    }

    [Fact]
    public void Faster_is_higher_with_gaps_for_months_without_a_pace()
    {
        var chart = Render(Data(Months));

        Assert.Equal(["bottom: 0%", "bottom: 100%"], chart.FindAll(".dot").Select(d => d.GetAttribute("style")));
        Assert.Equal("M0.5 100 M3.5 0", chart.Find("path.line").GetAttribute("d"));
        Assert.Equal(["5:30", "5:40", "5:50", "6:00"], chart.FindAll(".gridline span").Select(s => s.TextContent));
    }

    [Fact]
    public void The_table_lists_every_month_newest_first()
    {
        var rows = Render(Data(Months)).FindAll(".as-table tbody tr");

        Assert.Equal(["2026-10", "2026-09", "2026-08", "2026-07"], rows.Select(r => r.QuerySelector("td")!.TextContent));
        Assert.Equal(["2026-07", "6:00", "3 h 10 min", "6", "24"], rows[^1].QuerySelectorAll("td").Select(td => td.TextContent));
        Assert.Equal(["2026-09", "–", "15 min", "1", "15"], rows[1].QuerySelectorAll("td").Select(td => td.TextContent));
    }

    [Fact]
    public void Without_a_month_with_a_pace_it_says_what_is_needed()
    {
        var chart = Render(Data(Months[1], Months[2]));

        Assert.Equal("Not enough running at 150–160 bpm yet: a month needs 30 minutes in it.", chart.Find("p.muted").TextContent);
        Assert.Empty(chart.FindAll("figure"));
    }
}
