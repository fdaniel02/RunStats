using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RunStats.Data;

namespace RunStats.Tests.Data;

public class StartupRetryTests
{
    readonly FakeLogger logger = new();
    int calls;

    StartupRetry Retry(int maxAttempts = 15, TimeSpan delay = default) => new(logger, maxAttempts, delay);

    Func<CancellationToken, Task> FailingTimes(int failures, Exception? error = null) => _ =>
        ++calls <= failures ? throw error ?? new TimeoutException("Server not up yet") : Task.CompletedTask;

    static bool NeverPermanent(Exception _) => false;

    [Fact]
    public async Task Success_on_the_first_attempt_logs_nothing()
    {
        await Retry().RunAsync("Migration", FailingTimes(0), NeverPermanent, TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        Assert.Empty(logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task Each_retried_failure_is_a_warning_and_the_success_after_it_information()
    {
        await Retry().RunAsync("Migration", FailingTimes(2), NeverPermanent, TestContext.Current.CancellationToken);

        Assert.Equal(3, calls);
        var records = logger.Collector.GetSnapshot();
        Assert.Collection(records,
            r => AssertAttemptFailed(r, attempt: 1),
            r => AssertAttemptFailed(r, attempt: 2),
            r =>
            {
                Assert.Equal(LogLevel.Information, r.Level);
                Assert.Equal(2, r.Id.Id);
                Assert.Equal("3", r.GetStructuredStateValue("Attempts"));
                Assert.Equal("Migration succeeded after 3 attempts", r.Message);
            });
    }

    static void AssertAttemptFailed(FakeLogRecord record, int attempt)
    {
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(1, record.Id.Id);
        Assert.Equal("Migration", record.GetStructuredStateValue("Operation"));
        Assert.Equal(attempt.ToString(), record.GetStructuredStateValue("Attempt"));
        Assert.Equal("15", record.GetStructuredStateValue("MaxAttempts"));
        Assert.Equal(nameof(TimeoutException), record.GetStructuredStateValue("ErrorType"));
        Assert.Equal("Server not up yet", record.GetStructuredStateValue("ErrorMessage"));
        Assert.Null(record.Exception);
    }

    [Fact]
    public async Task Permanent_error_is_thrown_at_once_without_logging()
    {
        var error = new InvalidOperationException("Login failed");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry().RunAsync("Migration", FailingTimes(5, error), ex => ex == error, TestContext.Current.CancellationToken));

        Assert.Same(error, thrown);
        Assert.Equal(1, calls);
        Assert.Empty(logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task Last_error_is_thrown_when_every_attempt_fails()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            Retry(maxAttempts: 4).RunAsync("Migration", FailingTimes(10), NeverPermanent, TestContext.Current.CancellationToken));

        Assert.Equal(4, calls);
        Assert.Equal(3, logger.Collector.Count);
        Assert.All(logger.Collector.GetSnapshot(), r => Assert.Equal(LogLevel.Warning, r.Level));
    }

    [Fact]
    public async Task Cancellation_stops_the_wait_between_attempts()
    {
        using var cts = new CancellationTokenSource();
        Func<CancellationToken, Task> failAndCancel = _ =>
        {
            calls++;
            cts.Cancel();
            throw new TimeoutException("Server not up yet");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Retry(delay: TimeSpan.FromMinutes(1)).RunAsync("Migration", failAndCancel, NeverPermanent, cts.Token));

        Assert.Equal(1, calls);
    }
}
