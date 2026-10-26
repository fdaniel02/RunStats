using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;
using RunStats.Processing;
using RunStats.Tests.Data;

namespace RunStats.Tests.Processing;

[Collection("SqlServer")]
public sealed class PersonalRecordUpdateTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    readonly FixedTimeProvider time = new(new DateTimeOffset(2026, 10, 26, 20, 0, 0, TimeSpan.Zero));
    PooledDbContextFactory<AppDbContext> db = null!;
    PersonalRecordUpdate update = null!;

    public async ValueTask InitializeAsync()
    {
        db = await MigratedDatabase.CreateAsync(sqlServer);
        update = new PersonalRecordUpdate(db, time);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static PersonalRecord Record(int typeId, double value, long runId = 1, string rawJson = "{}") => new()
    {
        TypeId = typeId,
        Value = value,
        RunId = runId,
        AchievedAtUtc = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
        AchievedAtLocal = new DateTime(2026, 10, 1, 11, 0, 0),
        RawJson = rawJson
    };

    async Task<List<PersonalRecord>> StoredAsync()
    {
        await using var context = await db.CreateDbContextAsync(Ct);
        return await context.PersonalRecords.AsNoTracking().OrderBy(r => r.TypeId).ToListAsync(Ct);
    }

    [Fact]
    public async Task First_records_are_added()
    {
        var changes = await update.ApplyAsync([Record(3, 1250), Record(7, 21097.5)], Ct);

        Assert.Equal(new PersonalRecordChanges(2, 0, 0), changes);
        var stored = await StoredAsync();
        Assert.Equal([3, 7], stored.Select(r => r.TypeId));
        Assert.All(stored, r => Assert.Equal(time.GetUtcNow().UtcDateTime, r.UpdatedAtUtc));
    }

    [Fact]
    public async Task Improved_record_is_changed_and_others_keep_their_date()
    {
        await update.ApplyAsync([Record(3, 1250), Record(4, 2700, rawJson: """{"a":1,"b":2}""")], Ct);
        var firstUpdate = time.GetUtcNow().UtcDateTime;
        time.UtcNow = time.UtcNow.AddDays(1);

        var changes = await update.ApplyAsync([Record(3, 1199, runId: 2), Record(4, 2700, rawJson: """{"b":2,"a":1}""")], Ct);

        Assert.Equal(new PersonalRecordChanges(0, 1, 0), changes);
        var stored = await StoredAsync();
        Assert.Equal((1199.0, 2L, time.GetUtcNow().UtcDateTime), (stored[0].Value, stored[0].RunId!.Value, stored[0].UpdatedAtUtc));
        Assert.Equal(firstUpdate, stored[1].UpdatedAtUtc);
        Assert.Equal("""{"b":2,"a":1}""", stored[1].RawJson);
    }

    [Fact]
    public async Task Record_moved_to_another_time_counts_as_changed()
    {
        await update.ApplyAsync([Record(3, 1250)], Ct);
        var moved = Record(3, 1250);
        moved.AchievedAtUtc = moved.AchievedAtUtc.AddDays(3);

        Assert.Equal(new PersonalRecordChanges(0, 1, 0), await update.ApplyAsync([moved], Ct));
    }

    [Fact]
    public async Task Records_Garmin_no_longer_reports_are_removed()
    {
        await update.ApplyAsync([Record(3, 1250), Record(7, 21097.5)], Ct);

        var changes = await update.ApplyAsync([Record(7, 21097.5)], Ct);

        Assert.Equal(new PersonalRecordChanges(0, 0, 1), changes);
        Assert.Equal([7], (await StoredAsync()).Select(r => r.TypeId));
    }

    [Fact]
    public async Task A_type_listed_twice_is_stored_once()
    {
        var changes = await update.ApplyAsync([Record(3, 1250), Record(3, 1300)], Ct);

        Assert.Equal(new PersonalRecordChanges(1, 0, 0), changes);
        Assert.Equal(1250, Assert.Single(await StoredAsync()).Value);
    }
}
