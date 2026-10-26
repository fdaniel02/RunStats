namespace RunStats.Data;

// One second of a run from the FIT file. The types are kept small: there are thousands of rows per run.
// Every value can be missing: treadmill runs have no position, and sensors drop out for a second now and then.
public class RunSample
{
    public long RunId { get; set; }
    public int OffsetSeconds { get; set; }

    // Garmin's semicircles (degrees = semicircles * 180 / 2^31): exact and half the size of a double.
    public int? LatitudeSemicircles { get; set; }
    public int? LongitudeSemicircles { get; set; }

    public float? DistanceMeters { get; set; }
    public float? SpeedMps { get; set; }
    public float? AltitudeMeters { get; set; }
    public byte? HeartRate { get; set; }
    public float? CadenceSpm { get; set; }
    public short? StepLengthMm { get; set; }
    public float? GroundContactTimeMs { get; set; }
    public float? VerticalOscillationMm { get; set; }
    public float? VerticalRatioPercent { get; set; }
    public short? PowerWatts { get; set; }
    public short? TemperatureCelsius { get; set; }
}
