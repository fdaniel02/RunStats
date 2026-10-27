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
        if (meters < 1 || seconds <= 0)
            return Missing;
        var perKm = TimeSpan.FromSeconds(Math.Round(seconds / (meters / 1000)));
        return $"{(int)perKm.TotalMinutes}:{perKm.Seconds:00} /km";
    }

    // ISO 8601 everywhere: 2026-10-11.
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", Culture);

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

    static string Humanize(string typeKey)
    {
        var words = typeKey.Replace('_', ' ').Trim();
        return words.Length == 0 ? "Run" : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
