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
            new CreateExamRequest("EXA-2026-01", "Matematica", null, "Secundario", "Matematica", "Numeros"),
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
            new CreateExamRequest("EXA-2026-02", "Lengua", null, null, null, null),
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
            new PublishExamVersionRequest(null, "6", null),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(publish.Result);

        var published = (await ListExamsAsync(controller)).Single(x => x.Id == examId);
        Assert.Equal(ExamPublicationStates.Published, published.PublicationState);
        Assert.Equal(versionId, published.PublishedVersionId);
        Assert.Equal(1, published.PublishedVersionNumber);
        Assert.NotNull(published.PublishedAt);
        Assert.NotNull(published.Targets);
        Assert.Contains(published.Targets!, target =>
            target.TargetType == PublicationTargetTypes.Grade && target.TargetId == "6");
        Assert.Equal(0, published.PulledByNodeCount);
    }

    [Fact]
    public async Task Publish_version_with_no_blocks_is_rejected()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var create = await controller.Create(
            new CreateExamRequest("EXA-2026-03", "Historia", null, null, null, null),
            CancellationToken.None);
        var versionId = Assert.IsType<ExamSummaryDto>(
            Assert.IsType<CreatedAtActionResult>(create.Result).Value).InitialVersionId!;

        var publish = await controller.PublishVersion(
            versionId,
            new PublishExamVersionRequest(null, "1A", null),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(publish.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        using var verify = new PlanCopeDbContext(options);
        Assert.Empty(verify.PublicationPackages);
        Assert.Equal("Draft", (await verify.ExamVersions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Version_list_exposes_per_version_readiness()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var create = await controller.Create(
            new CreateExamRequest("EXA-2026-04", "Biologia", null, null, null, null),
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
