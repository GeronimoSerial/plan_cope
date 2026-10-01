using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Exams;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

/// <summary>
/// Pins the "editable, versioned exams" contract: a published version is immutable, editing it
/// means creating a new draft version that is a deep copy of an existing version, and publishing
/// the copy supersedes the previous published version without breaking sync delivery.
/// </summary>
public sealed class ExamVersioningEditTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    private static readonly IServiceProvider ControllerServices = CreateControllerServices();

    [Fact]
    public async Task Publish_multiple_choice_question_without_policy_succeeds()
    {
        using var dbContext = new PlanCopeDbContext(CreateOptions());
        var controller = CreateController(dbContext);
        var exam = await CreateExamAsync(controller, "EXA-POLICY-01");
        var versionId = exam.InitialVersionId!;
        var config = Json("""{"question":"Elegí las respuestas correctas","multiple":true,"options":[{"value":"a","label":"A"},{"value":"b","label":"B"}]}""");

        var blockResult = await controller.UpsertBlock(versionId, 0,
            new UpsertBlockRequest(0, BlockType.MultipleChoice, "Pregunta", null, config, null), CancellationToken.None);
        Assert.IsType<OkObjectResult>(blockResult.Result);

        var publishResult = await controller.PublishVersion(versionId,
            new PublishExamVersionRequest(null, null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(publishResult.Result);
    }

    [Fact]
    public async Task Create_version_defaults_to_a_deep_copy_of_the_latest_version()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);
        var now = DateTimeOffset.UtcNow;

        var exam = await CreateExamAsync(controller, "EXA-VER-01");
        var versionId = exam.InitialVersionId!;
        var assetId = await AddImageAssetAsync(controller, versionId);

        await SeedQuestionDocumentAsync(dbContext, controller, versionId);

        var sourceBlocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == versionId)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync();
        var sourceAnswerKeys = await dbContext.AnswerKeys
            .Where(x => sourceBlocks.Select(block => block.Id).Contains(x.ExamBlockId))
            .ToListAsync();
        var mcq = sourceBlocks.Single(block => block.BlockType == BlockType.MultipleChoice);

        // A separate block-option row and an uploaded asset must travel with the copy.
        dbContext.ExamBlockOptions.Add(new ExamBlockOption("opt-1", mcq.Id, "4", "Cuatro", 0, JsonDocument.Parse("""{"feedback":"ok"}"""), now, now));
        await dbContext.SaveChangesAsync();

        // force: true because the source version is still a draft (the default source would
        // otherwise be rejected with 409 draft_exists under the new rule).
        var result = await controller.CreateVersion(exam.Id, new CreateExamVersionRequest(Force: true), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ExamVersionDto>(created.Value);
        Assert.Equal(2, dto.VersionNumber);
        Assert.Equal("Draft", dto.Status);
        Assert.Equal(1, dto.BasedOnVersionNumber);
        Assert.Equal(2, dto.BlockCount);
        Assert.True(dto.CanPublish);
        Assert.Null(dto.PublishedAt);

        var copiedBlocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == dto.Id)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync();

        Assert.Equal(2, copiedBlocks.Count);
        Assert.Empty(copiedBlocks.Select(block => block.Id).Intersect(sourceBlocks.Select(block => block.Id)));
        Assert.Equal(sourceBlocks.Select(block => block.OrderIndex), copiedBlocks.Select(block => block.OrderIndex));
        Assert.Equal(sourceBlocks.Select(block => block.BlockType), copiedBlocks.Select(block => block.BlockType));
        Assert.Equal(sourceBlocks.Select(block => block.Title), copiedBlocks.Select(block => block.Title));

        foreach (var (source, copy) in sourceBlocks.Zip(copiedBlocks))
        {
            Assert.True(JsonEquals(source.Config.RootElement, copy.Config.RootElement));

            Assert.Equal(source.Validation is null, copy.Validation is null);
            if (source.Validation is not null && copy.Validation is not null)
            {
                Assert.True(JsonEquals(source.Validation.RootElement, copy.Validation.RootElement));
            }
        }

        var copiedBlockIdMap = sourceBlocks.Zip(copiedBlocks).ToDictionary(pair => pair.First.Id, pair => pair.Second.Id);
        var copiedAnswerKeys = await dbContext.AnswerKeys
            .Where(x => copiedBlocks.Select(block => block.Id).Contains(x.ExamBlockId))
            .ToListAsync();
        Assert.Equal(sourceAnswerKeys.Count, copiedAnswerKeys.Count);
        foreach (var sourceKey in sourceAnswerKeys)
        {
            var copyKey = Assert.Single(copiedAnswerKeys, key => key.ExamBlockId == copiedBlockIdMap[sourceKey.ExamBlockId]);
            Assert.True(JsonEquals(sourceKey.CorrectAnswer.RootElement, copyKey.CorrectAnswer.RootElement));
            Assert.Equal(sourceKey.ScoreValue, copyKey.ScoreValue);
        }

        var copiedOptions = await dbContext.ExamBlockOptions.SingleAsync(x => x.ExamBlockId == copiedBlockIdMap[mcq.Id]);
        Assert.Equal(copiedBlockIdMap[mcq.Id], copiedOptions.ExamBlockId);
        Assert.Equal("4", copiedOptions.Value);
        Assert.Equal("Cuatro", copiedOptions.Label);

        var copiedAsset = await dbContext.ExamAssets.SingleAsync(x => x.ExamVersionId == dto.Id);
        Assert.NotEqual(assetId, copiedAsset.Id);
        Assert.Equal("diagrama.png", copiedAsset.FileName);
        Assert.Equal("image/png", copiedAsset.MimeType);
        Assert.StartsWith("base64:", copiedAsset.StoragePath);

        var sourceVersion = await dbContext.ExamVersions.SingleAsync(x => x.Id == versionId);
        var copiedVersion = await dbContext.ExamVersions.SingleAsync(x => x.Id == dto.Id);
        Assert.Equal(versionId, copiedVersion.SourceVersionId);
        Assert.True(JsonEquals(
            sourceVersion.Metadata!.RootElement,
            copiedVersion.Metadata!.RootElement));
    }

    [Fact]
    public async Task Create_version_with_explicit_source_copies_that_version_and_not_the_latest()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var exam = await CreateExamAsync(controller, "EXA-VER-02");
        var v1 = exam.InitialVersionId!;
        await controller.UpsertBlock(
            v1,
            0,
            new UpsertBlockRequest(0, BlockType.TrueFalse, "P1", null, Json("""{"question":"v1"}"""), null),
            CancellationToken.None);

        var emptyResult = await controller.CreateVersion(exam.Id, new CreateExamVersionRequest(Empty: true, Force: true), CancellationToken.None);
        var v2 = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(emptyResult.Result).Value);
        Assert.Equal(0, v2.BlockCount);

        var result = await controller.CreateVersion(
            exam.Id,
            new CreateExamVersionRequest(SourceVersionId: v1, Force: true),
            CancellationToken.None);

        var dto = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(3, dto.VersionNumber);
        Assert.Equal(1, dto.BasedOnVersionNumber);
        var copied = Assert.Single(dto.Blocks);
        Assert.Equal(BlockType.TrueFalse, copied.BlockType);
        Assert.Equal("v1", copied.Config.GetProperty("question").GetString());
    }

    [Fact]
    public async Task Create_version_with_empty_true_creates_an_empty_version_with_no_source()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var exam = await CreateExamAsync(controller, "EXA-VER-03");
        await controller.UpsertBlock(
            exam.InitialVersionId!,
            0,
            new UpsertBlockRequest(0, BlockType.TrueFalse, "P1", null, Json("""{"question":"v1"}"""), null),
            CancellationToken.None);

        var result = await controller.CreateVersion(exam.Id, new CreateExamVersionRequest(Empty: true, Force: true), CancellationToken.None);

        var dto = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(2, dto.VersionNumber);
        Assert.Empty(dto.Blocks);
        Assert.Empty(dto.AnswerKeys);
        Assert.Empty(dto.Assets);
        Assert.Equal(0, dto.BlockCount);
        Assert.False(dto.CanPublish);
        Assert.Equal("no_blocks", dto.PublishBlockedReason);
        Assert.Null(dto.BasedOnVersionNumber);

        var stored = await dbContext.ExamVersions.SingleAsync(x => x.Id == dto.Id);
        Assert.Null(stored.SourceVersionId);
    }

    [Fact]
    public async Task Create_version_unknown_source_is_404_and_foreign_source_is_400()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var examA = await CreateExamAsync(controller, "EXA-VER-04A");
        var examB = await CreateExamAsync(controller, "EXA-VER-04B");

        var unknown = await controller.CreateVersion(
            examA.Id,
            new CreateExamVersionRequest(SourceVersionId: "does-not-exist"),
            CancellationToken.None);
        Assert.IsType<NotFoundResult>(unknown.Result);

        var foreign = await controller.CreateVersion(
            examA.Id,
            new CreateExamVersionRequest(SourceVersionId: examB.InitialVersionId),
            CancellationToken.None);
        var badRequest = Assert.IsAssignableFrom<ObjectResult>(foreign.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task Update_exam_updates_metadata_and_returns_the_computed_summary()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var exam = await CreateExamAsync(controller, "EXA-EDIT-01");

        var result = await controller.UpdateExam(
            exam.Id,
            new UpdateExamRequest("EXA-EDIT-01", "Matemática Avanzada", "Nueva descripción", ["secundaria-3"], "Ciencias", "Álgebra"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<ExamSummaryDto>(ok.Value);
        Assert.Equal("Matemática Avanzada", summary.Title);
        Assert.Equal(new[] { "secundaria-3" }, summary.Courses);
        Assert.Equal("Ciencias", summary.Area);
        Assert.Equal("Álgebra", summary.Subject);
        Assert.Equal(1, summary.VersionCount);
        Assert.Equal(ExamPublicationStates.Draft, summary.PublicationState);

        var stored = await dbContext.Exams.SingleAsync(x => x.Id == exam.Id);
        Assert.Equal("Matemática Avanzada", stored.Title);
        Assert.Equal("Nueva descripción", stored.Description);
        Assert.True(stored.UpdatedAt >= stored.CreatedAt);
    }

    [Fact]
    public async Task Update_exam_omitting_code_keeps_it()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var exam = await CreateExamAsync(controller, "EXA-EDIT-02");

        var result = await controller.UpdateExam(
            exam.Id,
            new UpdateExamRequest(null, "Otro título", null, null, null, null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var stored = await dbContext.Exams.SingleAsync(x => x.Id == exam.Id);
        Assert.Equal("EXA-EDIT-02", stored.Code);
        Assert.Equal("Otro título", stored.Title);
    }

    [Fact]
    public async Task Update_exam_with_a_different_code_is_rejected_with_key_code()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var exam = await CreateExamAsync(controller, "EXA-EDIT-03");

        var result = await controller.UpdateExam(
            exam.Id,
            new UpdateExamRequest("EXA-EDIT-99", "Título", null, null, null, null),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains("code", problem.Errors.Keys);
    }

    [Fact]
    public async Task Update_exam_validation_failure_returns_400()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var exam = await CreateExamAsync(controller, "EXA-EDIT-04");

        var result = await controller.UpdateExam(
            exam.Id,
            new UpdateExamRequest("EXA-EDIT-04", "", null, null, null, null),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task Update_exam_unknown_exam_returns_404()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var result = await controller.UpdateExam(
            "missing-exam",
            new UpdateExamRequest(null, "Título", null, null, null, null),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void Update_and_create_share_the_same_authorization_requirement()
    {
        var controllerType = typeof(ExamsController);
        Assert.NotEmpty(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));

        var create = controllerType.GetMethod(nameof(ExamsController.Create))!;
        var update = controllerType.GetMethod(nameof(ExamsController.UpdateExam))!;

        Assert.Empty(create.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.Empty(update.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.Equal("ExamAuthor", Assert.Single(update.GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);
        Assert.Equal("ExamAuthor", Assert.Single(create.GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);
    }

    [Fact]
    public async Task Publishing_a_newer_version_supersedes_the_previous_one()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var exam = await CreateExamAsync(controller, "EXA-SUPER-01");
        var v1 = exam.InitialVersionId!;
        await controller.UpsertBlock(
            v1,
            0,
            new UpsertBlockRequest(0, BlockType.TrueFalse, "P1", null, Json("""{"question":"v1"}"""), null),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>((await controller.PublishVersion(v1, new PublishExamVersionRequest(null, null), CancellationToken.None)).Result);

        var copyResult = await controller.CreateVersion(exam.Id, new CreateExamVersionRequest(), CancellationToken.None);
        var v2 = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(copyResult.Result).Value);
        Assert.Equal(1, v2.BasedOnVersionNumber);
        Assert.Single(v2.Blocks);
        Assert.IsType<OkObjectResult>((await controller.PublishVersion(v2.Id, new PublishExamVersionRequest(null, null), CancellationToken.None)).Result);

        var versions = await ListVersionsAsync(controller, exam.Id);
        var current = versions.Single(version => version.Id == v2.Id);
        var superseded = versions.Single(version => version.Id == v1);

        Assert.True(current.IsCurrent);
        Assert.Null(current.SupersededAt);
        Assert.NotNull(current.PublishedAt);

        Assert.False(superseded.IsCurrent);
        Assert.NotNull(superseded.PublishedAt);
        Assert.Equal(current.PublishedAt, superseded.SupersededAt);

        var detail = await GetVersionAsync(controller, v1);
        Assert.False(detail.IsCurrent);
        Assert.Equal(current.PublishedAt, detail.SupersededAt);

        var summary = (await ListExamsAsync(controller)).Single(item => item.Id == exam.Id);
        Assert.Equal(ExamPublicationStates.Published, summary.PublicationState);
        Assert.Equal(v2.Id, summary.PublishedVersionId);
        Assert.Equal(2, summary.PublishedVersionNumber);
        Assert.Equal(current.PublishedAt, summary.PublishedAt);
        Assert.Equal(0, summary.PulledByNodeCount);

        // The old package row survives so a node that never pulled v1 can still receive it.
        var packages = await dbContext.PublicationPackages.ToListAsync();
        Assert.Equal(2, packages.Count);
        Assert.All(packages, package => Assert.Equal("Published", package.Status));
        Assert.Contains(packages, package => package.ExamVersionId == v1);
        Assert.Contains(packages, package => package.ExamVersionId == v2.Id);
    }

    [Fact]
    public async Task Published_version_is_immutable_but_its_copy_is_editable()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var controller = CreateController(dbContext);

        var exam = await CreateExamAsync(controller, "EXA-IMMUT-01");
        var v1 = exam.InitialVersionId!;
        await AddImageAssetAsync(controller, v1);
        await SeedQuestionDocumentAsync(dbContext, controller, v1);
        Assert.IsType<OkObjectResult>((await controller.PublishVersion(v1, new PublishExamVersionRequest(null, null), CancellationToken.None)).Result);

        var documentEdit = await controller.ReplaceDocument(
            v1,
            new ReplaceExamDocumentRequest(null, [new DocumentBlockDto(0, BlockType.MultipleChoice, "T", null, Json("""{"question":"x","multiple":true,"scoringPolicy":"AllOrNothing","options":["a","b"]}"""), null, null, null)]),
            CancellationToken.None);
        var documentConflict = Assert.IsAssignableFrom<ObjectResult>(documentEdit.Result);
        Assert.Equal(StatusCodes.Status409Conflict, documentConflict.StatusCode);

        var blockEdit = await controller.UpsertBlock(
            v1,
            0,
            new UpsertBlockRequest(0, BlockType.MultipleChoice, "T", null, Json("""{"question":"x","options":["a","b"]}"""), null),
            CancellationToken.None);
        var blockConflict = Assert.IsAssignableFrom<ObjectResult>(blockEdit.Result);
        Assert.Equal(StatusCodes.Status409Conflict, blockConflict.StatusCode);

        var copyResult = await controller.CreateVersion(exam.Id, new CreateExamVersionRequest(), CancellationToken.None);
        var v2 = Assert.IsType<ExamVersionDto>(Assert.IsType<CreatedAtActionResult>(copyResult.Result).Value);

        var editable = await controller.UpsertBlock(
            v2.Id,
            5,
            new UpsertBlockRequest(5, BlockType.TrueFalse, "Nuevo", null, Json("""{"question":"editado"}"""), null),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(editable.Result);
    }

    [Fact]
    public async Task Sync_pull_delivers_the_newer_package_after_it_supersedes_the_previous_one()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        var t1 = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);

        dbContext.Exams.Add(new Exam("ex-sync", "EXA-SYNC-01", "Exam", null, [], null, "Matematica", "Published", null, t1, t2));
        dbContext.ExamVersions.Add(new ExamVersion("ev-1", "ex-sync", 1, 1, "Published", null, null, null, null, null, t1, t1, t1));
        dbContext.ExamVersions.Add(new ExamVersion("ev-2", "ex-sync", 2, 1, "Published", null, null, null, null, null, t2, t2, t2, SourceVersionId: "ev-1"));
        const string referencedAssetId = "asset-in-question";
        dbContext.ExamBlocks.Add(new ExamBlock("block-with-image", "ev-2", 0, BlockType.TrueFalse, "Q", null,
            JsonDocument.Parse("{\"question\":\"Question\",\"imageAssetId\":\"asset-in-question\"}"), null, t2, t2));
        dbContext.ExamAssets.Add(new ExamAsset(referencedAssetId, "ev-2", "question.png", "image/png", 3,
            "checksum", "base64:AQID", t2));
        dbContext.PublicationPackages.Add(new PublicationPackage("pkg-1", "ev-1", 1, "sha-1", JsonDocument.Parse("{}"), "Published", t1, t1));
        dbContext.PublicationPackages.Add(new PublicationPackage("pkg-2", "ev-2", 1, "sha-2", JsonDocument.Parse("{}"), "Published", t2, t2));
        dbContext.PublicationTargets.Add(new PublicationTarget("pt-1", "pkg-1", "grade", "6", t1, t1));
        dbContext.PublicationTargets.Add(new PublicationTarget("pt-2", "pkg-2", "grade", "6", t2, t2));
        await dbContext.SaveChangesAsync();

        var syncController = new SyncController(dbContext, new CentralStatsRollupService(dbContext));
        SyncTestPrincipals.BindNode(syncController, "node-A");
        var pull = await syncController.Pull("node-A", "0", 50, CancellationToken.None);
        var response = Assert.IsType<PullResponse>(Assert.IsType<OkObjectResult>(pull.Result).Value);

        Assert.Equal(["pkg-1", "pkg-2"], response.Items.Select(item => item.EntityId).ToList());
        var deliveredPackage = response.Items.Single(item => item.EntityId == "pkg-2").Payload;
        Assert.Equal(referencedAssetId, deliveredPackage.GetProperty("Assets")[0].GetProperty("Id").GetString());
        Assert.Equal(referencedAssetId,
            deliveredPackage.GetProperty("Blocks")[0].GetProperty("Config").GetProperty("imageAssetId").GetString());
        Assert.Equal("AQID", deliveredPackage.GetProperty("Assets")[0].GetProperty("ContentBase64").GetString());
        Assert.Equal(t2.UtcTicks.ToString(), response.NextCursor);

        var summary = (await ListExamsAsync(CreateController(dbContext))).Single(item => item.Id == "ex-sync");
        Assert.Equal(2, summary.PublishedVersionNumber);
        Assert.Equal("ev-2", summary.PublishedVersionId);
        Assert.Equal(1, summary.PulledByNodeCount);
    }

    private static async Task<ExamSummaryDto> CreateExamAsync(ExamsController controller, string code)
    {
        var result = await controller.Create(
            new CreateExamRequest(code, "Matemática", null, ["secundaria-1"], "Matemática", "Números"),
            CancellationToken.None);
        return Assert.IsType<ExamSummaryDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
    }

    private static async Task<string> AddImageAssetAsync(ExamsController controller, string versionId)
    {
        var result = await controller.CreateAsset(
            versionId,
            new CreateAssetRequest("diagrama.png", "image/png", Convert.ToBase64String([1, 2, 3, 4])),
            CancellationToken.None);
        return Assert.IsType<AssetDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value).Id;
    }

    // Builds a source version with the supported question types and answer keys. Uses UpsertBlock
    // because EF's InMemory provider rejects ReplaceDocument's
    // transaction; the behavior under test is version copying, not the write path.
    private static async Task SeedQuestionDocumentAsync(PlanCopeDbContext dbContext, ExamsController controller, string versionId)
    {
        var now = DateTimeOffset.UtcNow;
        await controller.UpsertBlock(versionId, 0, new UpsertBlockRequest(0, BlockType.MultipleChoice, "MCQ", null, Json("""{"question":"¿Cuánto es 2 + 2?","multiple":true,"options":[{"value":"3","label":"3"},{"value":"4","label":"4"}]}"""), Json("""{"required":true}""")), CancellationToken.None);
        await controller.UpsertBlock(versionId, 1, new UpsertBlockRequest(1, BlockType.TrueFalse, "Verdadero/Falso", null, Json("""{"question":"La Tierra es redonda"}"""), null), CancellationToken.None);

        var blocks = await dbContext.ExamBlocks
            .Where(x => x.ExamVersionId == versionId)
            .ToListAsync();
        var byType = blocks.ToDictionary(block => block.BlockType);
        dbContext.AnswerKeys.Add(new AnswerKey("ak-mcq", byType[BlockType.MultipleChoice].Id, JsonDocument.Parse("""{"value":"4"}"""), 1m, null, now, now));
        dbContext.AnswerKeys.Add(new AnswerKey("ak-tf", byType[BlockType.TrueFalse].Id, JsonDocument.Parse("""{"boolean":true}"""), 0.5m, null, now, now));

        var version = await dbContext.ExamVersions.SingleAsync(x => x.Id == versionId);
        dbContext.Entry(version).CurrentValues.SetValues(version with
        {
            Metadata = JsonDocument.Parse("""{"author":"teacher01","revision":1}""")
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<ExamSummaryDto>> ListExamsAsync(ExamsController controller)
    {
        var result = await controller.List(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IReadOnlyList<ExamSummaryDto>>(ok.Value);
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

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Structural JSON comparison, whitespace-insensitive.</summary>
    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        return string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);
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
