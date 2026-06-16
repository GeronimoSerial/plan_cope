using System.Reflection;
using DbUp;

namespace PlanCope.Local.Api.Data;

public sealed class LocalDatabaseInitializer(LocalDatabaseOptions options)
{
    public void Initialize()
    {
        var upgrader = DeployChanges.To
            .SQLiteDatabase(options.ConnectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException("Local database migration failed.", result.Error);
        }
    }
}
