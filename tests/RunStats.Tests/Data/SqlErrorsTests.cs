using Npgsql;
using RunStats.Data;

namespace RunStats.Tests.Data;

// The errors come from a real PostgreSQL, so the tests also check which SQLSTATE each failure really has.
[Collection("Postgres")]
public class SqlErrorsTests(PostgresFixture postgres)
{
    static async Task<NpgsqlException> OpenFails(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return await Assert.ThrowsAnyAsync<NpgsqlException>(() => connection.OpenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Wrong_password_is_permanent()
    {
        var error = await OpenFails(postgres.ConnectionString("postgres", password: "wrong-Password1"));

        Assert.Equal(PostgresErrorCodes.InvalidPassword, Assert.IsType<PostgresException>(error).SqlState);
        Assert.True(SqlErrors.IsPermanent(error));
    }

    [Fact]
    public async Task Missing_database_is_permanent()
    {
        var error = await OpenFails(postgres.ConnectionString(PostgresFixture.NewDatabaseName()));

        Assert.Equal(PostgresErrorCodes.InvalidCatalogName, Assert.IsType<PostgresException>(error).SqlState);
        Assert.True(SqlErrors.IsPermanent(error));
    }

    [Fact]
    public async Task Database_without_connect_permission_is_permanent()
    {
        var database = PostgresFixture.NewDatabaseName();
        var role = $"role_{Guid.NewGuid():N}";
        await postgres.ExecuteAsync($"CREATE DATABASE {database}");
        await postgres.ExecuteAsync($"REVOKE CONNECT ON DATABASE {database} FROM PUBLIC; CREATE ROLE {role} LOGIN PASSWORD 'Role-Password1'");

        var error = await OpenFails(postgres.ConnectionString(database, password: "Role-Password1", username: role));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error).SqlState);
        Assert.True(SqlErrors.IsPermanent(error));
    }

    [Fact]
    public async Task Unreachable_server_is_retried()
    {
        var error = await OpenFails("Host=127.0.0.1;Port=1;Timeout=2;Username=x;Password=x");

        Assert.IsNotType<PostgresException>(error);
        Assert.False(SqlErrors.IsPermanent(error));
    }

    [Fact]
    public void Server_errors_other_than_access_are_retried()
    {
        Assert.False(SqlErrors.IsPermanent(new PostgresException("the database system is starting up", "FATAL", "FATAL",
            PostgresErrorCodes.CannotConnectNow)));
    }

    [Fact]
    public void Errors_other_than_NpgsqlException_are_permanent()
    {
        Assert.True(SqlErrors.IsPermanent(new ArgumentException("Format of the initialization string is not valid")));
    }
}
