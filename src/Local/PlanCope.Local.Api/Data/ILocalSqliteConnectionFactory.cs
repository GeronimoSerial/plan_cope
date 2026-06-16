using Microsoft.Data.Sqlite;

namespace PlanCope.Local.Api.Data;

public interface ILocalSqliteConnectionFactory
{
    SqliteConnection CreateOpenConnection();
}
