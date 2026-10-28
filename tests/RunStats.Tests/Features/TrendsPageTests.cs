using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class TrendsPageTests : BunitContext
{
    sealed class FakeQueries(TrendsData data) : ITrendsQueries
    {
        public Task<TrendsData> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(data);
    }

    sealed class FakeForm(FormData data) : IFormQueries
    {
        public Task<FormData> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(data);
    }

    static readonly FormData Form = new(
        [new FormTile(FormMetric.StrideLength, new Comparison(102.0, 103.2, 3600))],
        [new FormSeries(FormMetric.StrideLength, [new FormMonth(new DateOnly(2026, 9, 1), new Comparison(102.0, 102.5, 7200), 7200)])],
        [new BalanceRun(1, new DateOnly(2026, 9, 22), 50.3)]);

    static TrendsData Data(IReadOnlyList<Warning>? active = null, IReadOnlyList<WarningEpisode>? episodes = null, double? typicalPace = 328) => new(
        active ?? [],
        episodes ?? [],
        [new LoadWeek(new DateOnly(2026, 10, 26), 35 * 60, 0, 0, [], true)],
        typicalPace,
        310,
        255);

    IRenderedComponent<Trends> Render(TrendsData data)
    {
        Services.AddSingleton<ITrendsQueries>(new FakeQueries(data));
        Services.AddSingleton<IFormQueries>(new FakeForm(Form));
        return Render<Trends>();
    }

    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Active_warnings_are_listed()
    {
        var page = Render(Data(active: [new Warning(WarningKind.VeryFastJump, "9 min faster than 4:15 /km in the last 7 days.")]));

        Assert.Equal(["!Very fast running 9 min faster than 4:15 /km in the last 7 days."], page.FindAll(".active li").Select(Text));
    }

    [Fact]
    public void Past_warnings_show_their_days()
    {
        var page = Render(Data(episodes:
        [
            new WarningEpisode(WarningKind.HardRunsBackToBack, new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 30), "Hard runs on 2026-09-24 and 2026-09-27, 68 h apart."),
            new WarningEpisode(WarningKind.FastJump, new DateOnly(2026, 7, 24), new DateOnly(2026, 7, 24), "53 min faster than 5:10 /km.")
        ]));

        Assert.Equal(
            ["2026-09-27 – 2026-09-30 Hard runs back to back Hard runs on 2026-09-24 and 2026-09-27, 68 h apart.",
             "2026-07-24 Fast running 53 min faster than 5:10 /km."],
            page.FindAll(".episodes li").Select(Text));
        Assert.Contains("No warnings now.", page.Find(".warnings").TextContent);
    }

    [Fact]
    public void Without_warnings_both_lists_say_so()
    {
        var page = Render(Data());

        Assert.Contains("No warnings now.", page.Markup);
        Assert.Contains("No warnings in the last 26 weeks.", page.Markup);
        Assert.Empty(page.FindAll(".episodes li"));
    }

    [Fact]
    public void The_typical_pace_stands_next_to_the_fast_setting()
    {
        var page = Render(Data());

        Assert.Equal("Typical pace (median of the last 8 weeks, outdoor runs): 5:28 /km · fast is faster than 5:10 /km", Text(page.Find(".pace")));
        Assert.Single(page.FindAll(".load"));
    }

    [Fact]
    public void Without_outdoor_runs_the_typical_pace_is_missing() =>
        Assert.Contains("outdoor runs): – ·", Text(Render(Data(typicalPace: null)).Find(".pace")));

    [Fact]
    public void Form_and_balance_follow_the_load()
    {
        var page = Render(Data());

        Assert.Equal(["Warnings", "Running time per week", "Form at the same pace", "Left/right balance"], page.FindAll("h2").Select(h => h.TextContent));
        Assert.Single(page.FindAll(".tile"));
        Assert.Single(page.FindAll(".form-charts figure"));
        Assert.Single(page.FindAll(".runs .slot"));
    }
}
