using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RunStats.Data;

namespace RunStats.Tests.Data;

// A new, migrated database on the shared SQL Server container.
public static class MigratedDatabase
{
    public static async Task<PooledDbContextFactory<AppDbContext>> CreateAsync(SqlServerFixture sqlServer)
    {
        var factory = new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(sqlServer.ConnectionString(SqlServerFixture.NewDatabaseName())).Options);
        await using var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return factory;
    }
}
