using Microsoft.EntityFrameworkCore;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("CentralDatabase") ??
                       "Host=localhost;Port=5432;Database=plan_cope_central;Username=plancope;Password=plancope";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddPlanCopeSharedInfrastructure();
builder.Services.AddDbContext<PlanCopeDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
