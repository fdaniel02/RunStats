using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Features.Pace;

namespace RunStats.Tests.Features;

public class PaceCalculatorPageTests : BunitContext
{
    sealed class FakeQueries(PaceData data) : IPaceQueries
    {
        public Task<PaceData> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(data);
    }

    IRenderedComponent<PaceCalculator> Render(PaceData data)
    {
        Services.AddSingleton<IPaceQueries>(new FakeQueries(data));
        return Render<PaceCalculator>();
    }

    static readonly PaceData Empty = new([], null);

    static readonly PaceData Full = new(
    [
        new PaceReference(1, 1000, null, 200, new DateOnly(2026, 9, 28), null),
        new PaceReference(3, 5000, 1310, 1250.25, new DateOnly(2026, 9, 27), 42),
        new PaceReference(5, 21097.5, 6423, null, null, null)
    ], new DateOnly(2026, 11, 5));

    [Fact]
    public void The_distance_presets_have_exact_distances()
    {
        var presets = Render(Empty).FindAll(".distance-preset");

        Assert.Equal(["1 km", "5 km", "10 km", "10 miles", "Half marathon", "Marathon"], presets.Select(b => b.TextContent));
        Assert.Equal(["1000", "5000", "10000", "16093.44", "21097.5", "42195"], presets.Select(b => b.GetAttribute("data-meters")));
    }

    [Fact]
    public void The_script_is_loaded()
    {
        Assert.Contains("js/pace.js", Render(Empty).Find("script[type=module]").GetAttribute("src"));
    }

    [Fact]
    public void Without_data_it_says_when_it_arrives()
    {
        var page = Render(Empty);

        Assert.Equal("Garmin's predictions and your records arrive with the next sync.", page.Find("section[aria-labelledby=references-title] p.muted").TextContent);
        Assert.Empty(page.FindAll("table"));
    }

    [Fact]
    public void Predictions_and_records_are_shown_with_their_pace()
    {
        var rows = Render(Full).FindAll("tbody tr");

        Assert.Equal(["1 km", "5 km", "Half marathon"], rows.Select(r => r.QuerySelector("td")!.TextContent));
        var cells = rows[1].QuerySelectorAll("td");
        Assert.Equal("21:50", cells[1].QuerySelector("button")!.TextContent);
        Assert.Equal("4:22 /km", cells[1].QuerySelector(".detail")!.TextContent);
        Assert.Equal("20:50", cells[2].QuerySelector("button")!.TextContent);
        Assert.Equal("4:10 /km", cells[2].QuerySelector(".detail")!.TextContent);
    }

    [Fact]
    public void A_missing_prediction_or_record_has_nothing_to_click()
    {
        var rows = Render(Full).FindAll("tbody tr");

        Assert.Equal("–", rows[0].QuerySelectorAll("td")[1].TextContent.Trim());
        Assert.Equal("–", rows[2].QuerySelectorAll("td")[2].TextContent.Trim());
        Assert.Equal([1, 2, 1], rows.Select(r => r.QuerySelectorAll("button").Length));
    }

    [Fact]
    public void The_buttons_give_the_script_exact_numbers()
    {
        var buttons = Render(Full).FindAll(".pace-reference");

        Assert.Equal(
            [("record", "1000", "200"), ("prediction", "5000", "1310"), ("record", "5000", "1250.25"), ("prediction", "21097.5", "6423")],
            buttons.Select(b => (b.GetAttribute("data-kind"), b.GetAttribute("data-meters"), b.GetAttribute("data-seconds"))));
    }

    [Fact]
    public void A_records_date_links_to_its_run_when_known()
    {
        var links = Render(Full).FindAll(".detail a");

        Assert.Equal(["2026-09-28", "2026-09-27"], links.Select(a => a.TextContent));
        Assert.Equal([null, "runs/42"], links.Select(a => a.GetAttribute("href")));
        Assert.All(links, a => Assert.Equal("false", a.GetAttribute("data-enhance-nav")));
    }

    [Fact]
    public void The_footnote_dates_the_predictions()
    {
        Assert.Equal("Garmin's predictions from 2026-11-05. Click a time to load it into the calculator.", Render(Full).Find(".footnote").TextContent);
    }

    [Fact]
    public void With_only_records_the_footnote_has_no_date()
    {
        var page = Render(new PaceData([new PaceReference(1, 1000, null, 200, new DateOnly(2026, 9, 28), null)], null));

        Assert.Equal("Click a time to load it into the calculator.", page.Find(".footnote").TextContent);
    }
}
