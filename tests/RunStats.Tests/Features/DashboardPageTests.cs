using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Data;
using RunStats.Features.Dashboard;
using RunStats.Shared;

namespace RunStats.Tests.Features;

public class DashboardPageTests : BunitContext
{
    sealed class FakeQueries(DashboardData data) : IDashboardQueries
    {
        public List<int?> RequestedRaceYears { get; } = [];

        public Task<DashboardData> LoadAsync(int? raceYear, CancellationToken cancellationToken)
        {
            RequestedRaceYears.Add(raceYear);
            return Task.FromResult(data);
        }
    }

    static readonly DateOnly ThisMonday = new(2026, 10, 26);

    static IReadOnlyList<WeekTotal> Weeks(params (double Km, double Hours)[] values) =>
    [
        .. values.Select((v, i) => new WeekTotal(ThisMonday.AddDays(7 * (i - values.Length + 1)), v.Km * 1000, v.Hours * 3600, i == values.Length - 1))
    ];

    static DashboardData Data(
        IReadOnlyList<WeekTotal>? weeks = null, IReadOnlyList<RunRow>? lastRuns = null, IReadOnlyList<RecordRow>? records = null,
        IReadOnlyList<int>? raceYears = null, int? raceYear = 2026, IReadOnlyList<RunRow>? races = null) => new(
        new Totals(84, 710_800, 68 * 3600 + 55 * 60),
        new Totals(1, 23_700, 2 * 3600 + 15 * 60),
        weeks ?? Weeks((12, 1), (0, 0), (33.1, 3.2), (6, 0.5)),
        lastRuns ?? [new RunRow(1, new DateOnly(2026, 10, 26), "treadmill_running", "Treadmill", 6100, 2104)],
        records ?? [new RecordRow(3, 1429, new DateOnly(2026, 9, 27), 1), new RecordRow(7, 21530, new DateOnly(2025, 10, 12), 2)],
        raceYears ?? [2026, 2025],
        raceYear,
        races ?? [new RunRow(2, new DateOnly(2026, 10, 17), "running", "Amsterdam Running", 7500, 2397)]);

    FakeQueries queries = null!;

    IRenderedComponent<RunStats.Features.Dashboard.Dashboard> Render(DashboardData data, string uri = "/")
    {
        queries = new FakeQueries(data);
        Services.AddSingleton<IDashboardQueries>(queries);
        Services.AddSingleton(new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero))));
        Services.GetRequiredService<NavigationManager>().NavigateTo(uri);
        return Render<RunStats.Features.Dashboard.Dashboard>();
    }

    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Totals_show_km_runs_and_time_for_this_year_and_month()
    {
        var page = Render(Data());

        var totals = page.FindAll(".total").Select(Text).ToList();
        Assert.Equal(["This year (2026) 710.8 km 84 runs · 68 h 55 min", "This month (October) 23.7 km 1 run · 2 h 15 min"], totals);
    }

    [Fact]
    public void Last_runs_show_date_type_distance_time_and_pace()
    {
        var page = Render(Data());

        var cells = page.Find(".last-runs tbody tr").QuerySelectorAll("td").Select(c => c.TextContent).ToList();
        Assert.Equal(["2026-10-26", "Treadmill", "6.1", "35:04", "5:45 /km"], cells);
        Assert.Empty(page.Find(".last-runs").QuerySelectorAll("td.name"));
    }

    [Fact]
    public void Records_show_name_value_and_date()
    {
        var page = Render(Data());

        Assert.Equal(["5 km 23:49 2026-09-27", "Longest run 21.5 km 2025-10-12"], page.FindAll(".record-list li").Select(Text));
    }

    [Fact]
    public void Races_include_their_names_under_their_year()
    {
        var page = Render(Data());

        Assert.Equal("Races 2026", page.Find(".races h2").TextContent);
        var cells = page.Find(".races tbody tr").QuerySelectorAll("td").Select(c => c.TextContent).ToList();
        Assert.Equal(["2026-10-17", "Amsterdam Running", "Run", "7.5", "39:57", "5:20 /km"], cells);
    }

    [Fact]
    public void Years_with_races_link_to_their_races_and_the_shown_year_is_marked()
    {
        var page = Render(Data(raceYears: [2026, 2025, 2024], raceYear: 2025));

        var links = page.FindAll(".years a");
        Assert.Equal(["2026", "2025", "2024"], links.Select(a => a.TextContent));
        Assert.Equal(["?races=2026#races", "?races=2025#races", "?races=2024#races"], links.Select(a => a.GetAttribute("href")));
        Assert.Equal(["2025"], links.Where(a => a.GetAttribute("aria-current") == "page").Select(a => a.TextContent));
    }

    [Fact]
    public void The_race_year_comes_from_the_address()
    {
        Render(Data(), "/?races=2025");

        Assert.Equal([2025], queries.RequestedRaceYears);
    }

    [Fact]
    public void Without_a_race_year_in_the_address_the_queries_choose()
    {
        Render(Data());

        Assert.Equal([(int?)null], queries.RequestedRaceYears);
    }

    [Fact]
    public void A_single_year_with_races_needs_no_switch()
    {
        var page = Render(Data(raceYears: [2026]));

        Assert.Empty(page.FindAll(".years"));
    }

    [Fact]
    public void Empty_sections_say_so()
    {
        var page = Render(Data(weeks: Weeks((0, 0), (0, 0)), lastRuns: [], records: [], raceYears: [], raceYear: null, races: []));

        Assert.Equal("No runs yet.", page.Find(".last-runs p").TextContent);
        Assert.Equal("No personal records yet.", page.Find(".records p").TextContent);
        Assert.Equal("Races", page.Find(".races h2").TextContent.Trim());
        Assert.Equal("No races yet.", page.Find(".races p").TextContent);
        Assert.Equal("No runs in the last 2 weeks.", page.Find(".weekly .empty").TextContent);
        Assert.Empty(page.FindAll(".plot"));
    }

    [Fact]
    public void Chart_has_a_distance_and_a_time_plot_and_starts_with_distance()
    {
        var page = Render(Data());

        Assert.Equal(["plot distance", "plot time"], page.FindAll(".plot").Select(p => p.ClassName));
        Assert.True(page.Find("#weekly-distance").HasAttribute("checked"));
        Assert.False(page.Find("#weekly-time").HasAttribute("checked"));
    }

    [Fact]
    public void Bars_are_scaled_to_a_round_axis_and_only_the_largest_is_labelled()
    {
        var page = Render(Data());

        var distance = page.Find(".plot.distance");
        Assert.Equal(["height: 30%", "height: 0%", "height: 82.75%", "height: 15%"], distance.QuerySelectorAll(".bar").Select(b => b.GetAttribute("style")));
        Assert.Equal(["0", "10", "20", "30", "40"], distance.QuerySelectorAll(".gridline span").Select(s => s.TextContent));
        Assert.Equal(["33.1 km"], distance.QuerySelectorAll(".value").Select(v => v.TextContent));
        Assert.Equal(["0 h", "1 h", "2 h", "3 h", "4 h"], page.Find(".plot.time").QuerySelectorAll(".gridline span").Select(s => s.TextContent));
    }

    [Fact]
    public void Each_bar_names_its_week_and_value_for_hover_and_screen_readers()
    {
        var page = Render(Data());

        var slots = page.Find(".plot.distance").QuerySelectorAll(".slot");
        Assert.Equal("2026-10-05 – 2026-10-11: 12.0 km", slots[0].GetAttribute("aria-label"));
        Assert.Equal("2026-10-05 – 2026-10-11: 12.0 km", slots[0].QuerySelector(".tip")!.TextContent);
        Assert.Equal("2026-10-05", slots[0].QuerySelector(".x")!.TextContent);
        Assert.Equal("This week (from 2026-10-26): 6.0 km", slots[^1].GetAttribute("aria-label"));
        Assert.Equal("slot current", slots[^1].ClassName!.Trim());
        Assert.Equal("This week", slots[^1].QuerySelector(".x")!.TextContent);
        Assert.Equal("This week (from 2026-10-26): 30 min", page.Find(".plot.time").QuerySelectorAll(".slot")[^1].GetAttribute("aria-label"));
    }

    [Fact]
    public void The_table_view_lists_every_week()
    {
        var page = Render(Data());

        var rows = page.FindAll(".as-table tbody tr").Select(r => r.QuerySelectorAll("td").Select(c => c.TextContent).ToList()).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(["2026-10-12 – 2026-10-18", "0.0", "0 min"], rows[1]);
        Assert.Equal(["This week (from 2026-10-26)", "6.0", "30 min"], rows[3]);
    }
}
