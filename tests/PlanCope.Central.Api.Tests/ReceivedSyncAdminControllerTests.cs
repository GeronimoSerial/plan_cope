using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class ReceivedSyncAdminControllerTests
{
    [Fact]
    public void Received_sync_admin_controller_requires_admin_role()
    {
        var authorize = typeof(ReceivedSyncAdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin", authorize?.Roles);
    }

    [Fact]
    public async Task List_pages_newest_received_attempts_first()
    {
        using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.ReceivedStudentAttempts.AddRange(
            Attempt("older", now.AddMinutes(-2)),
            Attempt("newer", now));
        await dbContext.SaveChangesAsync();
        var controller = new ReceivedSyncAdminController(dbContext, new CentralStatsRollupService(dbContext), NullLogger<ReceivedSyncAdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = Assert.IsType<OkObjectResult>(await controller.List(page: 1, pageSize: 1, CancellationToken.None));
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("newer", json.RootElement.GetProperty("items")[0].GetProperty("attemptId").GetString());
    }

    private static ReceivedStudentAttempt Attempt(string id, DateTimeOffset receivedAt) => new(
        id,
        $"local-{id}",
        null,
        "student-code",
        "submitted",
        null,
        null,
        receivedAt,
        $"key-{id}",
        receivedAt);

    private static PlanCopeDbContext CreateDbContext()
    {
        var services = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
            .BuildServiceProvider();
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(services)
            .Options;
        return new PlanCopeDbContext(options);
    }
}
