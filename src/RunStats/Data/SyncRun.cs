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
    // Runs to download, and how many of them are done.
    public int? RunsTotal { get; set; }
    public int RunsDownloaded { get; set; }
    // Runs whose raw data is processed after the downloads (new ones, and all of them when the processing changed).
    public int? RunsToProcess { get; set; }
    public int RunsProcessed { get; set; }
    public int RunsFailedToProcess { get; set; }
    public int RunsAdded { get; set; }
    public int RunsUpdated { get; set; }
    public int RunsDeleted { get; set; }
    public string? Error { get; set; }
}
