using RunStats.Features.Runs;

namespace RunStats.Tests.Features;

public class RunChartViewTests
{
    static ChartPoint Point(double km, double? pace = 300, double? heartRate = 150, double? elevation = 5, double? cadence = 170, double? stride = 1.05) =>
        new(km, pace, heartRate, elevation, cadence, stride, 51.6, 5.5);

    static List<ChartPoint> Points(int count, Func<int, ChartPoint> point) => [.. Enumerable.Range(0, count).Select(point)];

    static List<(double X, double Y)> Coordinates(string path) =>
    [
        .. path.Split(['M', 'L', 'Z', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(','))
            .Select(p => (double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
    ];

    [Fact]
    public void Every_measure_with_values_gets_a_chart_in_a_fixed_order()
    {
        var view = RunChartView.For(Points(10, i => Point(i * 0.5, pace: 300 + i, heartRate: 140 + i)))!;

        Assert.Equal(["pace", "hr", "elevation", "cadence", "stride"], view.Series.Select(s => s.Key));
        Assert.Equal(("Pace", "/km"), (view.Series[0].Title, view.Series[0].Unit));
        Assert.Equal(4.5, view.TotalKm);
    }

    [Fact]
    public void A_measure_without_values_gets_no_chart()
    {
        var view = RunChartView.For(Points(10, i => Point(i, elevation: null, stride: null)))!;

        Assert.Equal(["pace", "hr", "cadence"], view.Series.Select(s => s.Key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fewer_than_two_points_give_no_charts(int count)
    {
        Assert.Null(RunChartView.For(Points(count, i => Point(i))));
    }

    [Fact]
    public void Lines_span_the_width_along_the_distance()
    {
        var line = Coordinates(RunChartView.For(Points(5, i => Point(i, heartRate: 140 + i)))!.Series[1].Line);

        Assert.Equal([0, 250, 500, 750, 1000], line.Select(p => p.X));
    }

    [Fact]
    public void A_faster_pace_is_higher_up_and_so_is_a_higher_heart_rate()
    {
        var view = RunChartView.For([Point(0, pace: 360, heartRate: 140), Point(1, pace: 300, heartRate: 170)])!;

        var pace = Coordinates(view.Series[0].Line);
        var heartRate = Coordinates(view.Series[1].Line);
        Assert.True(pace[1].Y < pace[0].Y);
        Assert.True(heartRate[1].Y < heartRate[0].Y);
    }

    [Fact]
    public void Missing_values_break_the_line()
    {
        var view = RunChartView.For(Points(9, i => Point(i, pace: i is 3 or 4 ? null : 300 + i)))!;

        Assert.Equal(2, view.Series[0].Line.Count(c => c == 'M'));
        Assert.Equal(7, Coordinates(view.Series[0].Line).Count);
    }

    [Fact]
    public void Elevation_is_an_area_down_to_the_bottom()
    {
        var view = RunChartView.For(Points(3, i => Point(i, elevation: 4 + i)))!;

        var elevation = view.Series.Single(s => s.Key == "elevation");
        Assert.StartsWith("M0,100 L", elevation.Area);
        Assert.EndsWith("L1000,100 Z", elevation.Area);
        Assert.Null(view.Series[0].Area);
    }

    [Fact]
    public void Pace_ticks_read_as_minutes_from_fast_at_the_top()
    {
        var pace = RunChartView.For(Points(20, i => Point(i, pace: 290 + i * 3)))!.Series[0];

        Assert.Equal(["4:45", "5:00", "5:15", "5:30", "5:45", "6:00"], pace.Ticks.Select(t => t.Label));
        Assert.Equal(0, pace.Ticks[0].Position);
        Assert.Equal(100, pace.Ticks[^1].Position);
    }

    [Fact]
    public void The_scale_covers_every_value_even_a_single_peak()
    {
        var points = Points(100, i => Point(i, heartRate: i == 50 ? 191 : 150 + i % 10));

        var heartRate = RunChartView.For(points)!.Series[1];

        Assert.Equal(["140", "160", "180", "200"], heartRate.Ticks.Select(t => t.Label));
        Assert.All(Coordinates(heartRate.Line), p => Assert.InRange(p.Y, 0, 100));
    }

    [Fact]
    public void Stride_ticks_have_two_decimals()
    {
        var stride = RunChartView.For(Points(10, i => Point(i, stride: 0.95 + i * 0.02)))!.Series.Single(s => s.Key == "stride");

        Assert.All(stride.Ticks, t => Assert.Matches(@"^\d\.\d\d$", t.Label));
    }

    [Theory]
    [InlineData(7.5, new[] { "0", "1", "2", "3", "4", "5", "6", "7" })]
    [InlineData(21.1, new[] { "0", "5", "10", "15", "20" })]
    [InlineData(1.2, new[] { "0", "0.2", "0.4", "0.6", "0.8", "1", "1.2" })]
    public void The_km_axis_has_round_labels_up_to_the_distance(double totalKm, string[] expected)
    {
        var view = RunChartView.For([Point(0), Point(totalKm)])!;

        Assert.Equal(expected, view.KmTicks.Select(t => t.Label));
        Assert.Equal(0, view.KmTicks[0].Position);
    }
}
