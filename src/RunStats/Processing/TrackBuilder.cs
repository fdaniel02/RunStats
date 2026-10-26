using System.Text;
using RunStats.Data;

namespace RunStats.Processing;

// The route for the heatmap: GPS positions simplified with Douglas-Peucker, encoded as a Google polyline.
public static class TrackBuilder
{
    // Below what a phone screen or a heatmap line can show, and well below GPS accuracy.
    public const double ToleranceMeters = 3;

    const double EarthRadiusMeters = 6_371_000;
    const double DegreesPerSemicircle = 180.0 / 2_147_483_648;

    public readonly record struct Point(double Latitude, double Longitude);

    public static RunTrack? Build(long runId, IEnumerable<RunSample> samples)
    {
        var points = samples
            .Where(s => s.LatitudeSemicircles is not null && s.LongitudeSemicircles is not null)
            .OrderBy(s => s.OffsetSeconds)
            .Select(s => new Point(s.LatitudeSemicircles!.Value * DegreesPerSemicircle, s.LongitudeSemicircles!.Value * DegreesPerSemicircle))
            .ToList();
        if (points.Count < 2)
            return null;
        var simplified = Simplify(points, ToleranceMeters);
        return new RunTrack
        {
            RunId = runId,
            EncodedPolyline = Encode(simplified),
            PointCount = simplified.Count,
            MinLatitude = points.Min(p => p.Latitude),
            MaxLatitude = points.Max(p => p.Latitude),
            MinLongitude = points.Min(p => p.Longitude),
            MaxLongitude = points.Max(p => p.Longitude)
        };
    }

    // Keeps the points that are further than the tolerance from the line between the points kept around them.
    public static List<Point> Simplify(IReadOnlyList<Point> points, double toleranceMeters)
    {
        if (points.Count < 3)
            return [.. points];
        // Distances on a flat projection around the route: exact enough over the few kilometres of a run.
        var metersPerDegreeLatitude = Math.PI / 180 * EarthRadiusMeters;
        var metersPerDegreeLongitude = metersPerDegreeLatitude * Math.Cos(points[0].Latitude * Math.PI / 180);
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        // A stack instead of recursion: a long run has thousands of points.
        var ranges = new Stack<(int First, int Last)>();
        ranges.Push((0, points.Count - 1));
        while (ranges.Count > 0)
        {
            var (first, last) = ranges.Pop();
            var (furthest, distance) = (-1, 0.0);
            for (var i = first + 1; i < last; i++)
            {
                var d = DistanceToSegment(points[i], points[first], points[last], metersPerDegreeLatitude, metersPerDegreeLongitude);
                if (d > distance)
                    (furthest, distance) = (i, d);
            }
            if (furthest < 0 || distance <= toleranceMeters)
                continue;
            keep[furthest] = true;
            ranges.Push((first, furthest));
            ranges.Push((furthest, last));
        }
        return [.. points.Where((_, i) => keep[i])];
    }

    static double DistanceToSegment(Point p, Point a, Point b, double metersPerDegreeLatitude, double metersPerDegreeLongitude)
    {
        var (px, py) = ((p.Longitude - a.Longitude) * metersPerDegreeLongitude, (p.Latitude - a.Latitude) * metersPerDegreeLatitude);
        var (bx, by) = ((b.Longitude - a.Longitude) * metersPerDegreeLongitude, (b.Latitude - a.Latitude) * metersPerDegreeLatitude);
        var lengthSquared = bx * bx + by * by;
        var t = lengthSquared == 0 ? 0 : Math.Clamp((px * bx + py * by) / lengthSquared, 0, 1);
        var (dx, dy) = (px - t * bx, py - t * by);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Google's encoded polyline format with 5 decimals (about 1 m), which Leaflet plugins read.
    public static string Encode(IEnumerable<Point> points)
    {
        var result = new StringBuilder();
        var (previousLatitude, previousLongitude) = (0L, 0L);
        foreach (var point in points)
        {
            var latitude = (long)Math.Round(point.Latitude * 1e5, MidpointRounding.AwayFromZero);
            var longitude = (long)Math.Round(point.Longitude * 1e5, MidpointRounding.AwayFromZero);
            EncodeValue(latitude - previousLatitude, result);
            EncodeValue(longitude - previousLongitude, result);
            (previousLatitude, previousLongitude) = (latitude, longitude);
        }
        return result.ToString();
    }

    static void EncodeValue(long value, StringBuilder result)
    {
        var shifted = value < 0 ? ~(value << 1) : value << 1;
        while (shifted >= 0x20)
        {
            result.Append((char)((0x20 | (shifted & 0x1f)) + 63));
            shifted >>= 5;
        }
        result.Append((char)(shifted + 63));
    }
}
