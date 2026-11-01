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

    sealed class FakeEfforts(IReadOnlyList<EffortSeries> series) : IBestEffortQueries
    {
        public Task<IReadOnlyList<EffortSeries>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(series);
    }

    sealed class FakeEfficiency(EfficiencyData data) : IEfficiencyQueries
    {
        public Task<EfficiencyData> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(data);
    }

    static readonly MonthEffort Effort =new(1429, 1, new DateOnly(2026, 9, 27), "Morning run");

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
        255,
        [new PaceVariationMonth(new DateOnly(2026, 9, 1), 3.2, 9)]);

    IRenderedComponent<Trends> Render(TrendsData data, IReadOnlyList<EffortSeries>? efforts = null)
    {
        Services.AddSingleton<ITrendsQueries>(new FakeQueries(data));
        Services.AddSingleton<IFormQueries>(new FakeForm(Form));
        Services.AddSingleton<IBestEffortQueries>(new FakeEfforts(efforts ?? [new EffortSeries(3, Effort, [new EffortMonth(new DateOnly(2026, 9, 1), Effort)])]));
        Services.AddSingleton<IEfficiencyQueries>(new FakeEfficiency(new EfficiencyData(150, 160, 30, [new EfficiencyMonth(new DateOnly(2026, 9, 1), 3600, 5, 341, 14)])));
        return Render<Trends>();
    }

    [Fact]
    public void The_pace_at_the_same_heart_rate_names_its_band()
    {
        var section = Render(Data()).Find(".efficiency");

        Assert.Equal("Pace at 150–160 bpm", section.QuerySelector("h2")!.TextContent);
        Assert.Single(section.QuerySelectorAll(".dot"));
    }

    [Fact]
    public void Best_efforts_have_their_own_section()
    {
        var section = Render(Data()).Find(".best-efforts");

        Assert.Equal("Best efforts", section.QuerySelector("h2")!.TextContent);
        Assert.Equal(["runs/1"], section.QuerySelectorAll("a.dot").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void Without_best_efforts_the_section_says_so()
    {
        var section = Render(Data(), efforts: []).Find(".best-efforts");

        Assert.Contains("No best efforts yet.", section.TextContent);
        Assert.Empty(section.QuerySelectorAll(".tabs"));
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
    public void Best_efforts_form_and_balance_follow_the_load()
    {
        var page = Render(Data());

        Assert.Equal(["Warnings", "Running time per week", "Pace variation", "Best efforts", "Pace at 150–160 bpm", "Form at the same pace", "Left/right balance"],
            page.FindAll("h2").Select(h => h.TextContent));
        Assert.Single(page.FindAll(".tile"));
        Assert.Single(page.FindAll(".form-charts figure"));
        Assert.Single(page.FindAll(".runs .slot"));
    }
}
