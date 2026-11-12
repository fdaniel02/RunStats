using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RunStats.Data;

namespace RunStats.Tests.Data;

public class DatabaseSetupTests
{
    static IConfiguration Configuration(string? connectionString) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString }).Build();

    static AppDbContext Context(bool retryOnFailure) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseAppDatabase(Configuration("Host=localhost;Database=runstats"), retryOnFailure).Options);

    [Fact]
    public void Uses_PostgreSQL()
    {
        using var db = Context(retryOnFailure: true);

        Assert.True(db.Database.IsNpgsql());
    }

    [Fact]
    public void Retries_on_failure_when_asked()
    {
        using var db = Context(retryOnFailure: true);

        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void Does_not_retry_otherwise()
    {
        using var db = Context(retryOnFailure: false);

        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void Missing_connection_string_says_how_to_set_it()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(Configuration(null), retryOnFailure: true));

        Assert.Contains("ConnectionStrings__Default", error.Message);
    }
}
