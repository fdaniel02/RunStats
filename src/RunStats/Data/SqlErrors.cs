using Npgsql;

namespace RunStats.Data;

public static class SqlErrors
{
    static readonly HashSet<string> Permanent =
    [
        PostgresErrorCodes.InvalidPassword,
        PostgresErrorCodes.InvalidAuthorizationSpecification,
        PostgresErrorCodes.InvalidCatalogName,
        PostgresErrorCodes.InsufficientPrivilege
    ];

    // An NpgsqlException that isn't a PostgresException never got an answer from the server (refused, timed out), so
    // waiting may help. Anything else (e.g. a malformed connection string) won't go away by waiting either.
    public static bool IsPermanent(Exception exception) => exception switch
    {
        PostgresException postgres => Permanent.Contains(postgres.SqlState),
        NpgsqlException => false,
        _ => true
    };
}
