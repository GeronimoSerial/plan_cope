using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using PlanCope.Central.Api.Data;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class RosterSyncHostTests
{
    private const string SigningKey = "roster-host-test-signing-key-32-bytes-minimum";
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Real_host_starts_with_empty_roster_environment_values_and_endpoint_returns_401_and_403()
    {
        var variables = new Dictionary<string, string?>
        {
            ["ConnectionStrings__CentralDatabase"] = "Host=localhost;Database=roster_host_test;Username=test;Password=test",
            ["ConnectionStrings__Asistencias"] = "",
            ["Rosters__Source"] = "",
            ["Rosters__SchoolYear"] = "",
            ["Rosters__DailySyncTime"] = " ",
            ["Rosters__TimeZoneId"] = "",
            ["Auth__SigningKey"] = SigningKey,
            ["Auth__Issuer"] = "roster-test-issuer",
            ["Auth__Audience"] = "roster-test-audience"
        };
        var previous = variables.ToDictionary(static pair => pair.Key,
            static pair => Environment.GetEnvironmentVariable(pair.Key), StringComparer.Ordinal);
        try
        {
            foreach (var variable in variables) Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            await using var factory = new RosterHostFactory();
            using var client = factory.CreateClient();

            var live = await client.GetAsync("/health/live");
            Assert.Equal(System.Net.HttpStatusCode.OK, live.StatusCode);

            var unauthenticated = await client.PostAsync("/api/admin/rosters/sync-all", content: null);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken("Viewer"));
            var nonAdmin = await client.PostAsync("/api/admin/rosters/sync-all", content: null);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, nonAdmin.StatusCode);
            var forbiddenStatus = await client.GetAsync("/api/admin/rosters/sync-all/status");
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbiddenStatus.StatusCode);

            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateToken("Admin"));
            var status = await client.GetAsync("/api/admin/rosters/sync-all/status");
            Assert.Equal(System.Net.HttpStatusCode.OK, status.StatusCode);
            var disabledSync = await client.PostAsync("/api/admin/rosters/sync-all", content: null);
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, disabledSync.StatusCode);
        }
        finally
        {
            foreach (var variable in previous) Environment.SetEnvironmentVariable(variable.Key, variable.Value);
        }
    }

    private static string CreateToken(string role)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("roster-test-issuer", "roster-test-audience",
            [new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, role)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class RosterHostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CentralDatabase"] = "Host=localhost;Database=roster_host_test;Username=test;Password=test",
                ["ConnectionStrings:Asistencias"] = "",
                ["Rosters:Source"] = "",
                ["Rosters:SchoolYear"] = "",
                ["Rosters:DailySyncTime"] = " ",
                ["Rosters:TimeZoneId"] = "",
                ["Auth:SigningKey"] = SigningKey,
                ["Auth:Issuer"] = "roster-test-issuer",
                ["Auth:Audience"] = "roster-test-audience"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<PlanCopeDbContext>();
                services.RemoveAll<DbContextOptions<PlanCopeDbContext>>();
                services.AddDbContext<PlanCopeDbContext>(options => options
                    .UseInMemoryDatabase("roster-host-startup")
                    .UseInternalServiceProvider(InMemoryServices));
            });
        }
    }
}
