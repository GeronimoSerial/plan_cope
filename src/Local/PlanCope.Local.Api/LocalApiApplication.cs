using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Endpoints;
using PlanCope.Shared.Infrastructure.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using PlanCope.Local.Api.Services;

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
            scope.ServiceProvider.GetRequiredService<EmbeddedRosterSeeder>()
                .SeedAsync()
                .GetAwaiter()
                .GetResult();
            if (builder.Configuration.GetValue("Local:SeedDemoExam", true))
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
                var nodeIdentityRepository = context.RequestServices.GetRequiredService<INodeIdentityRepository>();
                var identity = await nodeIdentityRepository.GetAsync(context.RequestAborted);
                if (identity?.RevocationStage == "locked")
                {
                    context.Response.StatusCode = StatusCodes.Status423Locked;
                    await context.Response.WriteAsJsonAsync(new { error = "Este equipo está bloqueado. Reactivalo con una clave nueva." });
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
        app.MapSyncEndpoints();
        app.MapTakePageEndpoints();
        app.MapActivationEndpoints();
        app.MapEnrolmentEndpoints();

        return app;
    }
}
