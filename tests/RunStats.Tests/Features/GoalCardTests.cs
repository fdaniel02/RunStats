using Bunit;
using RunStats.Features.Years;

namespace RunStats.Tests.Features;

public class GoalCardTests : BunitContext
{
    static readonly DateOnly Today = new(2026, 11, 1);

    IRenderedComponent<GoalCard> Render(GoalProgress progress, string? href = null) =>
        Render<GoalCard>(p => p.Add(c => c.Progress, progress).Add(c => c.Href, href));

    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Under_way_the_card_shows_the_progress_and_what_each_day_needs()
    {
        // 1500 km expected 1253.4 km by today.
        var card = Render(GoalProgress.For(2026, 1_500_000, 1_200_000, Today)).Find(".goal");

        Assert.Equal("Goal 2026", card.QuerySelector(".goal-label")!.TextContent.Trim());
        Assert.Equal("1200.0 of 1500 km · 80%", Text(card.QuerySelector(".goal-value")!));
        Assert.Equal("5.0 km a day for the remaining 60 days", card.QuerySelector(".daily")!.TextContent);
        Assert.Null(card.QuerySelector(".status"));
        Assert.Null(card.QuerySelector(".weekly"));
        Assert.Equal("width: 80%", card.QuerySelector(".done")!.GetAttribute("style"));
        Assert.Equal("left: 84%", card.QuerySelector(".expected")!.GetAttribute("style"));
        Assert.Equal("80% of the goal done, 84% expected by today", card.QuerySelector(".bar")!.GetAttribute("aria-label"));
    }

    [Fact]
    public void A_reached_goal_says_so_and_fills_the_bar()
    {
        var card = Render(GoalProgress.For(2026, 1_000_000, 1_250_000, Today));

        Assert.Equal("Reached", card.Find(".status.reached").TextContent);
        Assert.Equal("1250.0 of 1000 km · 125%", Text(card.Find(".goal-value")));
        Assert.Equal("width: 100%", card.Find(".done").GetAttribute("style"));
        Assert.Empty(card.FindAll(".daily"));
    }

    [Fact]
    public void A_missed_goal_of_a_past_year_says_by_how_much()
    {
        var card = Render(GoalProgress.For(2025, 1_000_000, 880_000, Today));

        Assert.Equal("Missed by 120.0 km", card.Find(".status").TextContent);
        Assert.Empty(card.FindAll(".expected"));
        Assert.Empty(card.FindAll(".daily"));
    }

    [Fact]
    public void A_future_year_only_says_what_each_day_needs()
    {
        var card = Render(GoalProgress.For(2027, 1_460_000, 0, Today));

        Assert.Empty(card.FindAll(".status"));
        Assert.Empty(card.FindAll(".expected"));
        Assert.Equal("4.0 km a day for the remaining 365 days", card.Find(".daily").TextContent);
    }

    [Fact]
    public void On_the_last_but_one_day_one_day_is_left() =>
        Assert.Equal("100.0 km a day for the remaining day",
            Render(GoalProgress.For(2026, 1_000_000, 900_000, new DateOnly(2026, 12, 30))).Find(".daily").TextContent);

    [Fact]
    public void With_a_link_the_label_opens_the_year_with_a_full_page_load()
    {
        var link = Render(GoalProgress.For(2026, 1_500_000, 1_200_000, Today), href: "years/2026").Find(".goal-label a");

        Assert.Equal(("years/2026", "false", "Goal 2026"), (link.GetAttribute("href"), link.GetAttribute("data-enhance-nav"), link.TextContent));
    }
}
