using RunStats.Shared;

namespace RunStats.Tests.Shared;

public class FormatTests
{
    [Theory]
    [InlineData(10012.5, "10.0")]
    [InlineData(21097.5, "21.1")]
    [InlineData(960, "1.0")]
    [InlineData(0, "0.0")]
    public void Km_has_one_decimal(double meters, string expected) => Assert.Equal(expected, Format.Km(meters));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59.6, "1:00")]
    [InlineData(1503, "25:03")]
    [InlineData(3599, "59:59")]
    [InlineData(3765, "1:02:45")]
    [InlineData(36000, "10:00:00")]
    public void Duration_shows_hours_only_from_an_hour(double seconds, string expected) => Assert.Equal(expected, Format.Duration(seconds));

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(2520, "42 min")]
    [InlineData(3600, "1 h 0 min")]
    [InlineData(42120, "11 h 42 min")]
    public void Hours_for_totals(double seconds, string expected) => Assert.Equal(expected, Format.Hours(seconds));

    [Theory]
    [InlineData(10000, 2950, "4:55 /km")]
    [InlineData(5000, 1800, "6:00 /km")]
    [InlineData(1000, 359.6, "6:00 /km")]
    [InlineData(0, 1800, "–")]
    [InlineData(0.5, 1800, "–")]
    [InlineData(5000, 0, "–")]
    public void Pace_per_km(double meters, double seconds, string expected) => Assert.Equal(expected, Format.Pace(meters, seconds));

    [Fact]
    public void Dates_are_iso_8601() => Assert.Equal("2025-09-07", Format.Date(new DateOnly(2025, 9, 7)));

    [Theory]
    [InlineData("running", "Run")]
    [InlineData("treadmill_running", "Treadmill")]
    [InlineData("trail_running", "Trail run")]
    [InlineData("track_running", "Track")]
    [InlineData("street_running", "Street run")]
    [InlineData("indoor_running", "Indoor run")]
    [InlineData("virtual_run", "Virtual run")]
    [InlineData("obstacle_run", "Obstacle run")]
    [InlineData("ultra_run", "Ultra run")]
    [InlineData("hill_running", "Hill running")]
    [InlineData("", "Run")]
    public void Run_types_get_readable_names(string typeKey, string expected) => Assert.Equal(expected, Format.RunType(typeKey));
}
