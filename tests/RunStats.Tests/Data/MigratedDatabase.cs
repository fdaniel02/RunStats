using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;

namespace RunStats.Tests.Data;

// A new, migrated database on the shared PostgreSQL container.
public static class MigratedDatabase
{
    public static async Task<PooledDbContextFactory<AppDbContext>> CreateAsync(PostgresFixture postgres)
    {
        var factory = new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString(PostgresFixture.NewDatabaseName())).Options);
        await using var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return factory;
    }
}
