using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Stats;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class StatsShareServiceTests
{
    private static readonly IServiceProvider EfServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public void AdminMutationsRequireAdminWhilePublicSnapshotIsAnonymous()
    {
        var adminAuthorization = Assert.Single(typeof(StatsSharesAdminController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", adminAuthorization.Policy);
        Assert.NotEmpty(typeof(PublicStatsSharesController).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public async Task AdminMutationsFailClosedWithoutRosterScopeClaims()
    {
        using var db = CreateDb();
        var controller = new StatsSharesAdminController(new StatsShareService(db));
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test"))
            }
        };

        var create = await controller.Create(new StatsShareCreateRequest("course", new Dictionary<string, string?>()), CancellationToken.None);
        var revoke = await controller.Revoke("share-id", CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.ForbidResult>(create.Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.ForbidResult>(revoke);
    }

    [Fact]
    public async Task Create_StoresOnlyTokenHash_DefaultsToSevenDays_AndSnapshotIsImmutable()
    {
        using var db = CreateDb();
        db.ExamRollups.Add(Rollup("r1", "180000100", "4° grado", 8));
        await db.SaveChangesAsync();
        var service = new StatsShareService(db);

        var (created, error) = await service.CreateAsync(Request(), "admin-1", null, "", null, CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.StartsWith("/estadisticas/compartidas/", created.Url);
        var token = created.Url.Split('/').Last();
        Assert.Equal(43, token.Length);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var persisted = await db.StatsShares.SingleAsync();
        Assert.Equal(hash, persisted.TokenHash);
        Assert.DoesNotContain(token, persisted.TokenHash, StringComparison.Ordinal);
        Assert.InRange((persisted.ExpiresAt - persisted.CreatedAt).TotalDays, 6.99, 7.01);
        Assert.Contains(db.AuditLogs, log => log.Action == "stats.share.create" && !log.Payload!.RootElement.GetRawText().Contains(token, StringComparison.Ordinal));

        var first = await service.FindPublicAsync(token, CancellationToken.None);
        Assert.Equal(60, Assert.Single(first!.Rows).WeightedScorePercent.Value);
        Assert.Equal(60, first.TotalWeightedScorePercent.Value);
        db.ExamRollups.Add(Rollup("r2", "180000200", "4° grado", 100));
        await db.SaveChangesAsync();
        var second = await service.FindPublicAsync(token, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task Create_SuppressesSmallGroupsAndComplementaryTotals()
    {
        using var db = CreateDb();
        db.ExamRollups.AddRange(
            Rollup("small", "180000100", "4° grado", 3),
            Rollup("large", "180000200", "6° grado", 7));
        await db.SaveChangesAsync();
        var service = new StatsShareService(db);
        var (created, error) = await service.CreateAsync(Request("course"), "admin-1", null, "", null, CancellationToken.None);
        Assert.Null(error);

        var snapshot = await service.FindPublicAsync(created!.Url.Split('/').Last(), CancellationToken.None);
        Assert.Equal(2, snapshot!.Rows.Count);
        var hidden = Assert.Single(snapshot.Rows, x => x.Label == "Datos suprimidos por privacidad");
        Assert.Equal("suppressed", hidden.AttemptCount.Status);
        Assert.Null(hidden.AttemptCount.Value);
        Assert.Equal("suppressed", hidden.WeightedScorePercent.Status);
        var visible = Assert.Single(snapshot.Rows, x => x.Label == "6° grado");
        Assert.Equal(7, visible.AttemptCount.Value);
        Assert.Equal("suppressed", snapshot.TotalAttempts.Status);
        Assert.Null(snapshot.TotalAttempts.Value);
        Assert.Equal("suppressed", snapshot.TotalWeightedScorePercent.Status);
        Assert.DoesNotContain("4° grado", JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicJsonContainsNoCueSchoolVersionOrPrivateValues()
    {
        using var db = CreateDb();
        db.ExamRollups.Add(Rollup("rollup-private-id", "180000100", "Curso público", 8, "version-private-id"));
        await db.SaveChangesAsync();
        var service = new StatsShareService(db);
        var (created, _) = await service.CreateAsync(Request(), "admin-1", null, "", null, CancellationToken.None);
        var snapshot = await service.FindPublicAsync(created!.Url.Split('/').Last(), CancellationToken.None);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("180000100", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rollup-private-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("version-private-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("schoolName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("student", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_RestrictsSchoolScopeAndRejectsPrivateDimensionsAndLongExpiration()
    {
        using var db = CreateDb();
        db.ExamRollups.AddRange(Rollup("r1", "180000100", "4° grado", 8), Rollup("r2", "180000200", "6° grado", 8));
        await db.SaveChangesAsync();
        var service = new StatsShareService(db);

        var (restricted, error) = await service.CreateAsync(Request("course"), "admin-school", ["180000100"], "", null, CancellationToken.None);
        Assert.Null(error);
        var snapshot = await service.FindPublicAsync(restricted!.Url.Split('/').Last(), CancellationToken.None);
        Assert.Equal("4° grado", Assert.Single(snapshot!.Rows).Label);

        var badGroup = await service.CreateAsync(Request("school"), "admin-1", null, "", null, CancellationToken.None);
        Assert.Null(badGroup.Created);
        Assert.NotNull(badGroup.Error);
        var badVersion = await service.CreateAsync(Request("version"), "admin-1", null, "", null, CancellationToken.None);
        Assert.Null(badVersion.Created);
        var badFilter = await service.CreateAsync(new StatsShareCreateRequest("course", new Dictionary<string, string?> { ["school"] = "180000100" }), "admin-1", null, "", null, CancellationToken.None);
        Assert.Null(badFilter.Created);
        var versionFilter = await service.CreateAsync(new StatsShareCreateRequest("course", new Dictionary<string, string?> { ["version"] = "v1" }), "admin-1", null, "", null, CancellationToken.None);
        Assert.Null(versionFilter.Created);
        var tooLong = await service.CreateAsync(Request() with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(31) }, "admin-1", null, "", null, CancellationToken.None);
        Assert.Null(tooLong.Created);
        var withinLimit = await service.CreateAsync(Request() with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(30).AddSeconds(-1) }, "admin-1", null, "", null, CancellationToken.None);
        Assert.NotNull(withinLimit.Created);
        Assert.Null(withinLimit.Error);
        var outOfScopeFilter = await service.CreateAsync(new StatsShareCreateRequest("course", new Dictionary<string, string?> { ["course"] = "6° grado" }), "admin-school", ["180000100"], "", null, CancellationToken.None);
        Assert.Null(outOfScopeFilter.Created);
        Assert.NotNull(outOfScopeFilter.Error);
    }

    [Fact]
    public async Task RevokeAndExpirationReturnIndistinguishableMissingResult()
    {
        using var db = CreateDb();
        db.ExamRollups.Add(Rollup("r1", "180000100", "4° grado", 8));
        await db.SaveChangesAsync();
        var service = new StatsShareService(db);
        var (created, _) = await service.CreateAsync(Request(), "admin-1", null, "", null, CancellationToken.None);
        var token = created!.Url.Split('/').Last();
        Assert.NotNull(await service.FindPublicAsync(token, CancellationToken.None));
        Assert.False(await service.RevokeAsync(created.Id, "other-admin", null, false, CancellationToken.None));
        Assert.True(await service.RevokeAsync(created.Id, "admin-1", null, false, CancellationToken.None));
        Assert.Null(await service.FindPublicAsync(token, CancellationToken.None));
        Assert.False(await service.RevokeAsync(created.Id, "admin-1", null, false, CancellationToken.None));
        Assert.Single(db.AuditLogs.Where(x => x.Action == "stats.share.revoke"));

        var expiredToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiredHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expiredToken)));
        db.StatsShares.Add(new StatsShare("expired-share", expiredHash, "course", JsonDocument.Parse("{}"), JsonDocument.Parse("{}"), DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), null, "admin-1"));
        await db.SaveChangesAsync();
        Assert.Null(await service.FindPublicAsync(expiredToken, CancellationToken.None));
    }

    [Fact]
    public async Task PublicControllerReturns404ForMalformedAndMissingTokens()
    {
        using var db = CreateDb();
        var controller = new PublicStatsSharesController(new StatsShareService(db));
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new DefaultHttpContext() };
        var malformed = await controller.Read("bad", CancellationToken.None);
        var missing = await controller.Read(new string('A', 43), CancellationToken.None);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(malformed.Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(missing.Result);
    }

    [Fact]
    public async Task Create_RejectsMoreThanFiveThousandGroupsInsteadOfTruncating()
    {
        using var db = CreateDb();
        db.ExamRollups.AddRange(Enumerable.Range(0, 5001).Select(index => Rollup($"r-{index}", "180000100", $"Curso {index}", 5)));
        await db.SaveChangesAsync();
        var service = new StatsShareService(db);

        var (created, error) = await service.CreateAsync(Request("course"), "admin-1", null, "", null, CancellationToken.None);

        Assert.Null(created);
        Assert.NotNull(error);
        Assert.Contains("5000", error, StringComparison.Ordinal);
        Assert.Contains("agregá filtros", error, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotAggregateQueries_TranslateScopeGeographyAndAllPublicDimensionsToPostgres()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options;
        using var db = new PlanCopeDbContext(options);
        var service = new StatsShareService(db);
        var filtered = service.BuildFilteredRollups([180000100L], "2026", "4° grado", "locality-1", "department-1");
        var subjects = new Dictionary<string, string>(StringComparer.Ordinal) { ["version-1"] = "Matemática" };

        foreach (var dimension in new[] { "locality", "department", "course", "subject", "year" })
        {
            var sql = service.BuildGroupedQuery(dimension, filtered, subjects)
                .OrderBy(row => row.Label).ThenBy(row => row.Key).Take(5001).ToQueryString();
            Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SUM", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SchoolYear", sql, StringComparison.Ordinal);
            Assert.Contains("4° grado", sql, StringComparison.Ordinal);
        }
        var geographySql = service.BuildGroupedQuery("locality", filtered, subjects).ToQueryString();
        Assert.Contains("LEFT JOIN", geographySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DepartmentId", geographySql, StringComparison.Ordinal);
    }

    private static StatsShareCreateRequest Request(string groupBy = "locality") => new(groupBy, new Dictionary<string, string?>());

    private static ExamRollup Rollup(string id, string cue, string course, int attempts, string version = "v1") =>
        new(id, cue, "2026", course, version, attempts, attempts * 6d, attempts * 10d, DateTimeOffset.UtcNow);

    private static PlanCopeDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(EfServices)
            .Options;
        return new PlanCopeDbContext(options);
    }
}
