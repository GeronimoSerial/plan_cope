using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Activation;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class ActivationAdminControllerTests
{
    private const string CueA = "180000100";
    private const string CueB = "180000200";

    // The controller never verifies stored hashes, so a syntactically plausible Argon2id hash is
    // enough for fixtures. Runs no Argon2 work in tests.
    private const string FakeKeyHash = "argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$aGFzaA==";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    [Fact]
    public async Task RevokeKey_NeverTouchesEnrolledNodes()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var (keyId, nodeId) = SeedKeyWithNode(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.RevokeKey(keyId, new RevokeActivationKeyRequest("Baja de prueba"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        using var verify = CreateDbContext(options);
        var key = await verify.ActivationKeys.SingleAsync();
        Assert.Equal(keyId, key.Id);
        Assert.NotNull(key.RevokedAt);

        var node = await verify.RegisteredNodes.SingleAsync();
        Assert.Equal(nodeId, node.Id);
        Assert.Equal(keyId, node.ActivationKeyId);
        Assert.Null(node.RevokedAt);
    }

    [Fact]
    public async Task RevokeNode_NeverTouchesTheEnrollingKey()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var (keyId, nodeId) = SeedKeyWithNode(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.RevokeNode(nodeId, new RevokeNodeRequest("Baja de equipo"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        using var verify = CreateDbContext(options);
        var key = await verify.ActivationKeys.SingleAsync();
        Assert.Equal(keyId, key.Id);
        Assert.Null(key.RevokedAt);
        Assert.Equal(1, key.ActivationCount);
        Assert.Equal(5, key.MaxActivations);

        var node = await verify.RegisteredNodes.SingleAsync();
        Assert.Equal(nodeId, node.Id);
        Assert.NotNull(node.RevokedAt);
    }

    [Fact]
    public async Task SchoolUser_CannotListOrRevokeKeyIssuedAtAnotherSchool()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var adminId = NewId();
        var keyId = SeedKey(options, adminId, CueA);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueB), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListKeys(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        Assert.Empty(Assert.IsType<List<ActivationKeySummaryDto>>(ok.Value));

        var revoke = await controller.RevokeKey(keyId, new RevokeActivationKeyRequest("Intento fuera de alcance"), CancellationToken.None);
        Assert.IsType<ForbidResult>(revoke);
    }

    [Fact]
    public async Task ProvinceUser_CanListAndRevokeAnyKey()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var keyId = SeedKey(options, NewId(), CueA);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, ProvincePrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListKeys(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        var summary = Assert.Single(Assert.IsType<List<ActivationKeySummaryDto>>(ok.Value));
        Assert.Equal(keyId, summary.Id);

        var revoke = await controller.RevokeKey(keyId, new RevokeActivationKeyRequest("Razón de baja"), CancellationToken.None);
        Assert.IsType<NoContentResult>(revoke);
    }

    [Fact]
    public async Task AdminWithNoCueAssignment_CanIssueAKey()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var result = await controller.IssueKey(
            new IssueActivationKeyRequest(MaxActivations: 5, ExpiresAt: null, Note: null, HolderName: "  Ana Pérez  "),
            CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        using var verify = CreateDbContext(options);
        Assert.Equal("Ana Pérez", Assert.Single(verify.ActivationKeys).HolderName);
    }

    [Fact]
    public async Task IssueKey_RequiresTrimmedHolderNameWithinLimit()
    {
        using var scope = CreateAuthorizationScope();
        using var dbContext = CreateDbContext(CreateOptions());
        var controller = CreateController(dbContext, AdminPrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var missing = await controller.IssueKey(new IssueActivationKeyRequest(1, null, null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(missing.Result);
        var tooLong = await controller.IssueKey(new IssueActivationKeyRequest(1, null, null, new string('a', 201)), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(tooLong.Result);
    }

    [Fact]
    public async Task AdminWithNoCueAssignment_CanListAndRevokeAnyKey()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var keyId = SeedKey(options, NewId(), CueA);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListKeys(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        var summary = Assert.Single(Assert.IsType<List<ActivationKeySummaryDto>>(ok.Value));
        Assert.Equal(keyId, summary.Id);

        var revoke = await controller.RevokeKey(keyId, new RevokeActivationKeyRequest("Razón de baja"), CancellationToken.None);
        Assert.IsType<NoContentResult>(revoke);
    }

    [Fact]
    public async Task AdminWithNoCueAssignment_CanListAndRevokeAnyNode()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var (_, nodeId) = SeedKeyWithNode(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, AdminPrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListNodes(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        var summary = Assert.Single(Assert.IsType<List<RegisteredNodeSummaryDto>>(ok.Value));
        Assert.Equal(nodeId, summary.Id);

        var revoke = await controller.RevokeNode(nodeId, new RevokeNodeRequest("Baja de equipo"), CancellationToken.None);
        Assert.IsType<NoContentResult>(revoke);
    }

    [Fact]
    public async Task SchoolUser_CannotListOrRevokeNodeAtAnotherCue()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var (_, nodeId) = SeedKeyWithNode(options);

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueB), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListNodes(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        Assert.Empty(Assert.IsType<List<RegisteredNodeSummaryDto>>(ok.Value));

        var revoke = await controller.RevokeNode(nodeId, new RevokeNodeRequest("Intento fuera de alcance"), CancellationToken.None);
        Assert.IsType<ForbidResult>(revoke);

        using var verify = CreateDbContext(options);
        var node = await verify.RegisteredNodes.SingleAsync();
        Assert.Equal(nodeId, node.Id);
        Assert.Null(node.RevokedAt);
    }

    [Fact]
    public async Task SchoolKeyIssuer_CanListAndRevokeDevicesForTheirKeyOnly()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var ownKeyId = SeedKey(options, NewId(), CueA);
        var otherKeyId = SeedKey(options, NewId(), CueB);
        var ownNodeId = SeedNodeForKey(options, ownKeyId, "NODE-OWN");
        var otherNodeId = SeedNodeForKey(options, otherKeyId, "NODE-OTHER");

        using var dbContext = CreateDbContext(options);
        var controller = CreateController(dbContext, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());

        var list = await controller.ListKeyNodes(ownKeyId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list.Result);
        var listedNode = Assert.Single(Assert.IsType<List<ActivationKeyNodeDto>>(ok.Value));
        Assert.Equal(ownNodeId, listedNode.Id);
        Assert.Equal("1.0.0", listedNode.AppVersion);

        var revokeOwn = await controller.RevokeNode(ownNodeId, new RevokeNodeRequest("Prueba"), CancellationToken.None);
        Assert.IsType<NoContentResult>(revokeOwn);
        var revokeOther = await controller.RevokeNode(otherNodeId, new RevokeNodeRequest("Fuera de alcance"), CancellationToken.None);
        Assert.IsType<ForbidResult>(revokeOther);
        var listOther = await controller.ListKeyNodes(otherKeyId, CancellationToken.None);
        Assert.IsType<ForbidResult>(listOther.Result);
    }

    [Fact]
    public async Task NodeWithoutActivationKey_CanOnlyBeRevokedByAdmin()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var nodeId = SeedUnkeyedNode(options);

        using (var schoolDb = CreateDbContext(options))
        {
            var schoolController = CreateController(schoolDb, SchoolPrincipal(CueA), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());
            var result = await schoolController.RevokeNode(nodeId, new RevokeNodeRequest("Prueba"), CancellationToken.None);
            Assert.IsType<ForbidResult>(result);
        }

        using (var adminDb = CreateDbContext(options))
        {
            var adminController = CreateController(adminDb, AdminPrincipal(), scope.ServiceProvider.GetRequiredService<IAuthorizationService>());
            var result = await adminController.RevokeNode(nodeId, new RevokeNodeRequest("Prueba"), CancellationToken.None);
            Assert.IsType<NoContentResult>(result);
        }
    }

    [Fact]
    public async Task SchoolUserWithMultipleAssignedCues_CanIssueKeysWithoutGeneratingCueNoteTokens()
    {
        using var scope = CreateAuthorizationScope();
        var options = CreateOptions();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        // A school-scope caller with exactly one CUE resolves it automatically without a note.
        using (var singleDbContext = CreateDbContext(options))
        {
            var singleCueController = CreateController(singleDbContext, SchoolPrincipal(CueA), authorizationService);
            var single = await singleCueController.IssueKey(
                new IssueActivationKeyRequest(MaxActivations: 5, ExpiresAt: null, Note: null, HolderName: "Ana"),
                CancellationToken.None);
            Assert.IsType<ObjectResult>(single.Result);
        }

        using (var multiDbContext = CreateDbContext(options))
        {
            var multiCueController = CreateController(multiDbContext, SchoolPrincipal(CueA, CueB), authorizationService);

            // Holder-based issuance does not require a CUE token for multi-school users.
            var unnamed = await multiCueController.IssueKey(
                new IssueActivationKeyRequest(MaxActivations: 5, ExpiresAt: null, Note: null, HolderName: "Bruno"),
                CancellationToken.None);
            Assert.IsType<ObjectResult>(unnamed.Result);

            // Two CUEs with a correct issued-for-cue note: the key persists with that token.
            var named = await multiCueController.IssueKey(
                new IssueActivationKeyRequest(MaxActivations: 5, ExpiresAt: null, Note: $"issued-for-cue:{CueB}", HolderName: "Carla"),
                CancellationToken.None);
            Assert.IsType<ObjectResult>(named.Result);
        }

        using var verify = CreateDbContext(options);
        var keys = await verify.ActivationKeys.AsNoTracking().ToListAsync();
        Assert.Equal(3, keys.Count);
        Assert.Contains(keys, static key => key.Note is null);
        Assert.Contains(keys, static key => key.Note?.Contains($"issued-for-cue:{CueB}", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ActivationKeySummaryDto_NeverCarriesKeyMaterial()
    {
        var properties = typeof(ActivationKeySummaryDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain(properties, static name => name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, static name => name.Contains("Plaintext", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, static name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));

        // Closed property set: any future addition of key material must fail this test.
        var allowed = new[]
        {
            "Id", "KeyPrefix", "IssuedAt", "ExpiresAt", "MaxActivations",
            "ActivationCount", "RevokedAt", "RevokedReason", "Note", "HolderName"
        };
        Assert.Equal(allowed.OrderBy(static name => name), properties.OrderBy(static name => name));
    }

    [Fact]
    public void Revocation_IsTwoSeparateRoutesNotASharedHandler()
    {
        var revokeKey = typeof(ActivationAdminController).GetMethod(
            nameof(ActivationAdminController.RevokeKey), BindingFlags.Public | BindingFlags.Instance);
        var revokeNode = typeof(ActivationAdminController).GetMethod(
            nameof(ActivationAdminController.RevokeNode), BindingFlags.Public | BindingFlags.Instance);

        var keyRoute = revokeKey!.GetCustomAttribute<HttpPostAttribute>();
        var nodeRoute = revokeNode!.GetCustomAttribute<HttpPostAttribute>();

        Assert.NotNull(keyRoute);
        Assert.NotNull(nodeRoute);
        Assert.Contains("keys", keyRoute!.Template, StringComparison.Ordinal);
        Assert.Contains("nodes", nodeRoute!.Template, StringComparison.Ordinal);
        Assert.NotEqual(keyRoute.Template, nodeRoute.Template);
    }

    private static string SeedKey(DbContextOptions<PlanCopeDbContext> options, string issuerId, string issuerCue)
    {
        using var dbContext = CreateDbContext(options);
        dbContext.UserSchools.Add(new UserSchoolAssignment(issuerId, issuerCue, DateTimeOffset.UtcNow));
        var key = CreateKey(issuerId);
        dbContext.ActivationKeys.Add(key);
        dbContext.SaveChanges();
        return key.Id;
    }

    private static (string KeyId, string NodeId) SeedKeyWithNode(DbContextOptions<PlanCopeDbContext> options)
    {
        using var dbContext = CreateDbContext(options);
        var key = CreateKey(NewId());
        var node = CreateNode(key.Id, "NODE-TEST-1");
        dbContext.ActivationKeys.Add(key);
        dbContext.RegisteredNodes.Add(node);
        dbContext.SaveChanges();
        return (key.Id, node.Id);
    }

    private static string SeedNodeForKey(DbContextOptions<PlanCopeDbContext> options, string keyId, string nodeCode)
    {
        using var dbContext = CreateDbContext(options);
        var node = CreateNode(keyId, nodeCode);
        dbContext.RegisteredNodes.Add(node);
        dbContext.SaveChanges();
        return node.Id;
    }

    private static string SeedUnkeyedNode(DbContextOptions<PlanCopeDbContext> options)
    {
        using var dbContext = CreateDbContext(options);
        var node = CreateNode("unused-key", "NODE-UNKEYED") with { ActivationKeyId = null };
        dbContext.RegisteredNodes.Add(node);
        dbContext.SaveChanges();
        return node.Id;
    }

    private static ActivationKey CreateKey(string issuer)
    {
        var now = DateTimeOffset.UtcNow;
        return new ActivationKey(
            NewId(),
            FakeKeyHash,
            KeyPrefix: "PCOPE-01",
            issuer,
            now,
            ExpiresAt: null,
            MaxActivations: 5,
            ActivationCount: 1,
            RevokedAt: null,
            RevokedReason: null,
            ScopeCue: null,
            Note: "Clave de prueba");
    }

    private static RegisteredNode CreateNode(string activationKeyId, string nodeCode)
    {
        var now = DateTimeOffset.UtcNow;
        return new RegisteredNode(
            NewId(),
            SchoolId: null,
            nodeCode,
            DeviceName: "PC aula",
            Status: "Active",
            LastSeenAt: null,
            now,
            now,
            FingerprintHash: "fp-1",
            JsonDocument.Parse("""{"mac":"00:11:22:33:44:55"}"""),
            Cue: CueA,
            activationKeyId,
            EnrolledAt: now,
            RevokedAt: null,
            AppVersion: "1.0.0");
    }

    private static IServiceScope CreateAuthorizationScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddScoped<IAuthorizationHandler, RosterScopeAuthorizationHandler>();
        return services.BuildServiceProvider().CreateScope();
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

    private static ActivationAdminController CreateController(
        PlanCopeDbContext dbContext,
        ClaimsPrincipal principal,
        IAuthorizationService authorizationService)
    {
        return new ActivationAdminController(dbContext, new ActivationKeyService(), authorizationService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static ClaimsPrincipal ProvincePrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "RosterProvince"),
            new Claim("roster_scope", "province"));
    }

    private static ClaimsPrincipal SchoolPrincipal(params string[] cues)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, "Viewer"),
            new(ClaimTypes.NameIdentifier, NewId()),
            new("roster_scope", "school")
        };
        claims.AddRange(cues.Select(static cue => new Claim("roster_cue", cue)));
        return Principal(claims.ToArray());
    }

    private static ClaimsPrincipal AdminPrincipal()
    {
        return Principal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.NameIdentifier, NewId()),
            new Claim("roster_scope", "school"));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
