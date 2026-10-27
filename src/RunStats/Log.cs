using RunStats.Data;

namespace RunStats;

// Event ids are what log queries and alerts in Grafana filter on; keep them stable when messages change.
public static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "{Operation} attempt {Attempt}/{MaxAttempts} failed, retrying: {ErrorType}: {ErrorMessage}")]
    public static partial void StartupAttemptFailed(
        this ILogger logger, string operation, int attempt, int maxAttempts, string errorType, string errorMessage);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Operation} succeeded after {Attempts} attempts")]
    public static partial void StartupSucceededAfterRetries(this ILogger logger, string operation, int attempts);

    [LoggerMessage(EventId = 3, Level = LogLevel.Critical, Message = "Application stopped because of an unhandled error")]
    public static partial void ApplicationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Sync {SyncId} started ({Trigger})")]
    public static partial void SyncStarted(this ILogger logger, int syncId, SyncTrigger trigger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information,
        Message = "Sync {SyncId}: {ToAdd} runs to add, {ToUpdate} to update, {ToDelete} to delete, {Unchanged} unchanged")]
    public static partial void SyncPlanned(this ILogger logger, int syncId, int toAdd, int toUpdate, int toDelete, int unchanged);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information,
        Message = "Sync {SyncId} succeeded: {Added} runs added, {Updated} updated, {Deleted} deleted in {Seconds:0.0} s")]
    public static partial void SyncSucceeded(this ILogger logger, int syncId, int added, int updated, int deleted, double seconds);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "Sync {SyncId} stopped: {Reason}")]
    public static partial void SyncStopped(this ILogger logger, int syncId, string reason);

    [LoggerMessage(EventId = 14, Level = LogLevel.Error, Message = "Sync {SyncId} failed")]
    public static partial void SyncFailed(this ILogger logger, int syncId, Exception exception);

    [LoggerMessage(EventId = 15, Level = LogLevel.Error, Message = "Sync {SyncId} crashed and couldn't record its result")]
    public static partial void SyncCrashed(this ILogger logger, int syncId, Exception exception);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning, Message = "Syncs interrupted when the app stopped: {Count}")]
    public static partial void SyncsInterrupted(this ILogger logger, int count);

    [LoggerMessage(EventId = 17, Level = LogLevel.Warning, Message = "Run {RunId} couldn't be processed: {Error}")]
    public static partial void RunProcessingFailed(this ILogger logger, long runId, string error);

    [LoggerMessage(EventId = 18, Level = LogLevel.Information, Message = "Sync {SyncId}: {Processed} runs processed, {Failed} failed")]
    public static partial void SyncProcessed(this ILogger logger, int syncId, int processed, int failed);

    [LoggerMessage(EventId = 19, Level = LogLevel.Information,
        Message = "Sync {SyncId}: personal records {Added} added, {Changed} changed, {Removed} removed")]
    public static partial void PersonalRecordsUpdated(this ILogger logger, int syncId, int added, int changed, int removed);

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message = "The sync status couldn't be read")]
    public static partial void SyncStatusFailed(this ILogger logger, Exception exception);
}
