using Microsoft.EntityFrameworkCore;

namespace RunStats.Data;

public class DatabaseMigrator(
    [FromKeyedServices(DatabaseMigrator.DbFactoryKey)] IDbContextFactory<AppDbContext> dbFactory, ILogger<DatabaseMigrator> logger)
{
    public const string DbFactoryKey = "migration";

    // The SQL Server container may still be starting when the app boots.
    public int MaxAttempts { get; init; } = 15;
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(4);

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        new StartupRetry(logger, MaxAttempts, Delay).RunAsync("Database migration", async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Database.MigrateAsync(ct);
        }, SqlErrors.IsPermanent, cancellationToken);
}
