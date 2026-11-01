using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RunStats.Data;
using RunStats.Features.Dashboard;
using RunStats.Features.Years;
using RunStats.Shared;

namespace RunStats.Tests.Features;

public class YearOverviewPageTests : BunitContext
{
    sealed class FakeQueries(YearData? data) : IYearQueries
    {
        public List<int> Requested { get; } = [];

        public Task<YearData?> LoadAsync(int year, CancellationToken cancellationToken)
        {
            Requested.Add(year);
            return Task.FromResult(data);
        }
    }

    static YearData Data(
        IReadOnlyList<int>? years = null, IReadOnlyList<RecordRow>? records = null, IReadOnlyList<RunRow>? races = null,
        IReadOnlyList<RunRow>? runs = null, bool noDistance = false, double? goal = null)
    {
        var weeks = ActivityGrid.ForYear(2025).Weeks.Select((w, i) => new WeekTotal(w.Days[0], noDistance ? 0 : i * 1000, noDistance ? 0 : i * 300, false)).ToList();
        var months = Enumerable.Range(1, 12).Select(m => new MonthTotal(m, noDistance ? 0 : m * 10_000, noDistance ? 0 : m * 3600, false)).ToList();
        return new YearData(
            2025,
            years ?? [2026, 2025, 2024],
            new Totals(150, 1_234_500, 120 * 3600 + 5 * 60),
            goal,
            weeks,
            months,
            [new RunDay(new DateOnly(2025, 3, 1), "TEMPO"), new RunDay(new DateOnly(2025, 10, 12), null)],
            records ?? [new RecordRow(3, 1480, new DateOnly(2025, 5, 1), 11), new RecordRow(7, 21100, new DateOnly(2025, 10, 12), 12)],
            races ?? [new RunRow(12, new DateOnly(2025, 10, 12), "running", "Marathon Eindhoven", 21100, 7200, IsRace: true)],
            runs ??
            [
                new RunRow(12, new DateOnly(2025, 10, 12), "running", "Marathon Eindhoven", 21100, 7200, IsRace: true),
                new RunRow(11, new DateOnly(2025, 5, 1), "running", "Evening run", 5000, 1500)
            ]);
    }

    FakeQueries queries = null!;

    IRenderedComponent<YearOverview> Render(YearData? data, int? year = 2025)
    {
        queries = new FakeQueries(data);
        Services.AddSingleton<IYearQueries>(queries);
        Services.AddSingleton(new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero))));
        return Render<YearOverview>(p => p.Add(c => c.Year, year));
    }

    string Uri => Services.GetRequiredService<NavigationManager>().Uri;

    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Without_a_year_the_page_goes_to_this_year()
    {
        Render(Data(), year: null);

        Assert.Equal("http://localhost/years/2026", Uri);
        Assert.Empty(queries.Requested);
    }

    [Fact]
    public void A_year_that_cant_be_shown_goes_to_this_year()
    {
        Render(null, year: 2019);

        Assert.Equal([2019], queries.Requested);
        Assert.Equal("http://localhost/years/2026", Uri);
    }

    [Fact]
    public void The_year_comes_from_the_address_and_heads_the_page()
    {
        var page = Render(Data());

        Assert.Equal([2025], queries.Requested);
        Assert.Equal("Year 2025", page.Find("h1").TextContent);
    }

    [Fact]
    public void Year_links_switch_years_and_mark_the_shown_one()
    {
        var page = Render(Data());

        var links = page.FindAll(".page-head .years a");
        Assert.Equal(["years/2026", "years/2025", "years/2024"], links.Select(a => a.GetAttribute("href")));
        Assert.Equal(["2025"], links.Where(a => a.GetAttribute("aria-current") == "page").Select(a => a.TextContent));
    }

    [Fact]
    public void A_single_year_needs_no_switch()
    {
        Assert.Empty(Render(Data(years: [2025])).FindAll(".years"));
    }

    [Fact]
    public void The_total_shows_km_runs_and_time()
    {
        Assert.Equal("2025 1234.5 km 150 runs · 120 h 5 min", Text(Render(Data()).Find(".total")));
    }

    [Fact]
    public void Without_a_goal_a_form_sets_one()
    {
        var page = Render(Data());

        var form = page.Find(".totals .new-goal form");
        Assert.Equal(("post", "years/2025/goal"), (form.GetAttribute("method"), form.GetAttribute("action")));
        var input = form.QuerySelector("input[name=km]")!;
        Assert.Equal(("number", "1", "20000"), (input.GetAttribute("type"), input.GetAttribute("min"), input.GetAttribute("max")));
        Assert.True(input.HasAttribute("required"));
        Assert.Empty(page.FindAll(".goal"));
    }

    [Fact]
    public void A_goal_shows_its_progress_with_forms_to_change_and_remove_it()
    {
        var page = Render(Data(goal: 1_200_000));

        Assert.Equal("Reached", page.Find(".goal .status").TextContent);
        var forms = page.FindAll(".goal details.goal-edit form");
        Assert.Equal(["years/2025/goal", "years/2025/goal"], forms.Select(f => f.GetAttribute("action")));
        Assert.Equal("1200", forms[0].QuerySelector("input[name=km]")!.GetAttribute("value"));
        Assert.Equal("true", forms[1].QuerySelector("input[name=remove]")!.GetAttribute("value"));
        Assert.Empty(page.FindAll(".new-goal"));
    }

    [Fact]
    public void Days_with_a_run_cover_the_whole_year()
    {
        var page = Render(Data());

        var activity = page.Find(".activity");
        Assert.Equal(53 * 7, activity.QuerySelectorAll(".cell").Length);
        Assert.Equal(["2025-03-01 · Tempo", "2025-10-12"], activity.QuerySelectorAll(".cell.done").Select(c => c.GetAttribute("title")));
        Assert.Equal("2 days with a run in 2025", activity.QuerySelector(".calendar")!.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_grid_has_full_screen_buttons_in_its_corner_and_a_hint_to_turn_the_phone()
    {
        var page = Render(Data());

        var head = page.Find("#activity .card-head");
        Assert.Equal(["Days with a run"], head.QuerySelectorAll("h2").Select(h => h.TextContent));
        var open = head.QuerySelector("button.fullscreen-open")!;
        Assert.Equal(("Full screen", ""), (open.GetAttribute("aria-label"), open.TextContent.Trim()));
        Assert.NotNull(open.QuerySelector("svg"));
        Assert.Equal("Close full screen", head.QuerySelector("button.fullscreen-close")!.GetAttribute("aria-label"));
        Assert.Equal("Turn your phone for a wider view.", page.Find("#activity .rotate-hint").TextContent);
        Assert.EndsWith("calendar.js", page.Find("script[type=module]").GetAttribute("src"));
    }

    [Fact]
    public void Year_links_load_the_whole_page_so_the_script_runs()
    {
        var page = Render(Data());

        Assert.All(page.FindAll(".page-head .years a"), a => Assert.Equal("false", a.GetAttribute("data-enhance-nav")));
    }

    [Fact]
    public void Weeks_are_labelled_by_month_and_named_by_their_dates()
    {
        var page = Render(Data());

        var slots = page.Find(".weeks .plot.distance").QuerySelectorAll(".slot");
        Assert.Equal(53, slots.Length);
        Assert.Equal(["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
            slots.SelectMany(s => s.QuerySelectorAll(".x")).Select(x => x.TextContent));
        Assert.Equal("2024-12-30 – 2025-01-05: 0.0 km", slots[0].GetAttribute("aria-label"));
        Assert.Empty(page.FindAll(".thin-out"));
    }

    [Fact]
    public void Months_have_a_bar_each()
    {
        var page = Render(Data());

        var slots = page.Find(".months .plot.time").QuerySelectorAll(".slot");
        Assert.Equal(["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
            slots.Select(s => s.QuerySelector(".x")!.TextContent));
        Assert.Equal("December: 12 h 0 min", slots[^1].GetAttribute("aria-label"));
        Assert.Equal("Month", page.Find(".months .as-table th").TextContent);
    }

    [Fact]
    public void The_two_charts_switch_measure_on_their_own()
    {
        var page = Render(Data());

        Assert.Equal(["weekly-distance", "weekly-time", "monthly-distance", "monthly-time"], page.FindAll("input.measure").Select(i => i.Id));
        Assert.Equal(["weekly-measure", "weekly-measure", "monthly-measure", "monthly-measure"],
            page.FindAll("input.measure").Select(i => i.GetAttribute("name")));
    }

    [Fact]
    public void A_year_without_distance_says_so_in_both_charts()
    {
        var page = Render(Data(noDistance: true));

        Assert.Equal(["No runs in 2025.", "No runs in 2025."], page.FindAll(".period-chart .empty").Select(p => p.TextContent));
    }

    [Fact]
    public void Records_are_the_years_and_open_their_run()
    {
        var page = Render(Data());

        Assert.Equal("Personal records 2025", page.Find(".records h2").TextContent);
        Assert.Equal(["5 km 24:40 2025-05-01", "Longest run 21.1 km 2025-10-12"], page.FindAll(".record-list li").Select(Text));
        Assert.Equal(["runs/11", "runs/12"], page.FindAll(".record-list a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void Races_and_runs_list_the_years_runs()
    {
        var page = Render(Data());

        Assert.Equal(["runs/12"], page.FindAll(".races a.row-link").Select(a => a.GetAttribute("href")));
        Assert.Equal(["runs/12", "runs/11"], page.FindAll(".all-runs a.row-link").Select(a => a.GetAttribute("href")));
        Assert.Single(page.FindAll(".all-runs tr.race"));
        Assert.Equal("runs?from=2025-01-01&to=2025-12-31", page.Find(".all-runs .section-head a").GetAttribute("href"));
    }

    [Fact]
    public void Empty_sections_say_so()
    {
        var page = Render(Data(records: [], races: [], runs: []));

        Assert.Equal("No records in 2025.", page.Find(".records p:not(.note)").TextContent);
        Assert.Equal("No races in 2025.", page.Find(".races p").TextContent);
        Assert.Equal("No runs in 2025.", page.Find(".all-runs p").TextContent);
        Assert.Empty(page.FindAll(".all-runs .section-head a"));
    }
}
