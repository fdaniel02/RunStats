using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Features.Runs;
using RunStats.Shared;

namespace RunStats.Tests.Features;

public class RunsPageTests : BunitContext
{
    sealed class FakeQueries(RunListData data) : IRunListQueries
    {
        public List<(RunFilter Filter, int Page)> Requests { get; } = [];

        public Task<RunListData> LoadAsync(RunFilter filter, int page, CancellationToken cancellationToken)
        {
            Requests.Add((filter, page));
            return Task.FromResult(data);
        }
    }

    static readonly RunRow Amsterdam = new(2, new DateOnly(2026, 10, 17), "running", "Amsterdam Running", 7500, 2397,
        new RunRowMetrics(3.13, 4.5, 162, 181, 171, 186, 109.7, 512.4, 12, 14));
    static readonly RunRow Treadmill = new(1, new DateOnly(2026, 10, 9), "treadmill_running", "Treadmill", 6100, 2104,
        new RunRowMetrics(2.9, null, 140, 155, 166, 170, 104, 380, null, null));

    static RunListData Data(RunFilter? filter = null, IReadOnlyList<string>? typeKeys = null, bool hasRaces = true,
        int count = 2, int page = 1, int pageCount = 1, IReadOnlyList<RunRow>? runs = null) =>
        new(typeKeys ?? ["running", "treadmill_running"], hasRaces, filter ?? new RunFilter(),
            count, 13_600, 4501, page, pageCount, runs ?? [Amsterdam, Treadmill]);

    FakeQueries queries = null!;

    IRenderedComponent<RunStats.Features.Runs.Runs> Render(RunListData data, string uri = "/runs")
    {
        queries = new FakeQueries(data);
        Services.AddSingleton<IRunListQueries>(queries);
        Services.GetRequiredService<NavigationManager>().NavigateTo(uri);
        return Render<RunStats.Features.Runs.Runs>();
    }

    [Fact]
    public void Filters_come_from_the_address()
    {
        Render(Data(), "/runs?from=2026-01-01&to=2026-10-09&type=running&races=true&page=2");

        Assert.Equal([(new RunFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 9), "running", true), 2)], queries.Requests);
    }

    [Fact]
    public void Empty_form_fields_mean_no_filter()
    {
        Render(Data(), "/runs?from=&to=&type=");

        Assert.Equal([(new RunFilter(), 1)], queries.Requests);
    }

    [Theory]
    [InlineData("/runs?from=09-10-2026&to=2026-13-01&races=yes&page=abc")]
    [InlineData("/runs?page=-2")]
    public void Unreadable_values_are_ignored(string uri)
    {
        Render(Data(), uri);

        Assert.Equal([(new RunFilter(), 1)], queries.Requests);
    }

    [Fact]
    public void The_form_shows_the_applied_filter()
    {
        var page = Render(Data(new RunFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 9), "treadmill_running", true)));

        Assert.Equal("2026-01-01", page.Find("input[name=from]").GetAttribute("value"));
        Assert.Equal("2026-10-09", page.Find("input[name=to]").GetAttribute("value"));
        Assert.Equal(["treadmill_running"], page.FindAll("select[name=type] option[selected]").Select(o => o.GetAttribute("value")));
        Assert.True(page.Find("input[name=races]").HasAttribute("checked"));
    }

    [Fact]
    public void Without_a_filter_the_form_is_empty()
    {
        var page = Render(Data());

        Assert.False(page.Find("input[name=from]").HasAttribute("value"));
        Assert.False(page.Find("input[name=to]").HasAttribute("value"));
        Assert.Empty(page.FindAll("select[name=type] option[selected]"));
        Assert.False(page.Find("input[name=races]").HasAttribute("checked"));
    }

    [Fact]
    public void Type_choices_use_readable_names()
    {
        var options = Render(Data()).FindAll("select[name=type] option");

        Assert.Equal(["All types", "Run", "Treadmill"], options.Select(o => o.TextContent));
        Assert.Equal(["", "running", "treadmill_running"], options.Select(o => o.GetAttribute("value")));
    }

    [Fact]
    public void The_form_reloads_this_page()
    {
        var form = Render(Data()).Find("form");

        Assert.Equal(("get", "runs"), (form.GetAttribute("method"), form.GetAttribute("action")));
        Assert.Equal("runs", form.QuerySelector(".actions a")!.GetAttribute("href"));
    }

    [Fact]
    public void A_single_type_and_no_races_need_no_such_filters()
    {
        var page = Render(Data(typeKeys: ["running"], hasRaces: false));

        Assert.Empty(page.FindAll("select[name=type]"));
        Assert.Empty(page.FindAll("input[name=races]"));
    }

    [Fact]
    public void Summary_counts_the_selection()
    {
        Assert.Equal("2 runs · 13.6 km · 1 h 15 min", Render(Data()).Find(".summary").TextContent);
    }

    [Fact]
    public void Summary_of_one_run()
    {
        Assert.StartsWith("1 run ·", Render(Data(count: 1, runs: [Amsterdam])).Find(".summary").TextContent);
    }

    [Fact]
    public void Runs_are_listed_with_their_names_and_iso_dates()
    {
        var rows = Render(Data()).FindAll("tbody tr");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["2026-10-17", "Amsterdam Running", "Run", "7.5", "39:57", "5:20 / 3:42"], rows[0].Children.Take(6).Select(c => c.TextContent));
    }

    [Fact]
    public void The_pace_unit_is_in_the_header_like_the_other_columns()
    {
        var page = Render(Data());

        Assert.Equal("Pace/km", page.FindAll("thead th")[5].TextContent);
        Assert.Equal("/km", page.FindAll("thead th")[5].QuerySelector(".unit")!.TextContent);
    }

    [Fact]
    public void Max_pace_is_shown_only_on_wide_screens()
    {
        var pace = Render(Data()).FindAll("tbody tr")[0].Children[5];

        Assert.Equal(" / 3:42", pace.QuerySelector("span.metric")!.TextContent);
    }

    [Fact]
    public void Without_a_max_speed_the_max_pace_is_missing()
    {
        Assert.Equal("5:45 / –", Render(Data()).FindAll("tbody tr")[1].Children[5].TextContent);
    }

    [Fact]
    public void Runs_show_their_metrics_with_units_in_the_headers()
    {
        var page = Render(Data());

        Assert.Equal(["Speedkm/h", "HRbpm", "Cadencespm", "Stridem", "Energykcal", "Ascent / descentm"],
            page.FindAll("th.metric").Select(th => th.TextContent));
        var rows = page.FindAll("tbody tr");
        Assert.Equal(["11.3 / 16.2", "162 / 181", "171 / 186", "1.10", "512", "12 / 14"], rows[0].QuerySelectorAll("td.metric").Select(c => c.TextContent));
        Assert.Equal(["10.4 / –", "140 / 155", "166 / 170", "1.04", "380", "–"], rows[1].QuerySelectorAll("td.metric").Select(c => c.TextContent));
    }

    [Fact]
    public void An_empty_selection_says_so()
    {
        var page = Render(Data(count: 0, runs: []));

        Assert.Equal("No runs match these filters.", page.Find("p.muted").TextContent);
        Assert.Empty(page.FindAll(".summary"));
    }

    [Fact]
    public void Without_runs_there_is_no_form()
    {
        var page = Render(Data(typeKeys: [], hasRaces: false, count: 0, runs: []));

        Assert.Equal("No runs yet.", page.Find("p.muted").TextContent);
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void A_single_page_has_no_pager()
    {
        Assert.Empty(Render(Data()).FindAll("nav.pager"));
    }

    [Fact]
    public void Page_links_keep_the_filter()
    {
        var page = Render(Data(new RunFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 9), "treadmill_running", true), page: 2, pageCount: 3));

        Assert.Equal("Page 2 of 3", page.Find(".pager span").TextContent);
        Assert.Equal("runs?from=2026-01-01&to=2026-10-09&type=treadmill_running&races=true", page.Find("a[rel=prev]").GetAttribute("href"));
        Assert.Equal("runs?from=2026-01-01&to=2026-10-09&type=treadmill_running&races=true&page=3", page.Find("a[rel=next]").GetAttribute("href"));
    }

    [Fact]
    public void The_first_page_links_only_to_older_runs()
    {
        var page = Render(Data(page: 1, pageCount: 2));

        Assert.Empty(page.FindAll("a[rel=prev]"));
        Assert.Equal("runs?page=2", page.Find("a[rel=next]").GetAttribute("href"));
    }

    [Fact]
    public void The_last_page_links_only_to_newer_runs()
    {
        var page = Render(Data(page: 2, pageCount: 2));

        Assert.Equal("runs", page.Find("a[rel=prev]").GetAttribute("href"));
        Assert.Empty(page.FindAll("a[rel=next]"));
    }
}
