using RunStats.Shared;

namespace RunStats.Tests.Shared;

public class RecordTypesTests
{
    [Theory]
    [InlineData(1, "1 km")]
    [InlineData(2, "1 mile")]
    [InlineData(3, "5 km")]
    [InlineData(4, "10 km")]
    [InlineData(5, "Half marathon")]
    [InlineData(6, "Marathon")]
    [InlineData(7, "Longest run")]
    public void Garmins_running_record_types_have_names(int typeId, string name) => Assert.Equal(name, RecordTypes.Name(typeId));

    [Fact]
    public void Other_types_have_no_name() => Assert.Null(RecordTypes.Name(8));

    [Fact]
    public void Times_are_durations_and_the_longest_run_a_distance()
    {
        Assert.Equal("23:49", RecordTypes.Value(3, 1429));
        Assert.Equal("1:51:17", RecordTypes.Value(5, 6677));
        Assert.Equal("21.5 km", RecordTypes.Value(RecordTypes.LongestRun, 21530));
    }
}
