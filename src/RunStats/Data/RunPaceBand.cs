namespace RunStats.Data;

// A run's seconds at one pace. Stride and cadence follow pace, so form is compared within the same band across runs.
// Bands are narrow, so queries can combine them into wider ones or count the fast ones for any pace setting.
public class RunPaceBand
{
    public long RunId { get; set; }
    // The band's fast end: 300 holds the seconds from 5:00 up to 5:05/km.
    public short PaceSecondsPerKm { get; set; }
    public int Seconds { get; set; }
    // The form averages leave out steep sections, where stride changes with the slope.
    public int FlatSeconds { get; set; }

    public float? CadenceSpm { get; set; }
    public float? StepLengthMm { get; set; }
    public float? GroundContactTimeMs { get; set; }
    public float? VerticalOscillationMm { get; set; }
    public float? VerticalRatioPercent { get; set; }
    public float? HeartRate { get; set; }
    public float? PowerWatts { get; set; }
    public float? GroundContactBalanceLeftPercent { get; set; }
}
