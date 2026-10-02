using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class SyncStatusEndpointTests
{
    [Fact]
    public async Task Status_endpoint_returns_null_for_heartbeat_status_after_timeout()
    {
        using var factory = new SyncStatusApiFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var state = scope.ServiceProvider.GetRequiredService<ISyncStateRepository>();
            await state.UpsertAsync(new SyncState("heartbeat", "last_heartbeat_http_status", "null", DateTimeOffset.UtcNow.ToString("O")));
            await state.UpsertAsync(new SyncState("heartbeat-error", "last_heartbeat_error_code", "\"request_timeout\"", DateTimeOffset.UtcNow.ToString("O")));
        }

        using var response = await client.GetAsync("/api/sync/status");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("heartbeatLastHttpStatus").ValueKind);
        Assert.Equal("request_timeout", body.RootElement.GetProperty("heartbeatLastErrorCode").GetString());
    }

    private sealed class SyncStatusApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-sync-status-{Guid.NewGuid():N}.db");
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                ["Local:SeedDemoExam"] = "false"
            }));
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }
}
