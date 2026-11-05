namespace RunStats.Data;

public class UpcomingRace
{
    public Guid NotionPageId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public double? DistanceMeters { get; set; }
    public string? Type { get; set; }
    public bool IsRegistered { get; set; }
    public string? City { get; set; }
    public string? Link { get; set; }
    public double? TargetSeconds { get; set; }
}
