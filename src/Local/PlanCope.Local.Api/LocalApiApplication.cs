using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Endpoints;
using PlanCope.Shared.Infrastructure.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using PlanCope.Local.Api.Services;
using PlanCope.Local.Api.Services.Stats;

namespace PlanCope.Local.Api;

public static class LocalApiApplication
{
    private const string HostUiCorsPolicy = "HostUi";

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var logFilePath = builder.Configuration["Logging:FilePath"];
        if (!string.IsNullOrWhiteSpace(logFilePath))
            builder.Logging.AddProvider(new RollingFileLoggerProvider(logFilePath));

        builder.Services.AddPlanCopeSharedInfrastructure();
        builder.Services.AddPlanCopeLocalData(builder.Configuration);
        builder.Services.AddSingleton<StatsHtmlReportBuilder>();
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
            scope.ServiceProvider.GetRequiredService<IStatsRollupRepository>()
                .SelfHealIfInconsistentAsync()
                .GetAwaiter()
                .GetResult();
            // Demo exams are development-only. Absence of configuration in a production build
            // must not inject compiled-in demo data into a real school node; only
            // appsettings.Development.json / the dev launch profile opt in.
            if (builder.Configuration.GetValue("Local:SeedDemoExam", false))
            {
                scope.ServiceProvider.GetRequiredService<LocalDemoExamSeeder>().SeedIfEmpty();
            }
        }

        app.UseCors(HostUiCorsPolicy);

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var isAllowlisted = path.StartsWithSegments("/api/health")
                || path.StartsWithSegments("/api/activation")
                || path.StartsWithSegments("/api/enrolment");

            if (!isAllowlisted)
            {
                if (!path.StartsWithSegments("/api"))
                {
                    await next(context);
                    return;
                }
                var nodeIdentityRepository = context.RequestServices.GetRequiredService<INodeIdentityRepository>();
                var identity = await nodeIdentityRepository.GetAsync(context.RequestAborted);
                var expired = await context.RequestServices.GetRequiredService<ActivationRevalidationService>()
                    .IsExpiredAsync(context.RequestAborted);
                var activationInProgress = await context.RequestServices.GetRequiredService<ActivationRevalidationService>()
                    .IsActivationInProgressAsync(context.RequestAborted);
                if (identity?.RevocationStage == "locked" || expired)
                {
                    context.Response.StatusCode = StatusCodes.Status423Locked;
                    await context.Response.WriteAsJsonAsync(new { error = "Este equipo está bloqueado. Reactivalo con una clave nueva." });
                    return;
                }
                if (activationInProgress)
                {
                    context.Response.StatusCode = StatusCodes.Status423Locked;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        errorCode = "activation_in_progress",
                        error = "La descarga inicial no finalizó. Reintentá la descarga para completar la activación."
                    });
                    return;
                }
            }

            await next(context);
        });

        var clientDistPath = LocalClientAppFiles.FindDistPath();
        if (clientDistPath is not null)
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(clientDistPath),
                RequestPath = string.Empty
            });
        }

        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            service = "local-api"
        }));
        app.MapExamEndpoints();
        app.MapAssetEndpoints();
        app.MapSessionEndpoints();
        app.MapRosterEndpoints();
        app.MapAttemptEndpoints();
        app.MapStatsEndpoints();
        app.MapSyncEndpoints();
        app.MapTakePageEndpoints();
        app.MapActivationEndpoints();
        app.MapEnrolmentEndpoints();

        return app;
    }
}
