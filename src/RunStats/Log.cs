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
}
