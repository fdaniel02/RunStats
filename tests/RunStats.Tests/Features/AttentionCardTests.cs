using Bunit;
using RunStats.Features.Warnings;

namespace RunStats.Tests.Features;

public class AttentionCardTests : BunitContext
{
    static string Text(AngleSharp.Dom.IElement element) => string.Join(" ", element.TextContent.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Each_warning_shows_its_title_and_numbers()
    {
        var card = Render<AttentionCard>(p => p.Add(c => c.Warnings,
        [
            new Warning(WarningKind.FastJump, "1 h 8 min faster than 5:10 /km in the last 7 days, against 5 min a week in the 4 weeks before."),
            new Warning(WarningKind.HardRunsBackToBack, "Hard runs on 2026-09-24 and 2026-09-27, 68 h apart.")
        ]));

        Assert.Equal("Attention", Text(card.Find("h2")).TrimStart('!', ' '));
        Assert.Equal(
            ["Fast running 1 h 8 min faster than 5:10 /km in the last 7 days, against 5 min a week in the 4 weeks before.",
             "Hard runs back to back Hard runs on 2026-09-24 and 2026-09-27, 68 h apart."],
            card.FindAll("li").Select(Text));
        Assert.Equal("trends", card.Find("a").GetAttribute("href"));
        Assert.Contains("not medical advice", card.Find(".footnote").TextContent);
    }

    [Fact]
    public void Without_warnings_one_line_links_to_the_trends()
    {
        var card = Render<AttentionCard>(p => p.Add(c => c.Warnings, []));

        Assert.Equal("✓ No warnings in the last days and weeks. See trends", Text(card.Find(".attention.quiet")));
        Assert.Empty(card.FindAll("li"));
        Assert.Equal("trends", card.Find("a").GetAttribute("href"));
    }

    [Theory]
    [InlineData(WarningKind.FastJump, "Fast running")]
    [InlineData(WarningKind.VeryFastJump, "Very fast running")]
    [InlineData(WarningKind.VolumeJump, "More running")]
    [InlineData(WarningKind.LongRunJump, "Longer run")]
    [InlineData(WarningKind.HardRunsBackToBack, "Hard runs back to back")]
    [InlineData(WarningKind.FormChange, "Form change")]
    [InlineData(WarningKind.BalanceShift, "Balance shift")]
    public void Every_kind_has_a_title(WarningKind kind, string title) => Assert.Equal(title, WarningTitles.Title(kind));
}
