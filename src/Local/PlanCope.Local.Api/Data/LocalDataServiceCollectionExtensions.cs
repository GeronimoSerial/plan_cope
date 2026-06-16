using Microsoft.Data.Sqlite;

namespace PlanCope.Local.Api.Data;

public static class LocalDataServiceCollectionExtensions
{
    public static IServiceCollection AddPlanCopeLocalData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LocalDatabase") ??
                               new SqliteConnectionStringBuilder
                               {
                                   DataSource = "plan-cope-local.db",
                                   Cache = SqliteCacheMode.Shared
                               }.ToString();

        services.AddSingleton(new LocalDatabaseOptions(connectionString));
        services.AddSingleton<ILocalSqliteConnectionFactory, LocalSqliteConnectionFactory>();
        services.AddSingleton<LocalDatabaseInitializer>();

        return services;
    }
}
