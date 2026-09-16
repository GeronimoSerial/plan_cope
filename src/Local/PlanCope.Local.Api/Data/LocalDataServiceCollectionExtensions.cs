using Microsoft.Data.Sqlite;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Endpoints;
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
        services.AddScoped<LocalExamPullService>();
        services.AddScoped<LocalRosterPullService>();
        services.AddSingleton<IEmbeddedRosterSource, EmbeddedRosterSource>();
        services.Configure<RosterBundleOptions>(configuration.GetSection(RosterBundleOptions.SectionName));
        services.AddScoped<EmbeddedRosterSeeder>();
        services.AddScoped<LocalOutboxPushService>();
        services.AddScoped<ILocalRosterRepository, LocalRosterRepository>();
        services.AddScoped<IDocumentHmacService, DocumentHmacService>();
        services.AddSingleton<IStudentResolutionTokenService, StudentResolutionTokenService>();
        services.Configure<NominalizationOptions>(configuration.GetSection(NominalizationOptions.SectionName));
        // WindowsHardwareSignalReader is [SupportedOSPlatform("windows")]; this project stays a plain
        // net8.0 TFM so it keeps building/testing on any OS, but the type is only ever resolved at
        // runtime inside PlanCope.Local.Host, which is Windows-only.
#pragma warning disable CA1416
        services.AddScoped<IRawHardwareSignalReader, WindowsHardwareSignalReader>();
#pragma warning restore CA1416
        services.AddScoped<HardwareFingerprintService>();
        services.AddScoped<INodeIdentityRepository, NodeIdentityRepository>();
        services.AddScoped<NodeCredentialRefresher>();
        services.AddTransient<CentralCredentialHandler>();
        services.AddHttpClient(nameof(LocalExamPullService)).AddHttpMessageHandler<CentralCredentialHandler>();
        services.AddHttpClient(nameof(LocalRosterPullService)).AddHttpMessageHandler<CentralCredentialHandler>();
        services.AddHttpClient(nameof(LocalOutboxPushService)).AddHttpMessageHandler<CentralCredentialHandler>();
        services.AddHttpClient(nameof(NodeCredentialRefresher));
        services.AddHttpClient(nameof(EnrolmentEndpoints)).AddHttpMessageHandler<CentralCredentialHandler>();
        services.AddScoped<ILocalUserRepository, LocalUserRepository>();
        services.AddScoped<ILocalExamRepository, LocalExamRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IAttemptRepository, AttemptRepository>();
        services.AddScoped<IStatsRollupRepository, StatsRollupRepository>();
        services.AddScoped<IStatsQueryRepository, StatsQueryRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<ISyncStateRepository, SyncStateRepository>();
        services.AddScoped<RevocationEnforcer>();
        services.AddHostedService<RevocationEnforcementHostedService>();


        return services;
    }
}
