using PlanCope.Local.Api.Data;
using PlanCope.Shared.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlanCopeSharedInfrastructure();
builder.Services.AddPlanCopeLocalData(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<LocalDatabaseInitializer>().Initialize();
}

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "local-api"
}));

app.Run();
