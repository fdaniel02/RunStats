using RunStats.Data;
using RunStats.Processing;
using static RunStats.Processing.TrackBuilder;

namespace RunStats.Tests.Processing;

public class TrackBuilderTests
{
    const double SemicirclesPerDegree = 2_147_483_648 / 180.0;

    static RunSample At(int second, double latitude, double longitude) => new()
    {
        OffsetSeconds = second,
        LatitudeSemicircles = (int)Math.Round(latitude * SemicirclesPerDegree),
        LongitudeSemicircles = (int)Math.Round(longitude * SemicirclesPerDegree)
    };

    [Fact]
    public void Encodes_googles_example_polyline()
    {
        // The example from Google's documentation of the format.
        var encoded = Encode([new Point(38.5, -120.2), new Point(40.7, -120.95), new Point(43.252, -126.453)]);

        Assert.Equal("_p~iF~ps|U_ulLnnqC_mqNvxq`@", encoded);
    }

    [Fact]
    public void Points_on_a_straight_line_reduce_to_its_ends()
    {
        var line = Enumerable.Range(0, 100).Select(i => new Point(52.37 + i * 0.0001, 4.89)).ToList();

        Assert.Equal([line[0], line[^1]], Simplify(line, ToleranceMeters));
    }

    [Fact]
    public void Corners_are_kept_and_small_wiggles_are_not()
    {
        // About 11 m north, then 11 m east; the middle point of each leg is 1 m off the line.
        List<Point> points =
        [
            new(52.37, 4.89), new(52.37005, 4.890015), new(52.3701, 4.89),
            new(52.37011, 4.89008), new(52.3701, 4.89016)
        ];

        Assert.Equal([points[0], points[2], points[4]], Simplify(points, ToleranceMeters));
    }

    [Fact]
    public void Two_points_stay_as_they_are()
    {
        List<Point> points = [new(52.37, 4.89), new(52.38, 4.9)];

        Assert.Equal(points, Simplify(points, ToleranceMeters));
    }

    [Fact]
    public void Track_has_the_simplified_line_and_the_bounding_box_of_all_positions()
    {
        RunSample[] samples =
        [
            At(2, 52.3702, 4.8902), At(0, 52.3700, 4.8900), At(1, 52.3701, 4.8901),
            new RunSample { OffsetSeconds = 3 }, At(4, 52.3699, 4.8950)
        ];

        var track = Build(7, samples)!;

        Assert.Equal(7, track.RunId);
        Assert.Equal(Encode(Simplify([new(52.37, 4.89), new(52.3701, 4.8901), new(52.3702, 4.8902), new(52.3699, 4.895)], ToleranceMeters)), track.EncodedPolyline);
        Assert.Equal(3, track.PointCount);
        Assert.Equal((52.3699, 52.3702), (Math.Round(track.MinLatitude, 6), Math.Round(track.MaxLatitude, 6)));
        Assert.Equal((4.89, 4.895), (Math.Round(track.MinLongitude, 6), Math.Round(track.MaxLongitude, 6)));
    }

    [Fact]
    public void Run_without_positions_has_no_track()
    {
        Assert.Null(Build(7, [new RunSample { OffsetSeconds = 0 }, new RunSample { OffsetSeconds = 1 }]));
    }

    [Fact]
    public void A_single_position_is_no_track()
    {
        Assert.Null(Build(7, [At(0, 52.37, 4.89)]));
    }
}
