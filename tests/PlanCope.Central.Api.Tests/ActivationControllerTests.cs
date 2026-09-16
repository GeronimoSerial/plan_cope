using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Activation;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class ActivationControllerTests
{
    private const string Cue = "180000100";
    private const string Issuer = "test-issuer";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task Redeem_MalformedKey_Returns400MalformedKey()
    {
        using var dbContext = CreateDbContext(CreateOptions());
        var controller = CreateController(dbContext);

        var result = await controller.Redeem(CreateRedeemRequest("not-a-key", "fp-1", Cue), CancellationToken.None);

        AssertFailure(result, StatusCodes.Status400BadRequest, ActivationRedeemFailureReason.MalformedKey);
    }

    [Fact]
    public async Task Redeem_UnknownKeyPrefix_Returns404KeyNotFound()
    {
        using var dbContext = CreateDbContext(CreateOptions());
        var controller = CreateController(dbContext);

        var plaintext = new ActivationKeyService().Generate().PlaintextKey;
        var result = await controller.Redeem(CreateRedeemRequest(plaintext, "fp-1", Cue), CancellationToken.None);

        AssertFailure(result, StatusCodes.Status404NotFound, ActivationRedeemFailureReason.KeyNotFound);
    }

    [Fact]
    public async Task Redeem_RevokedKey_Returns403KeyRevoked()
    {
        var options = CreateOptions();
        var (plaintext, _) = SeedKey(options, revokedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext);

        var result = await controller.Redeem(CreateRedeemRequest(plaintext, "fp-1", Cue), CancellationToken.None);

        AssertFailure(result, StatusCodes.Status403Forbidden, ActivationRedeemFailureReason.KeyRevoked);
    }

    [Fact]
    public async Task Redeem_ExpiredKey_Returns403KeyExpired()
    {
        var options = CreateOptions();
        var (plaintext, _) = SeedKey(options, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext);

        var result = await controller.Redeem(CreateRedeemRequest(plaintext, "fp-1", Cue), CancellationToken.None);

        AssertFailure(result, StatusCodes.Status403Forbidden, ActivationRedeemFailureReason.KeyExpired);
    }

    [Fact]
    public async Task Redeem_ExhaustedKey_Returns403ActivationLimitReached()
    {
        var options = CreateOptions();
        var (plaintext, _) = SeedKey(options, maxActivations: 1, activationCount: 1);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext);

        var result = await controller.Redeem(CreateRedeemRequest(plaintext, "fp-1", Cue), CancellationToken.None);

        AssertFailure(result, StatusCodes.Status403Forbidden, ActivationRedeemFailureReason.ActivationLimitReached);
    }

    [Fact]
    public async Task Redeem_ConsumesMaxActivations_ThenReturnsActivationLimitReached()
    {
        var options = CreateOptions();
        var (plaintext, _) = SeedKey(options, maxActivations: 1);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext);

        var first = await controller.Redeem(CreateRedeemRequest(plaintext, "fp-1", Cue), CancellationToken.None);
        AssertSuccess(first);

        var second = await controller.Redeem(CreateRedeemRequest(plaintext, "fp-1", Cue), CancellationToken.None);
        AssertFailure(second, StatusCodes.Status403Forbidden, ActivationRedeemFailureReason.ActivationLimitReached);
    }

    [Fact]
    public async Task Redeem_SameFingerprintTwice_ReturnsSameNodeWithoutConsumingAnotherActivation()
    {
        var options = CreateOptions();
        var (plaintext, _) = SeedKey(options, maxActivations: 5);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext);

        var firstNodeId = AssertSuccess(
            await controller.Redeem(CreateRedeemRequest(plaintext, "fp-same", Cue), CancellationToken.None)).Response!.NodeId;
        var secondNodeId = AssertSuccess(
            await controller.Redeem(CreateRedeemRequest(plaintext, "fp-same", Cue), CancellationToken.None)).Response!.NodeId;

        Assert.Equal(firstNodeId, secondNodeId);

        var key = await dbContext.ActivationKeys.SingleAsync();
        Assert.Equal(1, key.ActivationCount);
        Assert.Equal(1, await dbContext.RegisteredNodes.CountAsync());
    }

    [Fact]
    public async Task Redeem_DifferentFingerprintUnderSameKey_CreatesSecondNodeAndConsumesAnotherActivation()
    {
        var options = CreateOptions();
        var (plaintext, _) = SeedKey(options, maxActivations: 5);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext);

        var firstNodeId = AssertSuccess(
            await controller.Redeem(CreateRedeemRequest(plaintext, "fp-a", Cue), CancellationToken.None)).Response!.NodeId;
        var secondNodeId = AssertSuccess(
            await controller.Redeem(CreateRedeemRequest(plaintext, "fp-b", Cue), CancellationToken.None)).Response!.NodeId;

        Assert.NotEqual(firstNodeId, secondNodeId);

        var key = await dbContext.ActivationKeys.SingleAsync();
        Assert.Equal(2, key.ActivationCount);
        Assert.Equal(2, await dbContext.RegisteredNodes.CountAsync());
    }

    [Fact]
    public async Task Refresh_RotatesAndSpentTokenIsRejected()
    {
        var options = CreateOptions();
        var (plaintext, _) = SeedKey(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext);

        var redeem = await controller.Redeem(CreateRedeemRequest(plaintext, "fp-rot", Cue), CancellationToken.None);
        var refreshToken = AssertSuccess(redeem).Response!.RefreshToken;

        var refresh = await controller.Refresh(new ActivationRefreshRequest(refreshToken), CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(refresh.Result);
        var response = Assert.IsType<ActivationRefreshResponse>(okResult.Value);
        Assert.False(response.NodeRevoked);
        Assert.NotEqual(refreshToken, response.RefreshToken);

        var replay = await controller.Refresh(new ActivationRefreshRequest(refreshToken), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(replay.Result);
    }

    [Fact]
    public async Task Refresh_RevokedNode_Returns200WithNodeRevokedTrue()
    {
        var options = CreateOptions();
        var (plaintext, _) = SeedKey(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext);

        var redeem = await controller.Redeem(CreateRedeemRequest(plaintext, "fp-revoked-node", Cue), CancellationToken.None);
        var refreshToken = AssertSuccess(redeem).Response!.RefreshToken;

        var node = await dbContext.RegisteredNodes.SingleAsync();
        dbContext.Entry(node).CurrentValues.SetValues(node with { RevokedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();

        var refresh = await controller.Refresh(new ActivationRefreshRequest(refreshToken), CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(refresh.Result);
        var response = Assert.IsType<ActivationRefreshResponse>(okResult.Value);
        Assert.True(response.NodeRevoked);
    }

    private static (string PlaintextKey, string KeyId) SeedKey(
        DbContextOptions<PlanCopeDbContext> options,
        int maxActivations = 5,
        int activationCount = 0,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null)
    {
        using var dbContext = CreateDbContext(options);
        var generated = new ActivationKeyService().Generate();
        var key = new ActivationKey(
            NewId(),
            generated.KeyHash,
            generated.KeyPrefix,
            Issuer,
            DateTimeOffset.UtcNow,
            expiresAt,
            maxActivations,
            activationCount,
            revokedAt,
            RevokedReason: null,
            ScopeCue: null,
            Note: "Clave de prueba");
        dbContext.ActivationKeys.Add(key);
        dbContext.SaveChanges();
        return (generated.PlaintextKey, key.Id);
    }

    private static ActivationRedeemRequest CreateRedeemRequest(string activationKey, string fingerprintHash, string cue)
    {
        return new ActivationRedeemRequest(
            activationKey,
            fingerprintHash,
            JsonDocument.Parse("""{"mac":"00:11:22:33:44:55"}"""),
            cue,
            AppVersion: "1.0.0");
    }

    private static ActivationRedeemResult AssertSuccess(ActionResult<ActivationRedeemResult> result)
    {
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var redeemResult = Assert.IsType<ActivationRedeemResult>(okResult.Value);
        Assert.True(redeemResult.IsSuccess);
        Assert.NotNull(redeemResult.Response);
        return redeemResult;
    }

    private static void AssertFailure(
        ActionResult<ActivationRedeemResult> result,
        int statusCode,
        ActivationRedeemFailureReason reason)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(statusCode, objectResult.StatusCode);
        var redeemResult = Assert.IsType<ActivationRedeemResult>(objectResult.Value);
        Assert.False(redeemResult.IsSuccess);
        Assert.Equal(reason, redeemResult.Reason);
    }

    private static DbContextOptions<PlanCopeDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
    }

    private static PlanCopeDbContext CreateDbContext(DbContextOptions<PlanCopeDbContext> options)
    {
        return new PlanCopeDbContext(options);
    }

    private static ActivationController CreateController(PlanCopeDbContext dbContext)
    {
        var options = Options.Create(new AuthOptions
        {
            SigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        });
        var credentialService = new NodeCredentialService(dbContext, new TokenService(options));
        return new ActivationController(dbContext, new ActivationKeyService(), credentialService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}