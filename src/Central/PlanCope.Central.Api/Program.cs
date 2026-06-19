using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Infrastructure.DependencyInjection;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("CentralDatabase") ??
                       "Host=localhost;Port=5432;Database=plan_cope_central;Username=plancope;Password=plancope";

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums como string (BlockType, etc.) para un contrato consistente con el frontend.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Plan Cope Central API", Version = "v1" });
    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Bearer token."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = JwtBearerDefaults.AuthenticationScheme
            }
        }] = []
    });
});
builder.Services.AddPlanCopeSharedInfrastructure();
builder.Services.AddDbContext<PlanCopeDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("PlanCope.Central.Migrations")));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddScoped<ITokenService, TokenService>();

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
if (string.IsNullOrWhiteSpace(authOptions.SigningKey))
{
    throw new InvalidOperationException("Auth:SigningKey must be configured with a secure secret.");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();

    // Las migraciones se aplican con `dotnet ef database update` (proyecto PlanCope.Central.Migrations).
    // Aca solo sembramos el usuario de testing; si el esquema no existe todavia, lo avisamos sin tumbar el arranque.
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PlanCopeDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await DevelopmentSeeder.SeedAsync(dbContext, app.Configuration, logger);
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "No se pudo sembrar el usuario de testing. ¿Corriste 'dotnet ef database update'?");
    }
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
