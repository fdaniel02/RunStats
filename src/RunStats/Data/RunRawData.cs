namespace RunStats.Data;

// Garmin's responses as they came, so the other tables can be rebuilt without downloading again.
public class RunRawData
{
    public long RunId { get; set; }
    // The activity list entry has fields the detail lacks (VO2max, fastest splits, time in zones).
    public string ListJson { get; set; } = "";
    public string DetailJson { get; set; } = "";
    public string? SplitsJson { get; set; }
    public string? HeartRateZonesJson { get; set; }
    public string? WeatherJson { get; set; }
    // Missing for activities imported without one.
    public byte[]? FitFile { get; set; }
    public DateTime DownloadedAtUtc { get; set; }
}
