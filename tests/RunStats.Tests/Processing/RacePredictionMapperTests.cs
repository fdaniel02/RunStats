using RunStats.Processing;

namespace RunStats.Tests.Processing;

public class RacePredictionMapperTests
{
    [Fact]
    public void Each_distance_gets_its_record_type()
    {
        var predictions = RacePredictionMapper.FromJson("""
            {"userId":1,"fromCalendarDate":null,"toCalendarDate":null,"calendarDate":"2026-11-05",
             "time5K":1310,"time10K":2806,"timeHalfMarathon":6423,"timeMarathon":14340}
            """);

        Assert.Equal([(3, 1310.0), (4, 2806.0), (5, 6423.0), (6, 14340.0)], predictions.Select(p => (p.TypeId, p.Seconds)));
        Assert.All(predictions, p => Assert.Equal(new DateOnly(2026, 11, 5), p.PredictedOn));
    }

    [Fact]
    public void Distances_without_a_usable_time_are_left_out()
    {
        var predictions = RacePredictionMapper.FromJson("""
            {"calendarDate":"2026-11-05","time5K":1310,"time10K":null,"timeHalfMarathon":0}
            """);

        Assert.Equal(3, Assert.Single(predictions).TypeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"time5K":1310}""")]
    [InlineData("""{"calendarDate":null,"time5K":1310}""")]
    [InlineData("""{"calendarDate":"soon","time5K":1310}""")]
    public void Without_a_response_or_a_date_there_are_no_predictions(string? json) =>
        Assert.Empty(RacePredictionMapper.FromJson(json));

    [Fact]
    public void Something_other_than_an_object_is_an_error()
    {
        var error = Assert.Throws<FormatException>(() => RacePredictionMapper.FromJson("[]"));

        Assert.Equal("Garmin's race predictions aren't an object.", error.Message);
    }
}
