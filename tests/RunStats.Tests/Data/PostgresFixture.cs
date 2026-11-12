using Npgsql;
using Testcontainers.PostgreSql;

namespace RunStats.Tests.Data;

// One PostgreSQL container for all tests in the "Postgres" collection; each test uses its own database, which EF's
// MigrateAsync creates. Needs Docker (Docker Desktop locally; GitHub's Ubuntu runners have it).
public sealed class PostgresFixture : IAsyncLifetime
{
    readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18").Build();

    public string ConnectionString(string database, string? password = null, string? username = null)
    {
        // Without pooling: every test has its own database, and each pool keeps its idle connections open, which soon
        // exhausts PostgreSQL's 100 connections.
        var builder = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database, Pooling = false };
        if (password is not null)
            builder.Password = password;
        if (username is not null)
            builder.Username = username;
        return builder.ConnectionString;
    }

    // The container's user is a superuser; its own database "postgres" always exists.
    public string AdminConnectionString => ConnectionString("postgres");

    public static string NewDatabaseName() => $"runstats_{Guid.NewGuid():N}";

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
