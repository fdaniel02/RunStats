using System.Globalization;

namespace RunStats.Features.Years;

public sealed record DistanceCount(int Km, int Runs);

public static class DistanceCounts
{
    // Every km from 1 to the longest, so distances without runs still take up room on the axis.
    public static List<DistanceCount> Of(IEnumerable<double> distancesInMeters)
    {
        var runsPerKm = distancesInMeters.Where(m => m > 0).Select(RoundedKm).ToLookup(km => km);
        return runsPerKm.Count == 0
            ? []
            : [.. Enumerable.Range(1, runsPerKm.Max(g => g.Key)).Select(km => new DistanceCount(km, runsPerKm[km].Count()))];
    }

    static int RoundedKm(double meters) => Math.Max(1, (int)Math.Round(meters / 1000, MidpointRounding.AwayFromZero));
}

// Elapsed rather than moving time: the end is when the run really ended, pauses included.
public sealed record RunTime(DateTime StartLocal, double ElapsedSeconds)
{
    const double MinutesPerDay = 24 * 60;

    public double StartMinute => StartLocal.TimeOfDay.TotalMinutes;
    // Cut at midnight, because the charts show a single day.
    public double EndMinute => Math.Min(StartMinute + ElapsedSeconds / 60, MinutesPerDay);
    public int DaysFromMonday => ((int)StartLocal.DayOfWeek + 6) % 7;
}

// The whole hours of the day that hold all the given runs, divided into half-hour slots.
public sealed record DayWindow(int FromHour, int ToHour)
{
    const int SlotMinutes = 30;

    public int Slots => (ToHour - FromHour) * 60 / SlotMinutes;

    public static DayWindow? Of(IReadOnlyCollection<RunTime> runs)
    {
        if (runs.Count == 0)
            return null;
        var from = (int)Math.Floor(runs.Min(r => r.StartMinute) / 60);
        var to = (int)Math.Ceiling(runs.Max(r => r.EndMinute) / 60);
        return new DayWindow(from, Math.Max(to, from + 1));
    }

    // A run counts in every slot it overlaps; one ending exactly on a slot's start doesn't count in that slot.
    public int[] RunsInProgress(IEnumerable<RunTime> runs)
    {
        var counts = new int[Slots];
        foreach (var run in runs)
        {
            var first = (int)Math.Floor((run.StartMinute - FromHour * 60) / SlotMinutes);
            var last = Math.Max(first, (int)Math.Ceiling((run.EndMinute - FromHour * 60) / SlotMinutes) - 1);
            for (var slot = first; slot <= last && slot < Slots; slot++)
                counts[slot]++;
        }
        return counts;
    }

    public string SlotName(int slot) => $"{TimeName(FromHour * 60 + slot * SlotMinutes)}–{TimeName(FromHour * 60 + (slot + 1) * SlotMinutes)}";

    public double Fraction(double minute) => (minute - FromHour * 60) / ((ToHour - FromHour) * 60);

    public IEnumerable<int> TickHours()
    {
        var step = ToHour - FromHour <= 6 ? 1 : 3;
        return Enumerable.Range(FromHour, ToHour - FromHour + 1).Where(hour => hour % step == 0);
    }

    public static string HourName(int hour) => TimeName(hour * 60);

    static string TimeName(int minutes) =>string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");
}
