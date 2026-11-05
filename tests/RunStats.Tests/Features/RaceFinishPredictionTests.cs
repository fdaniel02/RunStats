using RunStats.Features.Races;

namespace RunStats.Tests.Features;

public class RaceFinishPredictionTests
{
    static readonly Dictionary<double, double> Garmin = new()
    {
        [5000] = 1310,
        [10000] = 2806,
        [21097.5] = 6300,
        [42195] = 14000
    };

    [Theory]
    [InlineData(5000, 1310)]
    [InlineData(10000, 2806)]
    [InlineData(21100, 6300)]
    [InlineData(21000, 6300)]
    [InlineData(42200, 14000)]
    public void Garmins_own_distances_show_its_prediction(double meters, double seconds) =>
        Assert.Equal(new PredictedFinish(seconds, false), RaceFinishPrediction.For(meters, "Normal", Garmin));

    [Fact]
    public void Between_two_distances_it_follows_the_curve_through_them()
    {
        var prediction = RaceFinishPrediction.For(7000, "Normal", Garmin)!;

        var exponent = Math.Log(2806.0 / 1310) / Math.Log(2);
        Assert.Equal(1310 * Math.Pow(1.4, exponent), prediction.Seconds, 3);
        Assert.True(prediction.IsEstimate);
        Assert.InRange(prediction.Seconds, 1310, 2806);
    }

    [Fact]
    public void Under_the_shortest_distance_it_uses_Riegels_formula()
    {
        var prediction = RaceFinishPrediction.For(3000, "Normal", Garmin)!;

        Assert.Equal(1310 * Math.Pow(0.6, 1.06), prediction.Seconds, 3);
        Assert.True(prediction.IsEstimate);
    }

    [Fact]
    public void Over_the_longest_distance_it_uses_Riegels_formula()
    {
        var prediction = RaceFinishPrediction.For(50000, "Normal", Garmin)!;

        Assert.Equal(14000 * Math.Pow(50000 / 42195.0, 1.06), prediction.Seconds, 3);
        Assert.True(prediction.IsEstimate);
    }

    [Theory]
    [InlineData("Trail")]
    [InlineData("Obstacle")]
    [InlineData("trail")]
    public void Off_road_races_get_none(string type) => Assert.Null(RaceFinishPrediction.For(10000, type, Garmin));

    [Fact]
    public void A_race_without_a_type_counts_as_a_road_race() => Assert.NotNull(RaceFinishPrediction.For(10000, null, Garmin));

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void A_race_without_a_distance_gets_none(double? meters) => Assert.Null(RaceFinishPrediction.For(meters, "Normal", Garmin));

    [Fact]
    public void Without_predictions_there_is_none() => Assert.Null(RaceFinishPrediction.For(10000, "Normal", new Dictionary<double, double>()));
}
