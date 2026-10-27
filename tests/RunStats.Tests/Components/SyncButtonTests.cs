using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using RunStats.Components.Layout;
using RunStats.Data;
using RunStats.Sync;

namespace RunStats.Tests.Components;

public class SyncButtonTests : BunitContext
{
    sealed class FakeSync : ISyncStatusQueries
    {
        public SyncState State { get; set; } = new(null, null, null);
        public SyncState? AfterStart { get; set; }
        public bool IsRunning { get; set; }
        public int Loads { get; private set; }
        public int Starts { get; private set; }
        public Exception? LoadError { get; set; }

        public Task<SyncState> LoadAsync(CancellationToken cancellationToken)
        {
            Loads++;
            return LoadError is { } e ? Task.FromException<SyncState>(e) : Task.FromResult(State);
        }

        public Task<SyncState> StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            State = AfterStart ?? State;
            IsRunning = State.Running is not null;
            return Task.FromResult(State);
        }
    }

    // Monday 26 October 2026, 19:01 UTC: 20:01 in Amsterdam (winter time).
    static readonly DateTime Evening = new(2026, 10, 26, 19, 1, 0, DateTimeKind.Utc);

    readonly FakeSync sync = new();
    readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));

    IRenderedComponent<SyncButton> Render(bool interactive = true)
    {
        Services.AddSingleton<ISyncStatusQueries>(sync);
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton(new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), time));
        SetRendererInfo(new RendererInfo("Server", interactive));
        return Render<SyncButton>();
    }

    void Tick() => time.Advance(SyncButton.PollInterval);

    static SyncState Running(int? total = null, int downloaded = 0, int? toProcess = null, int processed = 0) =>
        new(new SyncProgress(total, downloaded, toProcess, processed), Evening, null);

    [Fact]
    public void Shows_the_last_successful_sync_in_local_time()
    {
        sync.State = new SyncState(null, Evening, null);

        var button = Render();

        Assert.Equal("Synced 2026-10-26 20:01", button.Find(".sync-status").TextContent);
        Assert.Equal("Sync now", button.Find("button").TextContent.Trim());
        Assert.False(button.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Shows_the_error_of_a_failed_sync()
    {
        sync.State = new SyncState(null, Evening.AddDays(-1), new SyncFailure(Evening, "Garmin is down"));

        var status = Render().Find(".sync-status");

        Assert.Equal("Sync failed 2026-10-26 20:01: Garmin is down", status.TextContent);
        Assert.Contains("failed", status.ClassList);
        Assert.Equal("Garmin is down", status.GetAttribute("title"));
    }

    [Fact]
    public void Before_the_first_sync_says_so()
    {
        Assert.Equal("Not synced yet", Render().Find(".sync-status").TextContent);
    }

    [Fact]
    public void The_button_waits_for_the_connection()
    {
        Assert.True(Render(interactive: false).Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void A_running_sync_shows_its_progress_instead_of_the_status()
    {
        sync.State = Running(120, 34);

        var button = Render();

        Assert.Equal("Syncing… 34/120", button.Find("button").TextContent.Trim());
        Assert.True(button.Find("button").HasAttribute("disabled"));
        Assert.Empty(button.FindAll(".sync-status"));
    }

    [Theory]
    [InlineData(null, 0, null, 0, "Syncing…")]
    [InlineData(120, 34, null, 0, "Syncing… 34/120")]
    [InlineData(0, 0, null, 0, "Syncing…")]
    [InlineData(5, 5, 8, 3, "Processing… 3/8")]
    [InlineData(5, 5, 0, 0, "Syncing…")]
    public void Progress_text(int? total, int downloaded, int? toProcess, int processed, string expected) =>
        Assert.Equal(expected, SyncButton.ProgressText(new SyncProgress(total, downloaded, toProcess, processed)));

    [Fact]
    public void Clicking_starts_a_sync()
    {
        sync.AfterStart = Running();
        var button = Render();

        button.Find("button").Click();

        Assert.Equal(1, sync.Starts);
        Assert.Equal("Syncing…", button.Find("button").TextContent.Trim());
        Assert.True(button.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Progress_updates_until_the_sync_finishes()
    {
        sync.AfterStart = Running();
        var button = Render();
        button.Find("button").Click();

        sync.State = Running(120, 34);
        Tick();
        button.WaitForAssertion(() => Assert.Equal("Syncing… 34/120", button.Find("button").TextContent.Trim()));

        sync.State = new SyncState(null, Evening.AddHours(1), null);
        sync.IsRunning = false;
        Tick();
        button.WaitForAssertion(() => Assert.Equal("Synced 2026-10-26 21:01", button.Find(".sync-status").TextContent));
        Assert.False(button.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void A_sync_started_elsewhere_shows_up()
    {
        var button = Render();

        sync.State = Running(10, 2);
        sync.IsRunning = true;
        Tick();

        button.WaitForAssertion(() => Assert.Equal("Syncing… 2/10", button.Find("button").TextContent.Trim()));
    }

    [Fact]
    public void While_idle_the_database_is_not_read()
    {
        Render();

        Tick();
        Tick();
        Tick();

        Assert.Equal(1, sync.Loads);
    }

    [Fact]
    public void A_failed_update_keeps_the_last_known_state_and_tries_again()
    {
        sync.State = Running(120, 34);
        sync.IsRunning = true;
        var button = Render();

        sync.LoadError = new InvalidOperationException("Database is down");
        Tick();
        button.WaitForAssertion(() => Assert.Equal(2, sync.Loads));
        Assert.Equal("Syncing… 34/120", button.Find("button").TextContent.Trim());

        sync.LoadError = null;
        sync.State = Running(120, 50);
        Tick();
        button.WaitForAssertion(() => Assert.Equal("Syncing… 50/120", button.Find("button").TextContent.Trim()));
    }

    [Fact]
    public async Task Polling_stops_when_the_component_goes_away()
    {
        sync.IsRunning = true;
        sync.State = Running();
        Render();

        await DisposeComponentsAsync();
        Tick();
        Tick();

        Assert.Equal(1, sync.Loads);
    }
}
