using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Features.Heatmap;

namespace RunStats.Tests.Features;

public class HeatmapPageTests : BunitContext
{
    sealed class FakeQueries(HeatmapData data) : IHeatmapQueries
    {
        public List<(int? Year, string? TypeKey)> Requests { get; } = [];

        public Task<HeatmapData> LoadAsync(int? year, string? typeKey, CancellationToken cancellationToken)
        {
            Requests.Add((year, typeKey));
            return Task.FromResult(data);
        }
    }

    static readonly TrackRow Buffel = new(3, new DateOnly(2026, 8, 22), "obstacle_run", "Buffel run", 7200, "abc");
    static readonly TrackRow Eindhoven = new(1, new DateOnly(2025, 10, 12), "running", "Eindhoven </script> & \"half\"", 21100, "def");

    static HeatmapData Data(int? year = null, string? typeKey = null, IReadOnlyList<int>? years = null, IReadOnlyList<string>? typeKeys = null,
        IReadOnlyList<TrackRow>? tracks = null) =>
        new(years ?? [2026, 2025], typeKeys ?? ["obstacle_run", "running"], year, typeKey, tracks ?? [Eindhoven, Buffel]);

    FakeQueries queries = null!;

    IRenderedComponent<RunStats.Features.Heatmap.Heatmap> Render(HeatmapData data, string uri = "/heatmap", string? cartoKey = null)
    {
        queries = new FakeQueries(data);
        Services.AddSingleton<IHeatmapQueries>(queries);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(cartoKey is null ? [] : [new KeyValuePair<string, string?>("Heatmap:CartoApiKey", cartoKey)])
            .Build());
        Services.GetRequiredService<NavigationManager>().NavigateTo(uri);
        return Render<RunStats.Features.Heatmap.Heatmap>();
    }

    [Fact]
    public void Filters_come_from_the_address()
    {
        Render(Data(), "/heatmap?year=2025&type=running");

        Assert.Equal([(2025, "running")], queries.Requests);
    }

    [Fact]
    public void Period_links_keep_the_type_and_mark_the_current_one()
    {
        var page = Render(Data(year: 2025, typeKey: "running"));

        var links = page.FindAll("nav[aria-label=Period] a");
        Assert.Equal(["All time", "2026", "2025"], links.Select(a => a.TextContent));
        Assert.Equal(["heatmap?type=running", "heatmap?year=2026&type=running", "heatmap?year=2025&type=running"], links.Select(a => a.GetAttribute("href")));
        Assert.Equal(["2025"], links.Where(a => a.GetAttribute("aria-current") == "page").Select(a => a.TextContent));
    }

    [Fact]
    public void Type_links_keep_the_year_and_use_readable_names()
    {
        var page = Render(Data(year: 2026));

        var links = page.FindAll("nav[aria-label=Type] a");
        Assert.Equal(["All types", "Obstacle run", "Run"], links.Select(a => a.TextContent));
        Assert.Equal(["heatmap?year=2026", "heatmap?year=2026&type=obstacle_run", "heatmap?year=2026&type=running"], links.Select(a => a.GetAttribute("href")));
        Assert.Equal(["All types"], links.Where(a => a.GetAttribute("aria-current") == "page").Select(a => a.TextContent));
    }

    [Fact]
    public void With_all_filters_off_the_links_have_no_query()
    {
        var page = Render(Data());

        Assert.Equal("heatmap", page.Find("nav[aria-label=Period] a").GetAttribute("href"));
        Assert.Equal("page", page.Find("nav[aria-label=Period] a").GetAttribute("aria-current"));
    }

    [Fact]
    public void Filter_links_do_full_page_loads()
    {
        var page = Render(Data());

        Assert.All(page.FindAll(".chips a"), a => Assert.Equal("false", a.GetAttribute("data-enhance-nav")));
    }

    [Fact]
    public void A_single_type_needs_no_type_filter()
    {
        var page = Render(Data(typeKeys: ["running"]));

        Assert.Empty(page.FindAll("nav[aria-label=Type]"));
    }

    [Fact]
    public void Summary_counts_the_shown_runs_and_their_distance()
    {
        Assert.Equal("2 runs · 28.3 km", Render(Data()).Find(".summary").TextContent);
    }

    [Fact]
    public void Summary_of_one_run()
    {
        Assert.Equal("1 run · 7.2 km", Render(Data(tracks: [Buffel])).Find(".summary").TextContent);
    }

    [Fact]
    public void Tracks_go_to_the_map_script_as_json_with_readable_values()
    {
        var page = Render(Data());

        var json = page.Find("#heatmap-data").InnerHtml;
        Assert.DoesNotContain("</script>", json);
        var runs = JsonDocument.Parse(json).RootElement;
        Assert.Equal(2, runs.GetArrayLength());
        var first = runs[0];
        Assert.Equal("def", first.GetProperty("polyline").GetString());
        Assert.Equal("Eindhoven </script> & \"half\"", first.GetProperty("name").GetString());
        Assert.Equal("Run", first.GetProperty("type").GetString());
        Assert.Equal("2025-10-12", first.GetProperty("date").GetString());
        Assert.Equal("21.1", first.GetProperty("km").GetString());
    }

    [Fact]
    public void The_carto_key_reaches_the_map_when_configured()
    {
        Assert.Equal("my-key", Render(Data(), cartoKey: "my-key").Find("#heatmap").GetAttribute("data-carto-key"));
    }

    [Fact]
    public void Without_a_carto_key_the_map_gets_none()
    {
        Assert.False(Render(Data()).Find("#heatmap").HasAttribute("data-carto-key"));
    }

    [Fact]
    public void Without_runs_with_gps_there_is_no_map()
    {
        var page = Render(Data(years: [], typeKeys: [], tracks: []));

        Assert.Equal("No runs with GPS yet.", page.Find("p.muted").TextContent);
        Assert.Empty(page.FindAll("#heatmap"));
        Assert.Empty(page.FindAll("script"));
    }
}
