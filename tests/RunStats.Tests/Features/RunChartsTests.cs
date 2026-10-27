using System.Text.Json;
using Bunit;
using RunStats.Features.Runs;

namespace RunStats.Tests.Features;

public class RunChartsTests : BunitContext
{
    static List<ChartPoint> Points(bool elevation = true) =>
    [
        new(0.01, 330.4, 120.2, elevation ? 5.4 : null, 170.4, 1.04567, 51.6000004, 5.5),
        new(2.5, null, 160, elevation ? 6 : null, null, null, null, null),
        new(5.004, 300, 170, elevation ? 4 : null, 180, 1.1, 51.61, 5.51)
    ];

    IRenderedComponent<RunCharts> Render(IReadOnlyList<ChartPoint> points) =>
        Render<RunCharts>(p => p.Add(c => c.Points, points));

    [Fact]
    public void Each_measure_gets_a_titled_chart_with_its_line_and_ticks()
    {
        var charts = Render(Points()).FindAll(".chart");

        Assert.Equal(["pace", "hr", "elevation", "cadence", "stride"], charts.Select(c => c.GetAttribute("data-key")));
        Assert.Equal("Pace /km", charts[0].QuerySelector(".chart-title")!.TextContent);
        Assert.StartsWith("M", charts[0].QuerySelector("path.line")!.GetAttribute("d"));
        Assert.NotEmpty(charts[0].QuerySelectorAll(".y-ticks span"));
        Assert.Equal(charts[0].QuerySelectorAll(".y-ticks span").Length, charts[0].QuerySelectorAll("line.grid").Length);
        Assert.NotNull(charts[2].QuerySelector("path.area"));
        Assert.Null(charts[0].QuerySelector("path.area"));
    }

    [Fact]
    public void Tick_labels_sit_at_their_place()
    {
        var page = Render(Points());

        Assert.Equal("top: 0%", page.Find(".chart .y-ticks span").GetAttribute("style"));
        Assert.Equal("left: 0%", page.Find(".x-ticks .ticks span").GetAttribute("style"));
    }

    [Fact]
    public void A_measure_without_values_has_no_chart_and_no_data()
    {
        var page = Render(Points(elevation: false));

        Assert.Empty(page.FindAll(".chart[data-key=elevation]"));
        Assert.False(JsonDocument.Parse(page.Find("#chart-data").InnerHtml).RootElement.TryGetProperty("elevation", out _));
    }

    [Fact]
    public void The_hover_script_gets_rounded_values_and_the_charts_it_shows()
    {
        var data = JsonDocument.Parse(Render(Points()).Find("#chart-data").InnerHtml).RootElement;

        Assert.Equal(5.004, data.GetProperty("total").GetDouble());
        Assert.Equal(["Pace", "Heart rate", "Elevation", "Cadence", "Stride"],
            data.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("title").GetString()));
        Assert.Equal("[0.01,2.5,5.004]", data.GetProperty("km").GetRawText());
        Assert.Equal("[330,null,300]", data.GetProperty("pace").GetRawText());
        Assert.Equal("[120,160,170]", data.GetProperty("hr").GetRawText());
        Assert.Equal("[1.05,null,1.1]", data.GetProperty("stride").GetRawText());
        Assert.Equal("[51.6,null,51.61]", data.GetProperty("lat").GetRawText());
    }

    [Fact]
    public void Full_screen_and_keyboard_controls_are_there()
    {
        var page = Render(Points());

        Assert.Equal("0", page.Find("#charts").GetAttribute("tabindex"));
        Assert.Equal("Full screen", page.Find(".fullscreen-open").TextContent);
        Assert.Equal("Close full screen", page.Find(".fullscreen-close").GetAttribute("aria-label"));
        Assert.Single(page.FindAll("script[type=module]"));
    }

    [Fact]
    public void Without_points_there_are_no_charts()
    {
        var page = Render([]);

        Assert.Empty(page.FindAll("#charts"));
        Assert.Empty(page.FindAll("script"));
    }
}
