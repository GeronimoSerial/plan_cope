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
using PlanCope.Shared.Infrastructure.Validation;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// Pins the authoring contract: creating an exam leaves an empty draft version ready to be built,
/// the computed publication state advances draft -> ready_to_publish -> published, and an empty
/// version can never be published.
/// </summary>
public sealed class ExamAuthoringPublicationTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    private static readonly IServiceProvider ControllerServices = CreateControllerServices();

    [Fact]
    public async Task Create_exam_returns_initial_draft_version()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var result = await controller.Create(
            new CreateExamRequest("EXA-2026-01", "Matematica", null, ["secundaria-1"], "Matematica", "Numeros"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var summary = Assert.IsType<ExamSummaryDto>(created.Value);
        Assert.False(string.IsNullOrWhiteSpace(summary.InitialVersionId));
        Assert.Equal(1, summary.VersionCount);
        Assert.Equal(ExamPublicationStates.Draft, summary.PublicationState);

        var version = await dbContext.ExamVersions.SingleAsync();
        Assert.Equal(summary.InitialVersionId, version.Id);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("Draft", version.Status);
    }

    [Fact]
    public async Task Publication_state_transitions_draft_ready_to_publish_published()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var create = await controller.Create(
            new CreateExamRequest("EXA-2026-02", "Lengua", null, ["primaria-1", "secundaria-1"], null, null),
            CancellationToken.None);
        var createdSummary = Assert.IsType<ExamSummaryDto>(Assert.IsType<CreatedAtActionResult>(create.Result).Value);
        var examId = createdSummary.Id;
        var versionId = createdSummary.InitialVersionId!;

        Assert.Equal(ExamPublicationStates.Draft, (await ListExamsAsync(controller)).Single(x => x.Id == examId).PublicationState);

        var upsert = await controller.UpsertBlock(
            versionId,
            0,
            new UpsertBlockRequest(0, BlockType.TrueFalse, "P1", null, Json("""{"question":"La Tierra es redonda","correct":true}"""), null),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(upsert.Result);

        Assert.Equal(ExamPublicationStates.ReadyToPublish, (await ListExamsAsync(controller)).Single(x => x.Id == examId).PublicationState);

        var publish = await controller.PublishVersion(
            versionId,
            new PublishExamVersionRequest(null, null, ["legacy-node"], ["legacy-school"]),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(publish.Result);

        var published = (await ListExamsAsync(controller)).Single(x => x.Id == examId);
        Assert.Equal(ExamPublicationStates.Published, published.PublicationState);
        Assert.Equal(versionId, published.PublishedVersionId);
        Assert.Equal(1, published.PublishedVersionNumber);
        Assert.NotNull(published.PublishedAt);
        Assert.NotNull(published.Targets);
        Assert.Contains(published.Targets!, target =>
            target.TargetType == PublicationTargetTypes.Grade && target.TargetId == "primaria-1");
        Assert.Contains(published.Targets!, target =>
            target.TargetType == PublicationTargetTypes.Grade && target.TargetId == "secundaria-1");
        Assert.DoesNotContain(published.Targets!, target =>
            target.TargetType is PublicationTargetTypes.Node or PublicationTargetTypes.School);
        Assert.DoesNotContain(await dbContext.PublicationTargets.ToListAsync(), target =>
            target.TargetType is PublicationTargetTypes.Node or PublicationTargetTypes.School);
        Assert.Equal(0, published.PulledByNodeCount);

        var duplicatePublish = await controller.PublishVersion(
            versionId,
            new PublishExamVersionRequest(null, null),
            CancellationToken.None);

        var duplicateConflict = Assert.IsAssignableFrom<ObjectResult>(duplicatePublish.Result);
        Assert.Equal(StatusCodes.Status409Conflict, duplicateConflict.StatusCode);
        Assert.Single(await dbContext.PublicationPackages.ToListAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidCourseLists))]
    public async Task Create_exam_rejects_missing_or_unknown_courses(IReadOnlyList<string>? courses)
    {
        using var dbContext = new PlanCopeDbContext(CreateOptions());
        var controller = CreateController(dbContext);

        var result = await controller.Create(
            new CreateExamRequest("EXA-INVALID-COURSE", "Examen", null, courses, null, null),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("Courses", problem.Errors.Keys);
    }

    public static IEnumerable<object?[]> InvalidCourseLists =>
    [
        [Array.Empty<string>()],
        [new[] { "unknown-course" }]
    ];

    [Fact]
    public async Task Create_exam_rejects_an_empty_area_when_provided()
    {
        using var dbContext = new PlanCopeDbContext(CreateOptions());
        var controller = CreateController(dbContext);

        var result = await controller.Create(
            new CreateExamRequest("EXA-INVALID-AREA", "Examen", null, ["primaria-1"], "   ", null),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("Area", problem.Errors.Keys);
    }

    [Fact]
    public async Task Publish_version_with_no_blocks_is_rejected()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var create = await controller.Create(
            new CreateExamRequest("EXA-2026-03", "Historia", null, ["secundaria-1"], null, null),
            CancellationToken.None);
        var versionId = Assert.IsType<ExamSummaryDto>(
            Assert.IsType<CreatedAtActionResult>(create.Result).Value).InitialVersionId!;

        var publish = await controller.PublishVersion(
            versionId,
            new PublishExamVersionRequest(null, null),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(publish.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        using var verify = new PlanCopeDbContext(options);
        Assert.Empty(verify.PublicationPackages);
        Assert.Equal("Draft", (await verify.ExamVersions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Publish_rejects_a_question_referencing_a_missing_image_asset()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var exam = await CreateExamAsync(controller, "EXA-MISSING-IMAGE");
        var versionId = exam.InitialVersionId!;

        var upsert = await controller.UpsertBlock(versionId, 0,
            new UpsertBlockRequest(0, BlockType.TrueFalse, "Q", null,
                Json("{\"question\":\"Q\",\"imageAssetId\":\"missing-asset\"}"), null), CancellationToken.None);
        Assert.IsType<OkObjectResult>(upsert.Result);

        var publish = await controller.PublishVersion(versionId,
            new PublishExamVersionRequest(null, null), CancellationToken.None);

        var result = Assert.IsAssignableFrom<ObjectResult>(publish.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Contains(problem.Errors.Values.SelectMany(static messages => messages),
            message => message.Contains("Cada imagen de pregunta", StringComparison.Ordinal));
        Assert.Empty(await dbContext.PublicationPackages.ToListAsync());
    }

    [Fact]
    public async Task Asset_upload_rejects_unsupported_types_and_oversized_content()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var exam = await CreateExamAsync(controller, "EXA-ASSET-VALIDATION");
        var versionId = exam.InitialVersionId!;

        var longFileName = await controller.CreateAsset(versionId,
            new CreateAssetRequest($"/tmp/{new string('a', 257)}.png", "image/png", Convert.ToBase64String([1, 2, 3])), CancellationToken.None);
        var longFileNameError = Assert.IsType<BadRequestObjectResult>(longFileName.Result);
        Assert.Equal("El nombre del archivo no puede superar los 256 caracteres.", longFileNameError.Value);

        var unsupported = await controller.CreateAsset(versionId,
            new CreateAssetRequest("vector.svg", "image/svg+xml", Convert.ToBase64String([1, 2, 3])), CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ObjectResult>(unsupported.Result).StatusCode);

        var oversizedBytes = new byte[2 * 1024 * 1024 + 1];
        var oversized = await controller.CreateAsset(versionId,
            new CreateAssetRequest("large.png", "image/png", Convert.ToBase64String(oversizedBytes)), CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ObjectResult>(oversized.Result).StatusCode);
        Assert.Empty(await dbContext.ExamAssets.ToListAsync());
    }

    [Fact]
    public async Task Version_list_exposes_per_version_readiness()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var create = await controller.Create(
            new CreateExamRequest("EXA-2026-04", "Biologia", null, ["secundaria-1"], null, null),
            CancellationToken.None);
        var createdSummary = Assert.IsType<ExamSummaryDto>(
            Assert.IsType<CreatedAtActionResult>(create.Result).Value);
        var examId = createdSummary.Id;
        var versionId = createdSummary.InitialVersionId!;

        var empty = await ListVersionsAsync(controller, examId);
        Assert.Equal(0, empty.BlockCount);
        Assert.False(empty.CanPublish);
        Assert.Equal("no_blocks", empty.PublishBlockedReason);

        var upsert = await controller.UpsertBlock(
            versionId,
            0,
            new UpsertBlockRequest(0, BlockType.TrueFalse, "P1", null, Json("""{"question":"La célula es la unidad de la vida","correct":true}"""), null),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(upsert.Result);

        var ready = await ListVersionsAsync(controller, examId);
        Assert.Equal(1, ready.BlockCount);
        Assert.True(ready.CanPublish);
        Assert.Null(ready.PublishBlockedReason);
    }

    private static async Task<IReadOnlyList<ExamSummaryDto>> ListExamsAsync(ExamsController controller)
    {
        var result = await controller.List(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IReadOnlyList<ExamSummaryDto>>(ok.Value);
    }

    private static async Task<ExamSummaryDto> CreateExamAsync(ExamsController controller, string code)
    {
        var result = await controller.Create(
            new CreateExamRequest(code, "Matemática", null, ["secundaria-1"], "Matemática", "Números"),
            CancellationToken.None);
        return Assert.IsType<ExamSummaryDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
    }

    private static async Task<ExamVersionDto> ListVersionsAsync(ExamsController controller, string examId)
    {
        var result = await controller.ListVersions(examId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var versions = Assert.IsAssignableFrom<IReadOnlyList<ExamVersionDto>>(ok.Value);
        return Assert.Single(versions);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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
