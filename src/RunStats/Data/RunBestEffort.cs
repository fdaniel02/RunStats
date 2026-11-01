namespace RunStats.Data;

// A run's fastest stretch over one of the record distances, so records can be found for any period: Garmin only keeps
// the all-time ones.
public class RunBestEffort
{
    public long RunId { get; set; }
    // Garmin's record type (RecordTypes): 3 = 5 km.
    public int TypeId { get; set; }
    public double Seconds { get; set; }
}
