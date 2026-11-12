using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Features.Pace;
using RunStats.Tests.Data;

namespace RunStats.Tests.Features;

[Collection("Postgres")]
public sealed class PaceQueriesTests(PostgresFixture postgres) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    PooledDbContextFactory<AppDbContext> db = null!;
    PaceQueries queries = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(postgres);
        queries = new PaceQueries(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static PersonalRecord Record(int typeId, double value, long? runId = 1) => new()
    {
        TypeId = typeId,
        Value = value,
        RunId = runId,
        AchievedAtUtc = new DateTime(2026, 9, 27, 23, 0, 0, DateTimeKind.Utc),
        AchievedAtLocal = new DateTime(2026, 9, 28, 1, 0, 0),
        UpdatedAtUtc = DateTime.UtcNow
    };

    static RacePrediction Prediction(int typeId, double seconds, int day = 5) =>
        new() { TypeId = typeId, Seconds = seconds, PredictedOn = new DateOnly(2026, 11, day) };

    [Fact]
    public async Task Without_data_there_are_no_references()
    {
        var data = await queries.LoadAsync(Ct);

        Assert.Empty(data.References);
        Assert.Null(data.PredictedOn);
    }

    [Fact]
    public async Task Predictions_and_records_share_a_row_per_distance_shortest_first()
    {
        await using (var context = await db.CreateDbContextAsync(Ct))
        {
            context.PersonalRecords.AddRange(Record(7, 21530), Record(3, 1250), Record(1, 200, runId: null));
            context.RacePredictions.AddRange(Prediction(6, 14340), Prediction(3, 1310, day: 4));
            await context.SaveChangesAsync(Ct);
        }

        var data = await queries.LoadAsync(Ct);

        Assert.Equal(
        [
            new PaceReference(1, 1000, null, 200, new DateOnly(2026, 9, 28), null),
            new PaceReference(3, 5000, 1310, 1250, new DateOnly(2026, 9, 28), 1),
            new PaceReference(6, 42195, 14340, null, null, null)
        ], data.References);
        Assert.Equal(new DateOnly(2026, 11, 5), data.PredictedOn);
    }
}
