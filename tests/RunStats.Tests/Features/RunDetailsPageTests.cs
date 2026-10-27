using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Data;
using RunStats.Features.Runs;

namespace RunStats.Tests.Features;

public class RunDetailsPageTests : BunitContext
{
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
        ElevationGainMeters = 2,
        ElevationLossMeters = 0
    };

    static RunDetailsData Data(Run? run = null, IReadOnlyList<RunLap>? laps = null, string? polyline = "abc") =>
        new(run ?? Amsterdam(), laps ?? [Lap(1), Lap(2, meters: 500, seconds: 163)], polyline, Points);

    static readonly ChartPoint[] Points = [new(0, 0, 330, 150, 5, 170, 1.05, 51.6, 5.5), new(7.5, 2397, 300, 170, 6, 180, 1.1, 51.61, 5.51)];

    FakeQueries queries = null!;
    readonly List<NotFoundEventArgs> notFound = [];

    IRenderedComponent<RunDetails> Render(RunDetailsData? data, long id = 24758031152, string? cartoKey = null)
    {
        queries = new FakeQueries(data);
        Services.AddSingleton<IRunDetailsQueries>(queries);
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
    public void The_summary_shows_a_tile_per_recorded_stat()
    {
        var tiles = Render(Data()).FindAll(".tile");

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

        Assert.Equal(["1", "1.0", "5:29", "5:29 / 4:27 /km", "154 / 181", "172", "1.05", "2 / 0"], rows[0].Children.Select(c => c.TextContent));
        Assert.Equal(["2", "0.5", "2:43", "5:26 / 4:27 /km"], rows[1].Children.Take(4).Select(c => c.TextContent));
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
}
