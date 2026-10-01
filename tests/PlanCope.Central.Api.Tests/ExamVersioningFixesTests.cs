using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// Pins the versioning defect fixes: the default copy source prefers the highest published version,
/// a second draft is rejected unless forced, publishing out of order is rejected, and
/// <c>supersededAt</c> is clamped to the superseded version's own publication time.
/// </summary>
public sealed class ExamVersioningFixesTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    private static readonly IServiceProvider ControllerServices = CreateControllerServices();

    [Fact]
    public async Task Create_version_defaults_to_the_highest_published_version_ignoring_a_higher_draft()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        SeedExam(dbContext, "ex-1", "EXA-FIX-01");
        SeedVersion(dbContext, "ex-1", 1, "Published", t1, "v1");
        SeedVersion(dbContext, "ex-1", 2, "Published", t1.AddMinutes(1), "v2");
        SeedVersion(dbContext, "ex-1", 3, "Draft", null, "v3");
        await dbContext.SaveChangesAsync();

        // force: a draft (v3) already exists, so the default source must ignore it, not 409.
        var result = await controller.CreateVersion("ex-1", new CreateExamVersionRequest(Force: true), CancellationToken.None);

        var dto = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(4, dto.VersionNumber);
        Assert.Equal(2, dto.BasedOnVersionNumber);
        Assert.Equal("v2", Assert.Single(dto.Blocks).Config.GetProperty("question").GetString());
    }

    [Fact]
    public async Task Create_version_falls_back_to_the_highest_version_when_none_is_published()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        SeedExam(dbContext, "ex-2", "EXA-FIX-02");
        SeedVersion(dbContext, "ex-2", 1, "Draft", null, "v1");
        SeedVersion(dbContext, "ex-2", 2, "Draft", null, "v2");
        await dbContext.SaveChangesAsync();

        var result = await controller.CreateVersion("ex-2", new CreateExamVersionRequest(Force: true), CancellationToken.None);

        var dto = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(3, dto.VersionNumber);
        Assert.Equal(2, dto.BasedOnVersionNumber);
        Assert.Equal("v2", Assert.Single(dto.Blocks).Config.GetProperty("question").GetString());
    }

    [Fact]
    public async Task Create_version_returns_409_draft_exists_when_a_draft_already_exists()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        SeedExam(dbContext, "ex-3", "EXA-FIX-03");
        SeedVersion(dbContext, "ex-3", 1, "Published", t1, "v1");
        SeedVersion(dbContext, "ex-3", 2, "Draft", null, "v2");
        await dbContext.SaveChangesAsync();

        var result = await controller.CreateVersion("ex-3", new CreateExamVersionRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        var body = JsonSerializer.SerializeToElement(objectResult.Value);
        Assert.Equal("draft_exists", body.GetProperty("code").GetString());
        Assert.Equal("ev-ex-3-2", body.GetProperty("draftVersionId").GetString());
        Assert.Equal(2, await dbContext.ExamVersions.CountAsync(version => version.ExamId == "ex-3"));
    }

    [Fact]
    public async Task Create_version_draft_conflict_reports_the_highest_numbered_draft()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        SeedExam(dbContext, "ex-4", "EXA-FIX-04");
        SeedVersion(dbContext, "ex-4", 1, "Draft", null, "v1");
        SeedVersion(dbContext, "ex-4", 2, "Draft", null, "v2");
        await dbContext.SaveChangesAsync();

        var result = await controller.CreateVersion("ex-4", new CreateExamVersionRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        var body = JsonSerializer.SerializeToElement(objectResult.Value);
        Assert.Equal("draft_exists", body.GetProperty("code").GetString());
        Assert.Equal("ev-ex-4-2", body.GetProperty("draftVersionId").GetString());
    }

    [Fact]
    public async Task Create_version_with_force_creates_a_second_draft()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        SeedExam(dbContext, "ex-5", "EXA-FIX-05");
        SeedVersion(dbContext, "ex-5", 1, "Published", t1, "v1");
        SeedVersion(dbContext, "ex-5", 2, "Draft", null, "v2");
        await dbContext.SaveChangesAsync();

        var result = await controller.CreateVersion("ex-5", new CreateExamVersionRequest(Force: true), CancellationToken.None);

        var dto = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(3, dto.VersionNumber);
        Assert.Equal("Draft", dto.Status);
        Assert.Equal(2, await dbContext.ExamVersions.CountAsync(version => version.ExamId == "ex-5" && version.Status == "Draft"));
    }

    [Fact]
    public async Task Publishing_an_older_version_is_rejected_with_older_than_current()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        SeedExam(dbContext, "ex-6", "EXA-FIX-06");
        SeedVersion(dbContext, "ex-6", 1, "Published", t1, "v1");
        SeedVersion(dbContext, "ex-6", 2, "Draft", null, "v2");
        SeedVersion(dbContext, "ex-6", 3, "Published", t1.AddMinutes(1), "v3");
        await dbContext.SaveChangesAsync();

        var result = await controller.PublishVersion(
            "ev-ex-6-2",
            new PublishExamVersionRequest(null, null),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        var body = JsonSerializer.SerializeToElement(objectResult.Value);
        Assert.Equal("older_than_current", body.GetProperty("code").GetString());
        Assert.Empty(await dbContext.PublicationPackages.Where(package => package.ExamVersionId == "ev-ex-6-2").ToListAsync());
        Assert.Equal("Draft", (await dbContext.ExamVersions.SingleAsync(version => version.Id == "ev-ex-6-2")).Status);
    }

    [Fact]
    public async Task SupersededAt_of_a_published_version_equals_the_next_published_version_time()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddHours(2);

        SeedExam(dbContext, "ex-7", "EXA-FIX-07");
        SeedVersion(dbContext, "ex-7", 1, "Published", t1, "v1");
        SeedVersion(dbContext, "ex-7", 2, "Published", t2, "v2");
        await dbContext.SaveChangesAsync();

        var versions = await ListVersionsAsync(controller, "ex-7");
        var v1 = versions.Single(version => version.VersionNumber == 1);
        var v2 = versions.Single(version => version.VersionNumber == 2);

        Assert.Equal(t2, v1.SupersededAt);
        Assert.True(v2.IsCurrent);
        Assert.Null(v2.SupersededAt);

        // The list and detail DTOs must agree.
        var detail = await GetVersionAsync(controller, "ev-ex-7-1");
        Assert.Equal(t2, detail.SupersededAt);
    }

    [Fact]
    public async Task SupersededAt_clamps_a_clock_skewed_next_publication_to_the_versions_own_time()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var own = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var skewedEarlier = own.AddHours(-1);

        SeedExam(dbContext, "ex-8", "EXA-FIX-08");
        SeedVersion(dbContext, "ex-8", 1, "Published", own, "v1");
        SeedVersion(dbContext, "ex-8", 2, "Published", skewedEarlier, "v2");
        await dbContext.SaveChangesAsync();

        var versions = await ListVersionsAsync(controller, "ex-8");
        var v1 = versions.Single(version => version.VersionNumber == 1);

        Assert.Equal(own, v1.SupersededAt);
    }

    [Fact]
    public async Task Create_version_with_a_null_request_copies_the_default_source()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        SeedExam(dbContext, "ex-9", "EXA-FIX-09");
        SeedVersion(dbContext, "ex-9", 1, "Published", t1, "v1");
        await dbContext.SaveChangesAsync();

        var result = await controller.CreateVersion("ex-9", null, CancellationToken.None);

        var dto = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(2, dto.VersionNumber);
        Assert.Equal(1, dto.BasedOnVersionNumber);
        Assert.Equal("v1", Assert.Single(dto.Blocks).Config.GetProperty("question").GetString());
    }

    private static void SeedExam(PlanCopeDbContext dbContext, string examId, string code)
    {
        var now = DateTimeOffset.UtcNow;
        dbContext.Exams.Add(new Exam(examId, code, "Exam", null, [], null, null, "Draft", null, now, now));
    }

    private static void SeedVersion(
        PlanCopeDbContext dbContext,
        string examId,
        int versionNumber,
        string status,
        DateTimeOffset? publishedAt,
        string question)
    {
        var now = DateTimeOffset.UtcNow;
        var versionId = $"ev-{examId}-{versionNumber}";
        dbContext.ExamVersions.Add(new ExamVersion(
            versionId,
            examId,
            versionNumber,
            1,
            status,
            null,
            null,
            null,
            null,
            null,
            publishedAt,
            now,
            now,
            null));
        dbContext.ExamBlocks.Add(new ExamBlock(
            $"blk-{examId}-{versionNumber}",
            versionId,
            0,
            BlockType.TrueFalse,
            "P1",
            null,
            JsonDocument.Parse($$"""{"question":"{{question}}"}"""),
            null,
            now,
            now));
    }

    private static async Task<IReadOnlyList<ExamVersionDto>> ListVersionsAsync(ExamsController controller, string examId)
    {
        var result = await controller.ListVersions(examId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IReadOnlyList<ExamVersionDto>>(ok.Value);
    }

    private static async Task<ExamVersionDto> GetVersionAsync(ExamsController controller, string versionId)
    {
        var result = await controller.GetVersion(versionId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<ExamVersionDto>(ok.Value);
    }

    private static DbContextOptions<PlanCopeDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
    }

    private static ExamsController CreateController(PlanCopeDbContext dbContext)
    {
        return new ExamsController(
            dbContext,
            new ExamValidator(),
            new ExamVersionValidator(),
            new ExamBlockValidator())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = ControllerServices }
            }
        };
    }

    private static IServiceProvider CreateControllerServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        return services.BuildServiceProvider();
    }
}
