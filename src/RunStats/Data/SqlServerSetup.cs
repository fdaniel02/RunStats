using Microsoft.EntityFrameworkCore;

namespace RunStats.Data;

public static class SqlServerSetup
{
    // Bridges a SQL Server restart of about half a minute; longer would leave a click hanging instead of showing an error.
    const int MaxRetryCount = 5;
    static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    public static TBuilder UseAppDatabase<TBuilder>(this TBuilder options, IConfiguration configuration, bool retryOnFailure)
        where TBuilder : DbContextOptionsBuilder
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. Set it with " +
                "'dotnet user-secrets set \"ConnectionStrings:Default\" \"...\"' for local development, " +
                "or the environment variable 'ConnectionStrings__Default' in Docker.");
        options.UseSqlServer(connectionString, sql =>
        {
            if (retryOnFailure)
                sql.EnableRetryOnFailure(MaxRetryCount, MaxRetryDelay, errorNumbersToAdd: null);
        });
        return options;
    }
}
