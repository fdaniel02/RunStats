namespace RunStats.Data;

public class RunLap : RunMetrics
{
    public long RunId { get; set; }
    public int LapIndex { get; set; }
    public DateTime StartUtc { get; set; }
    public string? IntensityType { get; set; }
    // Not Garmin's: calculated from the samples (PaceVariation).
    public double? PaceVariationPercent { get; set; }
}
