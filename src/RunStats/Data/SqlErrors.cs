using Microsoft.Data.SqlClient;

namespace RunStats.Data;

public static class SqlErrors
{
    const int CannotOpenDatabase = 4060;
    const int LoginFailed = 18456;
    const int AccountLocked = 18486;
    const int PasswordExpired = 18487;
    const int PasswordMustChange = 18488;

    static readonly HashSet<int> Permanent = [CannotOpenDatabase, LoginFailed, AccountLocked, PasswordExpired, PasswordMustChange];

    // Anything that isn't a SqlException (e.g. a malformed connection string) won't go away by waiting either.
    public static bool IsPermanent(Exception exception) =>
        exception is not SqlException sql || Permanent.Contains(sql.Number);
}
