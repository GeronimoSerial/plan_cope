using System.Data;
using Dapper;

namespace PlanCope.Local.Api.Data.Repositories;

public static class Schools
{
    public static Task EnsureRowAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string cue,
        CancellationToken cancellationToken = default)
    {
        return connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT OR IGNORE INTO schools (cue, created_at)
            VALUES (@Cue, datetime('now'));
            """,
            new { Cue = cue },
            transaction,
            cancellationToken: cancellationToken));
    }
}