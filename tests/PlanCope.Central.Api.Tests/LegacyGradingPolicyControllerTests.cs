using System.Security.Claims;
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
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class LegacyGradingPolicyControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task ListUnassigned_PublishedMultipleChoiceWithoutPolicy_Appears()
    {
        var options = CreateOptions();
        using var dbContext = await SeedAsync(
            options,
            versionScoringPolicy: null,
            hasMultipleChoiceBlock: true,
            existingAssignment: false);
        var controller = CreateController(dbContext, Principal("user-1"));

        var result = await controller.ListUnassigned(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<List<UnassignedExamVersionDto>>(ok.Value);
        var dto = Assert.Single(response);
        Assert.Equal("ev-1", dto.ExamVersionId);
        Assert.Equal("EXA-2026-01", dto.ExamCode);
        Assert.Equal(1, dto.VersionNumber);
        Assert.Equal(Now, dto.PublishedAt);
    }

    [Fact]
    public async Task ListUnassigned_PublishedMultipleChoiceWithValidPolicy_DoesNotAppear()
    {
        var options = CreateOptions();
        using var dbContext = await SeedAsync(
            options,
            versionScoringPolicy: "AllOrNothing",
            hasMultipleChoiceBlock: true,
            existingAssignment: false);
        var controller = CreateController(dbContext, Principal("user-1"));

        var result = await controller.ListUnassigned(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<List<UnassignedExamVersionDto>>(ok.Value);
        Assert.Empty(response);
    }

    [Fact]
    public async Task ListUnassigned_TrueFalseOnlyWithoutPolicy_DoesNotAppear()
    {
        var options = CreateOptions();
        using var dbContext = await SeedAsync(
            options,
            versionScoringPolicy: null,
            hasMultipleChoiceBlock: false,
            existingAssignment: false);
        var controller = CreateController(dbContext, Principal("user-1"));

        var result = await controller.ListUnassigned(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<List<UnassignedExamVersionDto>>(ok.Value);
        Assert.Empty(response);
    }

    [Fact]
    public async Task BulkAssign_UnparseableScoringPolicy_ReturnsBadRequestAndCreatesNoRows()
    {
        var options = CreateOptions();
        using var dbContext = await SeedAsync(
            options,
            versionScoringPolicy: null,
            hasMultipleChoiceBlock: true,
            existingAssignment: false);
        var controller = CreateController(dbContext, Principal("user-1"));

        var result = await controller.BulkAssign(
            new BulkAssignGradingPolicyRequest(new[] { "ev-1" }, "NotARealPolicy", null),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequest.Value);

        using var verify = new PlanCopeDbContext(options);
        Assert.Empty(verify.GradingPolicyAssignments);
    }

    [Fact]
    public async Task BulkAssign_ValidRequest_CreatesRowsWithCallerClaimAndRejectsAlreadyAssigned()
    {
        var options = CreateOptions();
        using var dbContext = await SeedAsync(
            options,
            versionScoringPolicy: null,
            hasMultipleChoiceBlock: true,
            existingAssignment: true);
        var controller = CreateController(dbContext, Principal("user-42"));

        var result = await controller.BulkAssign(
            new BulkAssignGradingPolicyRequest(new[] { "ev-already-assigned", "ev-new" }, "ProportionalPlain", "legacy backfill"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<BulkAssignGradingPolicyResult>(ok.Value);
        Assert.Equal(1, response.AssignedCount);
        var rejected = Assert.Single(response.RejectedExamVersionIds);
        Assert.Equal("ev-already-assigned", rejected);

        using var verify = new PlanCopeDbContext(options);
        var assignment = await verify.GradingPolicyAssignments.SingleAsync(a => a.ExamVersionId == "ev-new");
        Assert.Equal("ProportionalPlain", assignment.ScoringPolicy);
        Assert.Equal("user-42", assignment.AssignedBy);
        Assert.Equal("legacy backfill", assignment.Note);

        var preExisting = await verify.GradingPolicyAssignments.SingleAsync(a => a.ExamVersionId == "ev-already-assigned");
        Assert.Equal("AllOrNothing", preExisting.ScoringPolicy);
    }

    private static async Task<PlanCopeDbContext> SeedAsync(
        DbContextOptions<PlanCopeDbContext> options,
        string? versionScoringPolicy,
        bool hasMultipleChoiceBlock,
        bool existingAssignment)
    {
        var dbContext = new PlanCopeDbContext(options);
        var exam = new Exam(
            "ex-1",
            "EXA-2026-01",
            "Matematica · Primer Año",
            null,
            "Secundario",
            "Matematica",
            "Numeros y Operaciones",
            "Approved",
            null,
            Now,
            Now);
        var version = new ExamVersion(
            "ev-1",
            "ex-1",
            1,
            1,
            "Published",
            null,
            null,
            null,
            null,
            null,
            Now,
            Now,
            Now,
            versionScoringPolicy);
        var blockType = hasMultipleChoiceBlock ? BlockType.MultipleChoice : BlockType.TrueFalse;
        var blockConfig = hasMultipleChoiceBlock
            ? """{"question":"Cuanto es 2 + 2?","options":["3","4"]}"""
            : """{"question":"La Tierra es redonda."}""";
        var block = new ExamBlock(
            "blk-1",
            "ev-1",
            0,
            blockType,
            "Pregunta 1",
            null,
            JsonDocument.Parse(blockConfig),
            null,
            Now,
            Now);

        dbContext.Exams.Add(exam);
        dbContext.ExamVersions.Add(version);
        dbContext.ExamBlocks.Add(block);

        if (existingAssignment)
        {
            var secondVersion = new ExamVersion(
                "ev-new",
                "ex-1",
                2,
                1,
                "Published",
                null,
                null,
                null,
                null,
                null,
                Now,
                Now,
                Now,
                null);
            var secondBlock = new ExamBlock(
                "blk-2",
                "ev-new",
                0,
                BlockType.MultipleChoice,
                "Pregunta 2",
                null,
                JsonDocument.Parse("""{"question":"Cuanto es 2 + 2?","options":["3","4"]}"""),
                null,
                Now,
                Now);
            var preExisting = new GradingPolicyAssignment(
                "gpa-1",
                "ev-already-assigned",
                "AllOrNothing",
                "user-0",
                Now,
                null);
            dbContext.ExamVersions.Add(secondVersion);
            dbContext.ExamBlocks.Add(secondBlock);
            dbContext.GradingPolicyAssignments.Add(preExisting);
        }

        await dbContext.SaveChangesAsync();
        return dbContext;
    }

    private static DbContextOptions<PlanCopeDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
    }

    private static LegacyGradingPolicyController CreateController(
        PlanCopeDbContext dbContext,
        ClaimsPrincipal principal)
    {
        return new LegacyGradingPolicyController(dbContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static ClaimsPrincipal Principal(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}