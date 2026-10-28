using System.Globalization;

namespace RunStats.Shared;

// How the screens show Garmin's units: kilometres, durations, pace and names.
public static class Format
{
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public const string Missing = "–";

    public static string Km(double meters) => (meters / 1000).ToString("0.0", Culture);

    // 25:03 below an hour, 1:02:45 from an hour.
    public static string Duration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Round(seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    // For totals: 11 h 42 min, or 42 min below an hour.
    public static string Hours(double seconds)
    {
        var minutes = (long)Math.Round(seconds / 60);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60} min" : $"{minutes} min";
    }

    public static string Pace(double meters, double seconds)
    {
        var pace = PaceValue(meters, seconds);
        return pace == Missing ? Missing : $"{pace} /km";
    }

    // Minutes per km without the unit, e.g. to sit next to the max pace.
    public static string PaceValue(double meters, double seconds) =>
        meters < 1 || seconds <= 0 ? Missing : MinutesPerKm(seconds / (meters / 1000));

    // The fastest pace, from Garmin's max speed; without the unit.
    public static string MaxPace(double? maxSpeedMps) => maxSpeedMps > 0 ? MinutesPerKm(1000 / maxSpeedMps.Value) : Missing;

    static string MinutesPerKm(double secondsPerKm)
    {
        var perKm = TimeSpan.FromSeconds(Math.Round(secondsPerKm));
        return $"{(int)perKm.TotalMinutes}:{perKm.Seconds:00}";
    }

    // How evenly a run or lap was paced, e.g. ±3.2%.
    public static string Variation(double? percent) => percent is { } p ? "±" + p.ToString("0.0", Culture) + "%" : Missing;

    public static string Speed(double? metersPerSecond) => metersPerSecond is { } mps ? (mps * 3.6).ToString("0.0", Culture) : Missing;

    public static string Whole(double? value) => value is { } v ? v.ToString("0", Culture) : Missing;

    public static string OneDecimal(double? value) => value is { } v ? v.ToString("0.0", Culture) : Missing;

    public static string Meters(double? centimeters) => centimeters is { } cm ? (cm / 100).ToString("0.00", Culture) : Missing;

    // 148 / 172; a single missing side keeps its place as "–".
    public static string Pair(string first, string second) =>
        first == Missing && second == Missing ? Missing : $"{first} / {second}";

    // ISO 8601 everywhere: 2026-10-11.
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", Culture);

    public static string DateAndTime(DateTime local) => local.ToString("yyyy-MM-dd HH:mm", Culture);

    // Garmin's type keys for runs (parent type 1, and running itself).
    public static string RunType(string typeKey) => typeKey switch
    {
        "running" => "Run",
        "treadmill_running" => "Treadmill",
        "trail_running" => "Trail run",
        "track_running" => "Track",
        "street_running" => "Street run",
        "indoor_running" => "Indoor run",
        "virtual_run" => "Virtual run",
        "obstacle_run" => "Obstacle run",
        "ultra_run" => "Ultra run",
        _ => Humanize(typeKey)
    };

    // Garmin's lap intensity types; interval workouts mark their laps with them.
    public static string LapType(string? intensityType) => intensityType switch
    {
        null or "" => Missing,
        "ACTIVE" => "Active",
        "INTERVAL" => "Interval",
        "WARMUP" => "Warm-up",
        "COOLDOWN" => "Cool-down",
        "RECOVERY" => "Recovery",
        "REST" => "Rest",
        _ => Humanize(intensityType.ToLowerInvariant())
    };

    static string Humanize(string typeKey)
    {
        var words = typeKey.Replace('_', ' ').Trim();
        return words.Length == 0 ? "Run" : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
