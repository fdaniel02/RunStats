using RunStats.Notion;

namespace RunStats.Tests.Notion;

public class TargetTimeTests
{
    [Theory]
    [InlineData("1:45:30", 6330)]
    [InlineData("01:45:30", 6330)]
    [InlineData(" 45:30 ", 2730)]
    [InlineData("75:00", 4500)]
    [InlineData("45", 2700)]
    public void Times_are_read_like_in_the_pace_calculator(string text, double seconds) => Assert.Equal(seconds, TargetTime.ParseSeconds(text));

    [Theory]
    [InlineData("")]
    [InlineData("DNS")]
    [InlineData("-")]
    [InlineData("5:")]
    [InlineData("5:3")]
    [InlineData("5:60")]
    [InlineData("1:60:00")]
    [InlineData("1:2:3:4")]
    [InlineData("1:45:30:00")]
    [InlineData("0")]
    [InlineData("0:00")]
    [InlineData("99999")]
    [InlineData("٤٥")]
    public void Anything_else_isnt_a_time(string text) => Assert.Null(TargetTime.ParseSeconds(text));
}
