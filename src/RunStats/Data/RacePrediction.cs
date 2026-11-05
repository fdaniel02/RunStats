namespace RunStats.Data;

public class RacePrediction
{
    // The personal record type of the same distance (3 = 5 km, 4 = 10 km, 5 = half marathon, 6 = marathon).
    public int TypeId { get; set; }
    public double Seconds { get; set; }
    public DateOnly PredictedOn { get; set; }
}
