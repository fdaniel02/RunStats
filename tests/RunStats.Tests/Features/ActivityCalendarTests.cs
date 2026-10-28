using Bunit;
using RunStats.Features.Dashboard;

namespace RunStats.Tests.Features;

public class ActivityCalendarTests : BunitContext
{
    // Tuesday; the grid's last column is the week of Monday 2026-10-26.
    static readonly DateOnly Today = new(2026, 10, 27);

    IRenderedComponent<ActivityCalendar> Render(params DateOnly[] runDays) => RenderDays([.. runDays.Select(d => new RunDay(d, null))]);

    IRenderedComponent<ActivityCalendar> RenderDays(params RunDay[] runDays) =>
        Render<ActivityCalendar>(p => p
            .Add(c => c.Grid, new ActivityGrid(Today, 16))
            .Add(c => c.RunDays, runDays)
            .Add(c => c.Today, Today));

    [Fact]
    public void Each_week_is_a_month_label_and_seven_days()
    {
        var calendar = Render();

        Assert.Equal(16, calendar.FindAll(".month").Count);
        Assert.Equal(16 * 7, calendar.FindAll(".cell").Count);
        // This week ends on Sunday 1 November, so its column is "Nov", as in WorkoutTracker.
        Assert.Equal(["Jul", "Aug", "Sep", "Oct", "Nov"], calendar.FindAll(".month").Select(m => m.TextContent).Where(t => t.Length > 0));
    }

    [Fact]
    public void Days_with_a_run_are_filled_and_named_in_iso()
    {
        var calendar = Render(new DateOnly(2026, 10, 17), new DateOnly(2026, 7, 13));

        Assert.Equal(["2026-07-13", "2026-10-17"], calendar.FindAll(".cell.done").Select(c => c.GetAttribute("title")));
        Assert.Equal("2026-07-13", calendar.Find(".cell").GetAttribute("title"));
    }

    [Fact]
    public void Today_is_outlined_and_the_rest_of_its_week_is_hidden()
    {
        var calendar = Render(Today);

        Assert.Equal("cell done today", calendar.Find(".cell.today").GetAttribute("class"));
        Assert.Equal(["2026-10-28", "2026-10-29", "2026-10-30", "2026-10-31", "2026-11-01"],
            calendar.FindAll(".cell.future").Select(c => c.GetAttribute("title")));
    }

    [Fact]
    public void A_run_day_in_the_future_is_not_filled()
    {
        Assert.Empty(Render(new DateOnly(2026, 10, 30)).FindAll(".cell.done"));
    }

    [Fact]
    public void Screen_readers_get_the_count_of_run_days()
    {
        var calendar = Render(new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 26), new DateOnly(2026, 7, 1));

        Assert.Equal("2 days with a run in the last 16 weeks", calendar.Find(".calendar").GetAttribute("aria-label"));
    }

    [Fact]
    public void Days_are_shaded_by_their_training_effect_and_name_it()
    {
        var calendar = RenderDays(
            new RunDay(new DateOnly(2026, 10, 12), "RECOVERY"),
            new RunDay(new DateOnly(2026, 10, 17), "VO2MAX"),
            new RunDay(new DateOnly(2026, 10, 20), "SPRINT"));

        Assert.Equal(["cell done effect-1", "cell done effect-5", "cell done effect-6"],
            calendar.FindAll(".cell.done").Select(c => c.GetAttribute("class")));
        Assert.Equal(["2026-10-12 · Recovery", "2026-10-17 · VO2 max", "2026-10-20 · Sprint"],
            calendar.FindAll(".cell.done").Select(c => c.GetAttribute("title")));
    }

    [Fact]
    public void A_run_without_a_known_label_stays_plain_blue()
    {
        var calendar = RenderDays(new RunDay(new DateOnly(2026, 10, 12), null), new RunDay(new DateOnly(2026, 10, 13), "SOMETHING_NEW"));

        Assert.Equal(["cell done", "cell done"], calendar.FindAll(".cell.done").Select(c => c.GetAttribute("class")));
        Assert.Equal(["2026-10-12", "2026-10-13 · Something new"], calendar.FindAll(".cell.done").Select(c => c.GetAttribute("title")));
    }

    [Fact]
    public void The_legend_runs_from_recovery_to_anaerobic()
    {
        var legend = Render().Find(".legend");

        Assert.Equal(["Recovery", "Anaerobic"], legend.QuerySelectorAll("span:not(.key)").Select(s => s.TextContent));
        Assert.Equal(Enumerable.Range(1, 6).Select(l => $"key effect-{l}"), legend.QuerySelectorAll(".key").Select(k => k.GetAttribute("class")));
    }
}
