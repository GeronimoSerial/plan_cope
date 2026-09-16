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
            -- 8 MiB page cache: local DBs are small, so hot pages stay resident while leaving headroom for the OS and the WebView2 host on a 4 GB machine
            PRAGMA cache_size=-8000;
            -- 64 MiB mmap cap: mmap'd file pages remain OS-evictable under memory pressure, so reads skip the VFS copy without committing hard RAM on a 4 GB machine
            PRAGMA mmap_size=67108864;
            """;
        command.ExecuteNonQuery();

        return connection;
    }
}
