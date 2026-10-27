namespace RunStats.Shared;

// Round axis ticks: steps of 1, 2 or 5 times a power of ten, at most MaxIntervals of them up to the largest value.
public static class ChartScale
{
    public const int MaxIntervals = 4;

    static readonly int[] Multipliers = [1, 2, 5, 10];

    public static (double Max, double Step) Nice(double largest)
    {
        if (largest <= 0)
            return (1, 1);
        var rough = largest / MaxIntervals;
        var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = Multipliers.Select(m => m * power).First(s => s >= rough);
        return (Math.Ceiling(largest / step) * step, step);
    }

    public static IEnumerable<double> Ticks(double max, double step)
    {
        for (var tick = 0.0; tick <= max + step / 2; tick += step)
            yield return Math.Round(tick, 6);
    }

    // Round ticks around values that don't start at zero (a pace, a heart rate); steps can be given, e.g. for minutes.
    public static (double Min, double Max, double Step) Range(double lowest, double highest, IReadOnlyList<double>? steps = null)
    {
        if (highest - lowest < 1e-9)
        {
            lowest -= 1;
            highest += 1;
        }
        var rough = (highest - lowest) / MaxIntervals;
        double step;
        if (steps is not null && steps.FirstOrDefault(s => s >= rough) is var fitting && fitting > 0)
            step = fitting;
        else
        {
            var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            step = Multipliers.Select(m => m * power).First(s => s >= rough);
        }
        return (Math.Floor(lowest / step) * step, Math.Ceiling(highest / step) * step, step);
    }
}
