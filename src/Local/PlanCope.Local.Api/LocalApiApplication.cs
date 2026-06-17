using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Endpoints;
using PlanCope.Shared.Infrastructure.DependencyInjection;

namespace PlanCope.Local.Api;

public static class LocalApiApplication
{
    private const string HostUiCorsPolicy = "HostUi";

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddPlanCopeSharedInfrastructure();
        builder.Services.AddPlanCopeLocalData(builder.Configuration);
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(HostUiCorsPolicy, policy =>
            {
                policy
                    .WithOrigins(
                        "http://127.0.0.1:5173",
                        "http://localhost:5173",
                        "https://host.plancope.local")
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            scope.ServiceProvider.GetRequiredService<LocalDemoExamSeeder>().SeedIfEmpty();
        }

        app.UseCors(HostUiCorsPolicy);

        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            service = "local-api"
        }));
        app.MapExamEndpoints();
        app.MapSessionEndpoints();
        app.MapAttemptEndpoints();
        app.MapSyncEndpoints();
        app.MapTakePageEndpoints();

        return app;
    }
}
