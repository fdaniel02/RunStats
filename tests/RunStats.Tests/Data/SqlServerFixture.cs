using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace RunStats.Tests.Data;

// One SQL Server container for all tests in the "SqlServer" collection; each test uses its own database.
// Needs Docker (Docker Desktop locally; GitHub's Ubuntu runners have it).
public sealed class SqlServerFixture : IAsyncLifetime
{
    readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString(string database, string? password = null)
    {
        var builder = new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = database };
        if (password is not null)
            builder.Password = password;
        return builder.ConnectionString;
    }

    public static string NewDatabaseName() => $"RunStats_{Guid.NewGuid():N}";

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();
}

[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
