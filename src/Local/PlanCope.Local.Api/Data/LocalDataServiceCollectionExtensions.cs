using Microsoft.Data.Sqlite;
using Dapper;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;

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
        services.AddScoped<LocalDemoExamSeeder>();
        services.AddScoped<LocalAssetFileService>();
        services.AddScoped<LocalExamImportService>();
        services.AddScoped<LocalExamPullService>();
        services.AddHttpClient(nameof(LocalExamPullService));
        services.AddScoped<ILocalUserRepository, LocalUserRepository>();
        services.AddScoped<ILocalExamRepository, LocalExamRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IAttemptRepository, AttemptRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<ISyncStateRepository, SyncStateRepository>();

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return services;
    }
}
