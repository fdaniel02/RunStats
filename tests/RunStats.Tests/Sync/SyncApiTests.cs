using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RunStats.Data;
using RunStats.Garmin;
using RunStats.Sync;
using RunStats.Tests.Data;
using static RunStats.Tests.Sync.FakeGarminGateway;

namespace RunStats.Tests.Sync;

// The whole app with a fake gateway: the API, the coordinator and the background worker together.
[Collection("SqlServer")]
public sealed class SyncApiTests : IDisposable
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly FakeGarminGateway garmin = new();
    readonly WebApplicationFactory<Program> factory;

    public SyncApiTests(SqlServerFixture sqlServer)
    {
        var connectionString = sqlServer.ConnectionString(SqlServerFixture.NewDatabaseName());
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", connectionString)
            .ConfigureServices(s =>
            {
                s.RemoveAll<IGarminGateway>();
                s.AddSingleton<IGarminGateway>(garmin);
            }));
    }

    public void Dispose() => factory.Dispose();

    async Task<JsonElement> WaitUntilFinishedAsync(HttpClient client, Uri location)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var sync = await client.GetFromJsonAsync<JsonElement>(location, Ct);
            if (sync.GetProperty("status").GetString() != "Running")
                return sync;
            await Task.Delay(50, Ct);
        }
        throw new TimeoutException("The sync didn't finish.");
    }

    [Fact]
    public async Task Post_starts_a_sync_in_the_background_and_get_reports_its_result()
    {
        garmin.Activities.AddRange([Activity(1), Activity(2, "treadmill_running", 18, 1)]);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/sync", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var request = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(request.GetProperty("started").GetBoolean());
        Assert.Equal($"/api/sync/{request.GetProperty("syncId").GetInt32()}", response.Headers.Location!.ToString());
        var sync = await WaitUntilFinishedAsync(client, response.Headers.Location);
        Assert.Equal("Succeeded", sync.GetProperty("status").GetString());
        Assert.Equal("Schedule", sync.GetProperty("trigger").GetString());
        Assert.Equal(2, sync.GetProperty("runsAdded").GetInt32());
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(2, await db.Runs.CountAsync(Ct));
    }

    [Theory]
    [InlineData("manual", "Manual")]
    [InlineData("SCHEDULE", "Schedule")]
    public async Task Trigger_can_be_given(string trigger, string recorded)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/sync?trigger={trigger}", null, Ct);

        var sync = await WaitUntilFinishedAsync(client, response.Headers.Location!);
        Assert.Equal(recorded, sync.GetProperty("trigger").GetString());
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("1")]
    public async Task Unknown_trigger_is_rejected(string trigger)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/sync?trigger={trigger}", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_while_a_sync_runs_returns_the_running_sync()
    {
        using var release = new SemaphoreSlim(0);
        garmin.Activities.Add(Activity(1));
        garmin.Fail = call =>
        {
            if (call == "list 0")
                release.Wait(Ct);
            return null;
        };
        using var client = factory.CreateClient();
        var first = await client.PostAsync("/api/sync", null, Ct);

        var second = await client.PostAsync("/api/sync", null, Ct);
        release.Release();

        var secondRequest = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.False(secondRequest.GetProperty("started").GetBoolean());
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        await WaitUntilFinishedAsync(client, first.Headers.Location!);
    }

    [Fact]
    public async Task Unknown_sync_is_not_found()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sync/12345", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_sync_that_crashes_is_logged_and_the_next_one_can_start()
    {
        var logs = new FakeLogCollector();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<ILoggerProvider>(new FakeLoggerProvider(logs));
            s.RemoveAll<SyncJob>();
            s.AddTransient<SyncJob>(_ => throw new InvalidOperationException("Can't create the job"));
        }));
        using var client = app.CreateClient();

        var first = await client.PostAsync("/api/sync", null, Ct);
        var crash = await WaitForLogAsync(logs, 15);
        var coordinator = app.Services.GetRequiredService<SyncCoordinator>();
        for (var attempt = 0; attempt < 100 && coordinator.RunningSyncId is not null; attempt++)
            await Task.Delay(20, Ct);
        var second = await client.PostAsync("/api/sync", null, Ct);

        Assert.IsType<InvalidOperationException>(crash.Exception);
        Assert.NotEqual(first.Headers.Location, second.Headers.Location);
    }

    static async Task<FakeLogRecord> WaitForLogAsync(FakeLogCollector logs, int eventId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (logs.GetSnapshot().FirstOrDefault(r => r.Id.Id == eventId) is { } record)
                return record;
            await Task.Delay(50, Ct);
        }
        throw new TimeoutException($"No log event {eventId}.");
    }
}
