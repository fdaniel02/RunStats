using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RunStats.Features.Runs;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class UsualFormCardTests : BunitContext
{
    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    static Dictionary<FormMetric, Comparison?> Metrics(Comparison? stride, Comparison? cadence, bool strap = false)
    {
        var metrics = RunAnalysisQueries.UsualMetrics(strap);
        return new() { [metrics[0]] = stride, [metrics[1]] = cadence, [metrics[2]] = null, [metrics[3]] = null };
    }

    IRenderedComponent<UsualFormCard> Render(UsualForm usual, string typeKey = "running")
    {
        Services.AddSingleton(Options.Create(new WarningSettings()));
        return Render<UsualFormCard>(p => p.Add(c => c.Usual, usual).Add(c => c.TypeKey, typeKey));
    }

    static readonly UsualForm Usual = new(11, false,
        new BandComparison(null, 40 * 60, Metrics(new Comparison(109.7, 113.8, 660), new Comparison(176.1, 173.2, 660))),
        [
            new BandComparison(285, 29 * 60, Metrics(null, null)),
            new BandComparison(300, 11 * 60, Metrics(new Comparison(109.7, 114.4, 660), new Comparison(176.1, 172.9, 660)))
        ]);

    [Fact]
    public void The_intro_says_what_the_run_is_compared_with()
    {
        var intro = Text(Render(Usual).Find(".intro"));

        Assert.Contains("Against your outdoor runs of the 8 weeks before this one (11 runs), at the same pace.", intro);
        Assert.Contains("only against runs without the chest strap", intro);
    }

    [Theory]
    [InlineData("treadmill_running", "treadmill")]
    [InlineData("trail_running", "trail")]
    public void Other_types_are_compared_with_their_own(string typeKey, string kind) =>
        Assert.Contains($"Against your {kind} runs", Text(Render(Usual, typeKey).Find(".intro")));

    [Fact]
    public void The_overall_change_is_in_tiles()
    {
        var tiles = Render(Usual).FindAll(".tile");

        Assert.Equal(4, tiles.Count);
        Assert.Equal(["Stride length", "+4.1 cm+3.7%", "109.7 → 113.8 cm"], tiles[0].Children.Take(3).Select(Text));
    }

    [Fact]
    public void Each_pace_has_a_row_and_a_new_pace_says_so()
    {
        var rows = Render(Usual).FindAll("table.bands tbody tr");

        Assert.Equal(["4:45–5:00", "29 min", "New pace: no running at it in the 8 weeks before"], rows[0].Children.Select(Text));
        Assert.Equal(["5:00–5:15", "11 min"], rows[1].Children.Take(2).Select(Text));
        Assert.Equal("109.7 → 114.4 +4.7 · +4.3%", Text(rows[1].Children[2]).TrimStart('!', ' '));
        Assert.Equal("–", Text(rows[1].Children[4]));
    }

    [Fact]
    public void Changes_past_the_warning_thresholds_are_marked()
    {
        var row = Render(Usual).FindAll("table.bands tbody tr")[1];

        // Stride +4.3% (over 4%) and cadence −3.2 spm (over 3).
        Assert.Contains("flag", row.Children[2].ClassName);
        Assert.Contains("flag", row.Children[3].ClassName);
        Assert.NotNull(row.Children[2].QuerySelector(".icon"));
    }

    [Fact]
    public void Smaller_changes_are_not_marked()
    {
        var usual = Usual with { Bands = [new BandComparison(330, 600, Metrics(new Comparison(102, 104, 600), new Comparison(174, 172, 600)))] };

        Assert.Empty(Render(usual).FindAll(".flag"));
    }

    [Fact]
    public void A_strap_run_shows_the_strap_metrics()
    {
        var usual = new UsualForm(3, true, new BandComparison(null, 600, Metrics(null, null, strap: true)), []);

        var card = Render(usual);

        Assert.Contains("Ground contact (strap)", card.Markup);
        Assert.Contains("only against runs with the chest strap", Text(card.Find(".intro")));
        Assert.Empty(card.FindAll("table"));
    }
}
