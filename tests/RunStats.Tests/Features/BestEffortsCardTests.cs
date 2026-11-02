using Bunit;
using RunStats.Features.Runs;

namespace RunStats.Tests.Features;

public class BestEffortsCardTests : BunitContext
{
    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    static readonly OtherEffort Tilburg5k = new(1429.08, 24517302792, new DateOnly(2026, 9, 27));

    static IEnumerable<string> Lines(AngleSharp.Dom.IElement tile) =>
        tile.QuerySelectorAll(".against").Select(l => string.Join(" ", l.Children.Select(Text)));

    IRenderedComponent<BestEffortsCard> Render(params RunEffort[] efforts) =>
        Render<BestEffortsCard>(p => p.Add(c => c.Efforts, efforts).Add(c => c.Year, 2026));

    [Fact]
    public void A_slower_effort_shows_how_far_it_is_from_the_pr_and_the_year_best()
    {
        var tile = Render(new RunEffort(3, 1502.1, new EffortComparison(Tilburg5k, false), new EffortComparison(Tilburg5k, false))).Find(".tile");

        Assert.Equal("5 km", Text(tile.QuerySelector(".title")!));
        Assert.Equal("25:02 5:00 /km", Text(tile.QuerySelector(".value")!));
        Assert.Equal(["PR +1:13 23:49 · 2026-09-27", "2026 +1:13 23:49 · 2026-09-27"], Lines(tile));
        Assert.All(tile.QuerySelectorAll("a"), a => Assert.Equal("runs/24517302792", a.GetAttribute("href")));
        Assert.Empty(tile.QuerySelectorAll(".badge"));
    }

    [Fact]
    public void A_year_best_that_is_not_the_pr_gets_the_badge_and_its_lead_over_the_next()
    {
        var pr = new OtherEffort(261, 1, new DateOnly(2025, 7, 22));
        var next = new OtherEffort(291, 2, new DateOnly(2026, 3, 1));

        var tile = Render(new RunEffort(1, 279.8, new EffortComparison(pr, false), new EffortComparison(next, true))).Find(".tile");

        Assert.Equal(["Year best"], tile.QuerySelectorAll(".badge").Select(Text));
        Assert.Equal(["PR +19 s 4:21 · 2025-07-22", "2026 −11 s next 4:51 · 2026-03-01"], Lines(tile));
    }

    [Fact]
    public void A_first_effort_is_both_bests_without_a_next()
    {
        var tile = Render(new RunEffort(5, 6676.7, new EffortComparison(null, true), new EffortComparison(null, true))).Find(".tile");

        Assert.Equal("Half marathon", Text(tile.QuerySelector(".title")!));
        Assert.Equal(["PR", "Year best"], tile.QuerySelectorAll(".badge").Select(Text));
        Assert.StartsWith("1:51:17 5:16 /km", Text(tile.QuerySelector(".value")!));
        Assert.Equal(["PR First", "2026 First"], Lines(tile));
        Assert.Empty(tile.QuerySelectorAll("a"));
    }

    [Fact]
    public void Tiles_keep_the_given_order()
    {
        var card = Render(
            new RunEffort(1, 280, new EffortComparison(null, true), new EffortComparison(null, true)),
            new RunEffort(2, 463, new EffortComparison(null, true), new EffortComparison(null, true)));

        Assert.Equal(["1 km", "1 mile"], card.FindAll(".tile .title").Select(Text));
    }

    [Theory]
    [InlineData(73.2, "+1:13")]
    [InlineData(19.4, "+19 s")]
    [InlineData(-11, "−11 s")]
    [InlineData(-0.3, "±0 s")]
    [InlineData(-75, "−1:15")]
    public void Differences_are_signed_in_seconds_below_a_minute(double seconds, string text) =>
        Assert.Equal(text, BestEffortsCard.Difference(seconds));
}
