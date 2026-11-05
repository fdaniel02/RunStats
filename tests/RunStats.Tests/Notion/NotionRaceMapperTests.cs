using System.Text.Json;
using RunStats.Notion;

namespace RunStats.Tests.Notion;

public class NotionRaceMapperTests
{
    const string Id = "11111111-2222-4333-8444-555555555555";

    static NotionRace? Map(string properties)
    {
        using var document = JsonDocument.Parse($$$"""{"id":"{{{Id}}}","properties":{{{{properties}}}}}""");
        return NotionRaceMapper.FromPage(document.RootElement);
    }

    const string Name = """ "Race":{"type":"title","title":[{"plain_text":"Zandvoort"}]} """;
    const string Day = """ "Datum":{"type":"date","date":{"start":"2027-05-09","end":null,"time_zone":null}} """;

    [Fact]
    public void A_full_row_keeps_every_field()
    {
        var race = Map("""
            "Race":{"type":"title","title":[{"plain_text":"Zand"},{"plain_text":"voort "}]},
            "Datum":{"type":"date","date":{"start":"2027-03-21T13:30:00.000+01:00","end":null,"time_zone":null}},
            "Distance":{"type":"number","number":16.1},
            "Type":{"type":"select","select":{"id":"iXmM","name":"Normal","color":"red"}},
            "Registered":{"type":"checkbox","checkbox":true},
            "City":{"type":"rich_text","rich_text":[{"plain_text":"Zandvoort"}]},
            "Link":{"type":"url","url":"https://www.zandvoortcircuitrun.nl/"},
            "Target Time":{"type":"rich_text","rich_text":[{"plain_text":"1:20:00"}]}
            """);

        Assert.Equal(
            new NotionRace(Guid.Parse(Id), "Zandvoort", new DateOnly(2027, 3, 21), new TimeOnly(13, 30), 16.1, "Normal", true, "Zandvoort",
                "https://www.zandvoortcircuitrun.nl/", "1:20:00"),
            race);
    }

    [Fact]
    public void Empty_fields_are_empty()
    {
        var race = Map($$"""
            {{Name}},{{Day}},
            "Distance":{"type":"number","number":null},
            "Type":{"type":"select","select":null},
            "Registered":{"type":"checkbox","checkbox":false},
            "City":{"type":"rich_text","rich_text":[]},
            "Link":{"type":"url","url":null},
            "Target Time":{"type":"rich_text","rich_text":[]}
            """)!;

        Assert.Equal(new DateOnly(2027, 5, 9), race.Date);
        Assert.Null(race.StartTime);
        Assert.Equal((null, null, false, null, null, null), (race.DistanceKm, race.Type, race.IsRegistered, race.City, race.Link, race.TargetTime));
    }

    [Fact]
    public void Missing_properties_are_empty_too()
    {
        var race = Map($"{Name},{Day}")!;

        Assert.Equal((null, false), (race.DistanceKm, race.IsRegistered));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a link")]
    [InlineData("ftp://example.com/")]
    public void Only_web_addresses_are_links(string link)
    {
        var race = Map($$"""{{Name}},{{Day}},"Link":{"type":"url","url":"{{link}}"}""")!;

        Assert.Null(race.Link);
    }

    [Theory]
    [InlineData(""" "Race":{"type":"title","title":[]},"Datum":{"type":"date","date":{"start":"2027-05-09"}} """)]
    [InlineData(""" "Race":{"type":"title","title":[{"plain_text":"Zandvoort"}]},"Datum":{"type":"date","date":null} """)]
    [InlineData(""" "Race":{"type":"title","title":[{"plain_text":"Zandvoort"}]},"Datum":{"type":"date","date":{"start":"soon"}} """)]
    public void A_row_without_a_name_or_date_is_left_out(string properties) => Assert.Null(Map(properties));
}
