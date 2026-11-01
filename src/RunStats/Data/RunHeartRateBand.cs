namespace RunStats.Data;

// A run's steady, flat seconds at one heart rate and the distance covered in them, for the pace at the same heart rate
// across runs. Bands are narrow, so queries can combine them into the band they compare.
public class RunHeartRateBand
{
    public long RunId { get; set; }
    // The band's low end: 150 holds the seconds from 150 up to 155 bpm.
    public short HeartRate { get; set; }
    public int Seconds { get; set; }
    public double DistanceMeters { get; set; }
}
