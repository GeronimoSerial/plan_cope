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

public sealed class ExamPublishScoringPolicyGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    private static readonly IServiceProvider ControllerServices = CreateControllerServices();

    [Fact]
    public async Task Publish_MultipleChoiceWithoutPolicy_IsRejectedAndCreatesNoPackage()
    {
        var options = CreateOptions();
        using var dbContext = await SeedAsync(
            options,
            scoringPolicy: null,
            BlockType.MultipleChoice,
            """{"question":"Cuanto es 2 + 2?","options":["3","4"]}""");
        var controller = CreateController(dbContext);

        var result = await controller.PublishVersion(
            "ev-1",
            new PublishExamVersionRequest(null, "1A", null),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains("scoringPolicy", problem.Errors.Keys);

        using var verify = new PlanCopeDbContext(options);
        Assert.Empty(verify.PublicationPackages);
        Assert.Equal("Draft", (await verify.ExamVersions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Publish_MultipleChoiceWithValidPolicy_Succeeds()
    {
        var options = CreateOptions();
        using var dbContext = await SeedAsync(
            options,
            scoringPolicy: "AllOrNothing",
            BlockType.MultipleChoice,
            """{"question":"Cuanto es 2 + 2?","options":["3","4"]}""");
        var controller = CreateController(dbContext);

        var result = await controller.PublishVersion(
            "ev-1",
            new PublishExamVersionRequest(null, "1A", null),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PublishExamVersionResponse>(ok.Value);
        Assert.Equal("ev-1", response.ExamVersionId);

        using var verify = new PlanCopeDbContext(options);
        Assert.Single(verify.PublicationPackages);
        Assert.Equal("Published", (await verify.ExamVersions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Publish_TrueFalseOnlyWithoutPolicy_Succeeds()
    {
        var options = CreateOptions();
        using var dbContext = await SeedAsync(
            options,
            scoringPolicy: null,
            BlockType.TrueFalse,
            """{"question":"La Tierra es redonda."}""");
        var controller = CreateController(dbContext);

        var result = await controller.PublishVersion(
            "ev-1",
            new PublishExamVersionRequest(null, "1A", null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);

        using var verify = new PlanCopeDbContext(options);
        Assert.Single(verify.PublicationPackages);
        Assert.Equal("Published", (await verify.ExamVersions.SingleAsync()).Status);
    }

    private static async Task<PlanCopeDbContext> SeedAsync(
        DbContextOptions<PlanCopeDbContext> options,
        string? scoringPolicy,
        BlockType blockType,
        string configJson)
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
            "Draft",
            null,
            null,
            null,
            null,
            null,
            null,
            Now,
            Now,
            scoringPolicy);
        var block = new ExamBlock(
            "blk-1",
            "ev-1",
            0,
            blockType,
            "Pregunta 1",
            null,
            JsonDocument.Parse(configJson),
            null,
            Now,
            Now);

        dbContext.Exams.Add(exam);
        dbContext.ExamVersions.Add(version);
        dbContext.ExamBlocks.Add(block);
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
