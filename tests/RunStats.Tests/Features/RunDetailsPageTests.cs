using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RunStats.Data;
using RunStats.Features.Runs;
using RunStats.Features.Trends;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class RunDetailsPageTests : BunitContext
{
    sealed class FakeAnalysis(RunAnalysis? analysis) : IRunAnalysisQueries
    {
        public Task<RunAnalysis?> LoadAsync(long runId, CancellationToken cancellationToken) => Task.FromResult(analysis);
    }

    static RunAnalysis Analysis(IReadOnlyList<Warning>? triggered = null, DriftMissing? driftMissing = DriftMissing.TooShort, Comparison? driftCadence = null)
    {
        Dictionary<FormMetric, Comparison?> none = RunAnalysisQueries.UsualMetrics(false).ToDictionary(m => m, _ => (Comparison?)null);
        var drift = RunAnalysisQueries.DriftMetrics(false).ToDictionary(m => m, m => m == FormMetric.Cadence ? driftCadence : null);
        return new RunAnalysis(new UsualForm(0, false, new BandComparison(null, 0, none), []), new FormDrift(drift, driftMissing), triggered ?? []);
    }

    sealed class FakeQueries(RunDetailsData? data) : IRunDetailsQueries
    {
        public List<long> Requests { get; } = [];

        public Task<RunDetailsData?> LoadAsync(long id, CancellationToken cancellationToken)
        {
            Requests.Add(id);
            return Task.FromResult(data);
        }
    }

    static Run Amsterdam(string name = "Amsterdam Running", bool race = true) => new()
    {
        Id = 24758031152,
        Name = name,
        TypeKey = "running",
        IsRace = race,
        StartLocal = new DateTime(2026, 10, 17, 15, 7, 31),
        DistanceMeters = 7500,
        DurationSeconds = 2397,
        AverageHeartRate = 163
    };

    static RunLap Lap(int index, string? intensity = "ACTIVE", double meters = 1000, double seconds = 329) => new()
    {
        LapIndex = index,
        IntensityType = intensity,
        DistanceMeters = meters,
        DurationSeconds = seconds,
        AverageHeartRate = 154,
        MaxHeartRate = 181,
        AverageCadenceSpm = 172,
        StrideLengthCm = 104.6,
        MaxSpeedMps = 3.75,
        PaceVariationPercent = 3.24,
        ElevationGainMeters = 2,
        ElevationLossMeters = 0
    };

    static RunDetailsData Data(Run? run = null, IReadOnlyList<RunLap>? laps = null, string? polyline = "abc") =>
        new(run ?? Amsterdam(), laps ?? [Lap(1), Lap(2, meters: 500, seconds: 163)], polyline, Points);

    static readonly ChartPoint[] Points = [new(0, 0, 330, 150, 5, 170, 1.05, 51.6, 5.5), new(7.5, 2397, 300, 170, 6, 180, 1.1, 51.61, 5.51)];

    FakeQueries queries = null!;
    readonly List<NotFoundEventArgs> notFound = [];

    IRenderedComponent<RunDetails> Render(RunDetailsData? data, long id = 24758031152, string? cartoKey = null, RunAnalysis? analysis = null)
    {
        queries = new FakeQueries(data);
        Services.AddSingleton<IRunDetailsQueries>(queries);
        Services.AddSingleton<IRunAnalysisQueries>(new FakeAnalysis(analysis ?? Analysis()));
        Services.AddSingleton(Options.Create(new WarningSettings()));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(cartoKey is null ? [] : [new KeyValuePair<string, string?>("Heatmap:CartoApiKey", cartoKey)])
            .Build());
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, e) => notFound.Add(e);
        return Render<RunDetails>(p => p.Add(c => c.Id, id));
    }

    [Fact]
    public void Loads_the_run_from_the_address()
    {
        Render(Data(), id: 42);

        Assert.Equal([42L], queries.Requests);
    }

    [Fact]
    public void The_header_has_the_name_start_type_and_a_garmin_link()
    {
        var page = Render(Data());

        Assert.Equal("Amsterdam Running", page.Find("h1").TextContent);
        Assert.Equal("Race", page.Find(".badge").TextContent);
        Assert.StartsWith("2026-10-17 15:07 · Run ·", page.Find(".run-head p").TextContent.Trim());
        var garmin = page.Find(".run-head p a");
        Assert.Equal("https://connect.garmin.com/modern/activity/24758031152", garmin.GetAttribute("href"));
        Assert.Equal("_blank", garmin.GetAttribute("target"));
        Assert.Equal("runs", page.Find("a.back").GetAttribute("href"));
    }

    [Fact]
    public void A_run_without_a_name_is_named_after_its_type()
    {
        Assert.Equal("Run", Render(Data(Amsterdam(name: "", race: false))).Find("h1").TextContent);
    }

    [Fact]
    public void Only_races_get_the_race_label()
    {
        Assert.Empty(Render(Data(Amsterdam(race: false))).FindAll(".badge"));
    }

    [Fact]
    public void The_primary_benefit_is_a_badge_after_the_race_badge_with_its_shade()
    {
        var run = Amsterdam();
        run.TrainingEffectLabel = "TEMPO";

        var page = Render(Data(run));

        Assert.Equal(["Race", "Tempo"], page.FindAll(".title .badge").Select(b => b.TextContent));
        Assert.Equal("effect-swatch effect-3", page.Find(".badge.benefit .effect-swatch").ClassName);
        Assert.DoesNotContain(page.FindAll("section[aria-label=Summary] .tile-label"), l => l.TextContent == "Primary benefit");
    }

    [Fact]
    public void A_label_without_a_level_gets_the_plain_swatch()
    {
        var run = Amsterdam(race: false);
        run.TrainingEffectLabel = "NEW_KIND_OF_RUN";

        var badge = Render(Data(run)).Find(".badge.benefit");

        Assert.Equal(("New kind of run", "effect-swatch"), (badge.TextContent, badge.QuerySelector(".effect-swatch")!.ClassName?.Trim()));
    }

    [Fact]
    public void The_summary_shows_a_tile_per_recorded_stat()
    {
        var tiles = Render(Data()).FindAll("section[aria-label=Summary] .tile");

        Assert.Equal(["Distance", "Time", "Pace", "Heart rate"], tiles.Select(t => t.QuerySelector(".tile-label")!.TextContent));
        Assert.Equal("163 bpm", tiles[3].QuerySelector(".tile-value")!.TextContent);
        Assert.Null(tiles[3].QuerySelector(".tile-detail"));
    }

    [Fact]
    public void The_route_goes_to_the_map_script()
    {
        var page = Render(Data(), cartoKey: "my-key");

        Assert.Equal("my-key", page.Find("#route").GetAttribute("data-carto-key"));
        Assert.Equal("abc", JsonDocument.Parse(page.Find("#route-data").InnerHtml).RootElement.GetProperty("polyline").GetString());
    }

    [Fact]
    public void A_run_without_gps_has_no_map()
    {
        var page = Render(Data(polyline: null));

        Assert.Empty(page.FindAll("#route"));
        Assert.Empty(page.FindAll("#route-data"));
    }

    [Fact]
    public void The_charts_come_after_the_map()
    {
        var page = Render(Data());

        Assert.Equal(["route", "charts"], page.FindAll("#route, #charts").Select(e => e.Id));
    }

    [Fact]
    public void Laps_are_listed()
    {
        var rows = Render(Data()).FindAll("table.laps tbody tr");

        Assert.Equal(["1", "1.0", "5:29", "5:29 / 4:27", "3.2", "154 / 181", "172", "1.05", "2 / 0"], rows[0].Children.Select(c => c.TextContent));
        Assert.Equal(["2", "0.5", "2:43", "5:26 / 4:27"], rows[1].Children.Take(4).Select(c => c.TextContent));
    }

    [Fact]
    public void The_lap_pace_unit_is_in_the_header()
    {
        var header = Render(Data()).FindAll("table.laps th")[3];

        Assert.Equal(("Pace/km", "/km"), (header.TextContent, header.QuerySelector(".unit")!.TextContent));
    }

    [Fact]
    public void The_max_pace_is_hidden_on_a_phone_like_the_other_extra_columns()
    {
        var pace = Render(Data()).FindAll("table.laps tbody tr")[0].Children[3];

        Assert.Equal(" / 4:27", pace.QuerySelector("span.optional")!.TextContent);
    }

    [Fact]
    public void A_lap_without_distance_has_no_pace()
    {
        var row = Render(Data(laps: [Lap(1, meters: 0, seconds: 12)])).Find("table.laps tbody tr");

        Assert.Equal("–", row.Children[3].TextContent);
    }

    [Fact]
    public void Lap_types_show_only_when_they_differ()
    {
        Assert.Empty(Render(Data()).FindAll("table.laps td:nth-child(2):not(.num)"));
    }

    [Fact]
    public void Interval_workouts_show_their_lap_types()
    {
        var page = Render(Data(laps: [Lap(1, "WARMUP"), Lap(2, "INTERVAL"), Lap(3, "RECOVERY"), Lap(4, null)]));

        Assert.Equal("Type", page.FindAll("table.laps th")[1].TextContent);
        Assert.Equal(["Warm-up", "Interval", "Recovery", "–"], page.FindAll("table.laps tbody tr").Select(r => r.Children[1].TextContent));
    }

    [Fact]
    public void A_run_without_laps_has_no_lap_table()
    {
        Assert.Empty(Render(Data(laps: [])).FindAll("table.laps"));
    }

    [Fact]
    public void An_unknown_run_is_not_found()
    {
        var page = Render(null);

        Assert.Single(notFound);
        Assert.Empty(page.FindAll("h1"));
    }

    [Fact]
    public void The_lap_pace_variation_is_an_extra_column_with_its_unit_in_the_header()
    {
        var page = Render(Data());
        var header = page.FindAll("table.laps th")[4];

        Assert.Equal(("Variation±%", true), (header.TextContent, header.ClassList.Contains("optional")));
    }

    [Fact]
    public void A_short_lap_has_no_pace_variation()
    {
        var lap = Lap(1);
        lap.PaceVariationPercent = null;

        Assert.Equal("–", Render(Data(laps: [lap])).Find("table.laps tbody tr").Children[4].TextContent);
    }

    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Warnings_from_the_run_are_listed_above_the_map()
    {
        var page = Render(Data(), analysis: Analysis(triggered: [new Warning(WarningKind.HardRunsBackToBack, "Hard runs on 2026-09-24 and 2026-09-27, 68 h apart.")]));

        var section = page.Find(".triggered");
        Assert.Equal(["Hard runs back to back Hard runs on 2026-09-24 and 2026-09-27, 68 h apart."], section.QuerySelectorAll("li").Select(Text));
        Assert.True(page.Markup.IndexOf("triggered", StringComparison.Ordinal) < page.Markup.IndexOf("id=\"route\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_warnings_from_the_run_there_is_no_warnings_section() => Assert.Empty(Render(Data()).FindAll(".triggered"));

    [Fact]
    public void The_comparison_with_the_usual_and_the_drift_follow_the_charts()
    {
        var titles = Render(Data()).FindAll("h2").Select(h => h.TextContent).ToList();

        Assert.Equal(["Compared to your usual", "Form drift", "Laps"], titles.Where(t => t is "Compared to your usual" or "Form drift" or "Laps"));
    }

    [Theory]
    [InlineData(DriftMissing.TooShort, "Only for runs of 30 min or more.")]
    [InlineData(DriftMissing.NotSteady, "Not enough time at the same pace in the first and the last third")]
    public void Without_drift_the_page_says_why(DriftMissing missing, string text) =>
        Assert.Contains(text, Render(Data(), analysis: Analysis(driftMissing: missing)).Markup);

    [Fact]
    public void The_drift_shows_the_first_and_last_third_in_tiles()
    {
        var page = Render(Data(), analysis: Analysis(driftMissing: null, driftCadence: new Comparison(176, 170, 900)));

        var drift = page.Find("#drift-title").ParentElement!;
        var cadence = drift.QuerySelectorAll(".tile").Single(t => t.TextContent.Contains("Cadence")).Children.Select(Text).ToList();
        Assert.Equal(["Cadence", "−6.0 spm−3.4%", "176.0 → 170.0 spm", "over 15 min at comparable paces"], cadence);
    }

    [Fact]
    public void The_comparison_names_the_runs_type()
    {
        var run = Amsterdam();
        run.TypeKey = "treadmill_running";

        Assert.Contains("Against your treadmill runs", Text(Render(Data(run)).Find("#usual-title").ParentElement!.QuerySelector(".intro")!));
    }
}
