using Bunit;
using RunStats.Features.Races;

namespace RunStats.Tests.Features;

public class UpcomingRacesCardTests : BunitContext
{
    static readonly DateOnly Today = new(2026, 11, 5);
    static readonly Guid PageId = Guid.Parse("11111111-2222-4333-8444-555555555555");

    static UpcomingRaceRow Race(
        string name = "Zandvoort", DateOnly? date = null, TimeOnly? start = null, double? meters = 16100, bool registered = true, string? city = "Zandvoort",
        string? link = null, double? target = null, PredictedFinish? prediction = null) =>
        new(PageId, name, date ?? new DateOnly(2027, 3, 21), start, meters, registered, city, link, target, prediction);

    IRenderedComponent<UpcomingRacesCard> Card(params UpcomingRaceRow[] races) => Render<UpcomingRacesCard>(p => p
        .Add(c => c.Races, races)
        .Add(c => c.Today, Today));

    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Without_races_it_says_so() => Assert.Equal("No upcoming races.", Card().Find("p.muted").TextContent);

    [Fact]
    public void The_first_registered_race_is_the_next_one_even_with_an_unregistered_race_before_it()
    {
        var card = Card(Race("Earlier", new DateOnly(2027, 1, 1), registered: false), Race("Registered"), Race("Later", new DateOnly(2027, 6, 1)));

        Assert.Equal("Registered", card.Find(".next-race h3").TextContent);
        var rows = card.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Earlier", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Later", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_registered_race_nothing_is_highlighted()
    {
        var card = Card(Race(registered: false));

        Assert.Empty(card.FindAll(".next-race"));
        var row = card.Find("tbody tr");
        Assert.Contains("not-registered", row.ClassList);
        Assert.Contains("not registered", row.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "tomorrow")]
    [InlineData(136, "in 136 days")]
    public void The_countdown_is_in_days(int days, string expected) =>
        Assert.Equal(expected, Card(Race(date: Today.AddDays(days))).Find(".countdown").TextContent);

    [Fact]
    public void The_next_race_shows_its_date_time_place_and_distance() =>
        Assert.Equal("2027-03-21 13:30 · Zandvoort · 16.1 km", Card(Race(start: new TimeOnly(13, 30))).Find(".next-head p.muted").TextContent);

    [Fact]
    public void The_next_race_shows_only_what_it_has()
    {
        var card = Card(Race(city: null, meters: null));

        Assert.Equal("2027-03-21", card.Find(".next-head p.muted").TextContent);
        Assert.Empty(card.FindAll(".times dt"));
    }

    [Fact]
    public void The_next_race_shows_target_and_prediction_with_their_paces()
    {
        var card = Card(Race(target: 4800, prediction: new PredictedFinish(4833, true)));

        var times = card.FindAll(".times dd");
        Assert.StartsWith("1:20:00", Text(times[0]), StringComparison.Ordinal);
        Assert.Equal(["4:58 /km"], times[0].QuerySelectorAll(".detail").Select(Text));
        Assert.StartsWith("~about 1:20:33", Text(times[1]), StringComparison.Ordinal);
        Assert.Equal(["5:00 /km"], times[1].QuerySelectorAll(".detail").Select(Text));
    }

    [Fact]
    public void A_prediction_of_Garmin_itself_isnt_marked_as_an_estimate()
    {
        var card = Card(Race(), Race("Half", meters: 21100, prediction: new PredictedFinish(6300, false)));

        var time = card.FindAll("tbody td.num")[1].TextContent;
        Assert.Contains("1:45:00", time, StringComparison.Ordinal);
        Assert.DoesNotContain("~", time, StringComparison.Ordinal);
    }

    [Fact]
    public void In_the_table_target_and_prediction_share_a_column_one_below_the_other()
    {
        var card = Card(Race(), Race("Half", meters: 21100, target: 6000, prediction: new PredictedFinish(6300, false)));

        var cell = card.FindAll("tbody td.num")[1];
        var times = cell.QuerySelectorAll(".time");
        Assert.Equal(2, times.Length);
        Assert.Equal(["Target"], times[0].QuerySelectorAll(".detail").Select(Text));
        Assert.Contains("1:40:00", times[0].TextContent, StringComparison.Ordinal);
        Assert.Equal(["Predicted"], times[1].QuerySelectorAll(".detail").Select(Text));
        Assert.Contains("1:45:00", times[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void In_the_table_a_target_without_a_prediction_stands_alone()
    {
        var card = Card(Race(), Race("Trail", target: 6000));

        var time = Assert.Single(card.FindAll("tbody td.num")[1].QuerySelectorAll(".time"));
        Assert.Equal(["Target"], time.QuerySelectorAll(".detail").Select(Text));
    }

    [Fact]
    public void Missing_values_in_the_table_show_a_dash()
    {
        var row = Card(Race(), Race("Mudrun", meters: null)).Find("tbody tr");

        Assert.Equal(["–", "–"], row.QuerySelectorAll("td.num").Select(Text));
        Assert.Empty(row.QuerySelectorAll(".calculator-link"));
    }

    [Fact]
    public void A_race_with_a_link_opens_it_in_a_new_tab()
    {
        var link = Card(Race(link: "https://example.com/race")).Find(".next-race h3 a");

        Assert.Equal("https://example.com/race", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Theory]
    [InlineData(4800.0, 5000.4, "pace?meters=16100&seconds=4800")]
    [InlineData(null, 5000.4, "pace?meters=16100&seconds=5000")]
    [InlineData(null, null, "pace?meters=16100")]
    public void The_calculator_link_carries_the_target_or_otherwise_the_prediction(double? target, double? predicted, string expected)
    {
        var card = Card(Race(target: target, prediction: predicted is { } p ? new PredictedFinish(p, true) : null));

        var link = card.Find(".calculator-link");
        Assert.Equal(expected, link.GetAttribute("href"));
        Assert.Equal("false", link.GetAttribute("data-enhance-nav"));
    }

    [Fact]
    public void Every_race_links_its_own_page_in_Notion()
    {
        var card = Card(Race(), Race("Later", meters: null));

        var links = card.FindAll(".notion-link");
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal("https://app.notion.com/p/11111111222243338444555555555555", link.GetAttribute("href")));
        Assert.Equal("_blank", links[0].GetAttribute("target"));
        Assert.Equal("Later in Notion", links[1].GetAttribute("aria-label"));
    }

    static UpcomingRaceRow[] Races(int count) => [.. Enumerable.Range(1, count).Select(i => Race($"Race {i}"))];

    [Fact]
    public void Two_races_after_the_next_one_show_and_the_rest_wait_behind_a_button()
    {
        var card = Card(Races(6));

        var rows = card.FindAll("tbody tr");
        Assert.Equal(5, rows.Count);
        Assert.Equal([false, false, true, true, true], rows.Select(r => r.ClassList.Contains("extra")));
        Assert.Equal("Show 3 more", card.Find(".show-more").TextContent);
        Assert.Equal("upcoming-more", card.Find(".more-toggle").Id);
        Assert.Equal("upcoming-more", card.Find(".more-button").GetAttribute("for"));
    }

    [Fact]
    public void Without_more_than_two_other_races_there_is_no_button()
    {
        var card = Card(Races(3));

        Assert.Empty(card.FindAll(".more-button"));
        Assert.Empty(card.FindAll(".more-toggle"));
        Assert.Empty(card.FindAll(".extra"));
    }

    [Fact]
    public void An_unregistered_race_behind_the_button_keeps_its_look()
    {
        var card = Card([.. Races(3), Race("Maybe", registered: false)]);

        var row = card.FindAll("tbody tr")[2];
        Assert.Contains("extra", row.ClassList);
        Assert.Contains("not-registered", row.ClassList);
    }
}
