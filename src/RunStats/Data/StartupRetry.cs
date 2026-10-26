namespace RunStats.Data;

public class StartupRetry(ILogger logger, int maxAttempts, TimeSpan delay)
{
    public async Task RunAsync(
        string operation, Func<CancellationToken, Task> action, Func<Exception, bool> isPermanent, CancellationToken cancellationToken = default)
    {
        var attempt = 1;
        while (true)
        {
            try
            {
                await action(cancellationToken);
                if (attempt > 1)
                    logger.StartupSucceededAfterRetries(operation, attempt);
                return;
            }
            // The stack trace is left out: the same error repeats on every attempt, and the final one is logged in full.
            catch (Exception ex) when (attempt < maxAttempts && !isPermanent(ex))
            {
                logger.StartupAttemptFailed(operation, attempt, maxAttempts, ex.GetType().Name, ex.Message);
            }
            await Task.Delay(delay, cancellationToken);
            attempt++;
        }
    }
}
