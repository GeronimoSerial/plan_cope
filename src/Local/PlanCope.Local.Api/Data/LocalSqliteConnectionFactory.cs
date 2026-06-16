using Microsoft.Data.Sqlite;

namespace PlanCope.Local.Api.Data;

public sealed class LocalSqliteConnectionFactory(LocalDatabaseOptions options) : ILocalSqliteConnectionFactory
{
    public SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection(options.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            PRAGMA synchronous=NORMAL;
            PRAGMA cache_size=-4000;
            """;
        command.ExecuteNonQuery();

        return connection;
    }
}
