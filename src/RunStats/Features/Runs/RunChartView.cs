using System.Globalization;
using RunStats.Shared;

namespace RunStats.Features.Runs;

// Y positions are in % from the top; the SVG paths are drawn in a 1000 × 100 box that CSS stretches to any size.
public sealed record AxisTick(string Label, double Position);

public sealed record ChartSeries(string Key, string Title, string Unit, string Line, string? Area, IReadOnlyList<AxisTick> Ticks);

public sealed record ChartView(IReadOnlyList<ChartSeries> Series, IReadOnlyList<AxisTick> KmTicks, double TotalKm);

public static class RunChartView
{
    public const double Width = 1000;
    public const double Height = 100;

    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    static readonly double[] PaceSteps = [5, 10, 15, 30, 60, 90, 120, 180, 240, 300, 600];
    static readonly double[] KmSteps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50];

    // The km axis has room for more labels than a chart's height.
    const int KmIntervals = 8;

    public static ChartView? For(IReadOnlyList<ChartPoint> points)
    {
        if (points.Count < 2)
            return null;
        var totalKm = points[^1].Km;
        SeriesSpec[] specs =
        [
            new("pace", "Pace", "/km", p => p.PaceSecondsPerKm, PaceLabel, PaceSteps, FasterOnTop: true),
            new("hr", "Heart rate", "bpm", p => p.HeartRate, Whole),
            new("elevation", "Elevation", "m", p => p.ElevationMeters, Whole, Area: true),
            new("cadence", "Cadence", "spm", p => p.CadenceSpm, Whole),
            new("stride", "Stride", "m", p => p.StrideMeters, v => v.ToString("0.00", Culture))
        ];
        var series = specs.Select(spec => Series(points, totalKm, spec));
        var kmStep = KmSteps.FirstOrDefault(step => step >= totalKm / KmIntervals, KmSteps[^1]);
        var kmTicks = ChartScale.Ticks(totalKm, kmStep).Where(km => km <= totalKm)
            .Select(km => new AxisTick(km.ToString("0.#", Culture), km / totalKm * 100)).ToList();
        return new ChartView([.. series.OfType<ChartSeries>()], kmTicks, totalKm);
    }

    sealed record SeriesSpec(
        string Key, string Title, string Unit, Func<ChartPoint, double?> Value, Func<double, string> Label,
        IReadOnlyList<double>? Steps = null, bool FasterOnTop = false, bool Area = false);

    static ChartSeries? Series(IReadOnlyList<ChartPoint> points, double totalKm, SeriesSpec spec)
    {
        var values = points.Select(spec.Value).ToList();
        var present = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        if (present.Count < 2)
            return null;
        // Every value fits: the points are smoothed already, so what's left at the extremes is real (a sprint, a walk).
        var (min, max, step) = ChartScale.Range(present.Min(), present.Max(), spec.Steps);
        double Y(double v) => (spec.FasterOnTop ? v - min : max - v) / (max - min) * Height;
        var segments = Segments(points, values, totalKm, Y);
        var ticks = new List<AxisTick>();
        for (var tick = min; tick <= max + step / 2; tick += step)
            ticks.Add(new AxisTick(spec.Label(tick), Math.Round(Y(tick) / Height * 100, 2)));
        return new ChartSeries(spec.Key, spec.Title, spec.Unit, Line(segments), spec.Area ? Area(segments) : null, ticks);
    }

    // Runs of consecutive points with a value; a missing value (a stop, a sensor dropout) leaves a gap in the line.
    static List<List<(double X, double Y)>> Segments(IReadOnlyList<ChartPoint> points, List<double?> values, double totalKm, Func<double, double> y)
    {
        var segments = new List<List<(double X, double Y)>>();
        List<(double X, double Y)>? current = null;
        for (var i = 0; i < points.Count; i++)
        {
            if (values[i] is not { } v)
            {
                current = null;
                continue;
            }
            if (current is null)
            {
                current = [];
                segments.Add(current);
            }
            current.Add((points[i].Km / totalKm * Width, y(v)));
        }
        return segments;
    }

    static string Line(List<List<(double X, double Y)>> segments) =>
        string.Join(' ', segments.Select(s => "M" + string.Join(" L", s.Select(Point))));

    static string Area(List<List<(double X, double Y)>> segments) =>
        string.Join(' ', segments.Select(s => $"M{Number(s[0].X)},{Number(Height)} L" + string.Join(" L", s.Select(Point)) + $" L{Number(s[^1].X)},{Number(Height)} Z"));

    static string Point((double X, double Y) p) => $"{Number(p.X)},{Number(p.Y)}";

    static string Number(double value) => Math.Round(value, 1).ToString("0.#", Culture);

    static string Whole(double value) => value.ToString("0", Culture);

    static string PaceLabel(double secondsPerKm)
    {
        var time = TimeSpan.FromSeconds(Math.Round(secondsPerKm));
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
