namespace RunStats.Data;

public class PersonalRecord
{
    // Garmin's record type (1 = 1 km, 3 = 5 km, 6 = marathon, ...): one current record per type.
    public int TypeId { get; set; }
    // A time in seconds, or a distance in meters for "longest run".
    public double Value { get; set; }
    // Not a foreign key: the record can belong to a run that has been deleted in Garmin.
    public long? RunId { get; set; }
    public DateTime AchievedAtUtc { get; set; }
    public DateTime AchievedAtLocal { get; set; }
    public string RawJson { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}
