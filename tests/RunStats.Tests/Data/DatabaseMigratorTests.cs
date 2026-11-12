using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Testing;
using RunStats.Data;

namespace RunStats.Tests.Data;

[Collection("Postgres")]
public class DatabaseMigratorTests(PostgresFixture postgres)
{
    readonly FakeLogger<DatabaseMigrator> logger = new();

    static PooledDbContextFactory<AppDbContext> Factory(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    [Fact]
    public async Task Applies_all_migrations_to_an_empty_database()
    {
        var factory = Factory(postgres.ConnectionString(PostgresFixture.NewDatabaseName()));

        await new DatabaseMigrator(factory, logger).MigrateAsync(TestContext.Current.CancellationToken);

        await using var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task Wrong_password_fails_without_retrying()
    {
        var factory = Factory(postgres.ConnectionString(PostgresFixture.NewDatabaseName(), password: "wrong-Password1"));
        var migrator = new DatabaseMigrator(factory, logger) { Delay = TimeSpan.FromMinutes(1) };

        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.InvalidPassword, error.SqlState);
        Assert.Empty(logger.Collector.GetSnapshot());
    }
}
