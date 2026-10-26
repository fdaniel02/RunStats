namespace RunStats.Data;

public enum SyncStatus
{
    Running,
    Succeeded,
    Failed
}

public enum SyncTrigger
{
    Schedule,
    Manual
}

public class SyncRun
{
    public int Id { get; set; }
    public SyncTrigger Trigger { get; set; }
    public SyncStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int? RunsTotal { get; set; }
    public int RunsProcessed { get; set; }
    public int RunsAdded { get; set; }
    public int RunsUpdated { get; set; }
    public string? Error { get; set; }
}
