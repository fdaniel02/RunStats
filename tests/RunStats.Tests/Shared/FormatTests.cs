using RunStats.Shared;

namespace RunStats.Tests.Shared;

public class FormatTests
{
    [Theory]
    [InlineData(10012.5, "10.0")]
    [InlineData(21097.5, "21.1")]
    [InlineData(960, "1.0")]
    [InlineData(0.0, "0.0")]
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
    [InlineData(3.13, "11.3")]
    [InlineData(0.0, "0.0")]
    [InlineData(null, "–")]
    public void Speed_is_in_km_per_hour(double? metersPerSecond, string expected) => Assert.Equal(expected, Format.Speed(metersPerSecond));

    [Theory]
    [InlineData(171.4, "171")]
    [InlineData(512.5, "513")]
    [InlineData(null, "–")]
    public void Whole_rounds_to_a_whole_number(double? value, string expected) => Assert.Equal(expected, Format.Whole(value));

    [Theory]
    [InlineData(109.7, "1.10")]
    [InlineData(84.0, "0.84")]
    [InlineData(null, "–")]
    public void Meters_from_centimeters_with_two_decimals(double? centimeters, string expected) => Assert.Equal(expected, Format.Meters(centimeters));

    [Theory]
    [InlineData("148", "172", "148 / 172")]
    [InlineData("148", "–", "148 / –")]
    [InlineData("–", "172", "– / 172")]
    [InlineData("–", "–", "–")]
    public void Pair_shows_both_sides_unless_both_are_missing(string first, string second, string expected) =>
        Assert.Equal(expected, Format.Pair(first, second));

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

    [Theory]
    [InlineData(1000, 344, "5:44")]
    [InlineData(0, 300, "–")]
    public void Pace_value_has_no_unit(double meters, double seconds, string expected) => Assert.Equal(expected, Format.PaceValue(meters, seconds));

    [Theory]
    [InlineData(3.75, "4:27")]
    [InlineData(2.0, "8:20")]
    [InlineData(0.0, "–")]
    [InlineData(null, "–")]
    public void Max_pace_comes_from_max_speed(double? maxSpeedMps, string expected) => Assert.Equal(expected, Format.MaxPace(maxSpeedMps));

    [Theory]
    [InlineData(4.25, "4.3")]
    [InlineData(0.0, "0.0")]
    [InlineData(null, "–")]
    public void One_decimal(double? value, string expected) => Assert.Equal(expected, Format.OneDecimal(value));

    [Theory]
    [InlineData("WARMUP", "Warm-up")]
    [InlineData("COOLDOWN", "Cool-down")]
    [InlineData("ACTIVE", "Active")]
    [InlineData("INTERVAL", "Interval")]
    [InlineData("RECOVERY", "Recovery")]
    [InlineData("REST", "Rest")]
    [InlineData("SOMETHING_NEW", "Something new")]
    [InlineData(null, "–")]
    [InlineData("", "–")]
    public void Lap_types_have_readable_names(string? intensityType, string expected) => Assert.Equal(expected, Format.LapType(intensityType));

    [Fact]
    public void Dates_are_iso_8601() => Assert.Equal("2025-09-07", Format.Date(new DateOnly(2025, 9, 7)));

    [Fact]
    public void Date_and_time_are_iso_8601_to_the_minute() =>
        Assert.Equal("2025-09-07 08:05", Format.DateAndTime(new DateTime(2025, 9, 7, 8, 5, 59)));

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
