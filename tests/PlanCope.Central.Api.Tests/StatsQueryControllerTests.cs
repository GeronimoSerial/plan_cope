using System.Security.Claims;
using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Contracts.Stats;
using PlanCope.TestSupport;
using Xunit;
using Testcontainers.PostgreSql;

namespace PlanCope.Central.Api.Tests;

public sealed partial class StatsQueryControllerTests
{
    [Fact]
    public async Task Summary_WithoutRosterScopeIsForbidden()
    {
        using var db = CreateDb();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim(ClaimTypes.Role, "Viewer")));

        Assert.IsType<ForbidResult>((await controller.Summary(CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Summary_CountsFreshSessionsSeparatelyFromGradedAttempts()
    {
        using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.DeliverySessions.AddRange(Enumerable.Range(1, 5).Select(index => new CentralDeliverySession($"session-{index}", $"local-{index}", "180000100", "version-1", null, null, "active", now, null, now, now, "node-1", "2026", null, null, 5, 1, 0, 0, now, now, "1.0")));
        await db.SaveChangesAsync();
        using var scope = AuthScope();
        var controller = Controller(db, scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), Principal(new Claim("roster_scope", "province")));

        var result = Assert.IsType<OkObjectResult>((await controller.Summary(CancellationToken.None)).Result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.True(json.RootElement.GetProperty("gradedAttempts").GetProperty("value").ValueKind == JsonValueKind.Number, json.RootElement.ToString());
        Assert.Equal(0, json.RootElement.GetProperty("gradedAttempts").GetProperty("value").GetInt32());
        Assert.Equal(5, json.RootElement.GetProperty("freshSessions").GetProperty("value").GetInt32());
    }

}
