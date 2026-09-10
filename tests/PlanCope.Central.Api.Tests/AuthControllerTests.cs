using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Contracts.Auth;
using PlanCope.Shared.Domain.Central;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Login_WithRosterProvinceRole_SetsProvinceScopeAndNoCues()
    {
        using var dbContext = CreateDbContext();
        var provinceRole = new Role(NewId(), "RosterProvince", "Alcance provincial de padrón", null, DateTimeOffset.UtcNow);
        var user = CreateUser("province@plancope.test", "Usuario de Planeamiento");
        dbContext.Roles.Add(provinceRole);
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, provinceRole.Id, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);

        var result = await controller.Login(new LoginRequest(user.Email, Password), CancellationToken.None);

        var profile = AssertProfile(result);
        Assert.Equal("province", profile.RosterScope);
        Assert.Empty(profile.RosterCues);
    }

    [Fact]
    public async Task Login_WithSchoolUserAndAssignedCue_SetsSchoolScopeWithCues()
    {
        using var dbContext = CreateDbContext();
        var viewerRole = new Role(NewId(), "Viewer", "Visor de escuela", null, DateTimeOffset.UtcNow);
        var user = CreateUser("school@plancope.test", "Usuario de escuela");
        dbContext.Roles.Add(viewerRole);
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, viewerRole.Id, DateTimeOffset.UtcNow));
        dbContext.UserSchools.Add(new UserSchoolAssignment(user.Id, "180000100", DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);

        var result = await controller.Login(new LoginRequest(user.Email, Password), CancellationToken.None);

        var profile = AssertProfile(result);
        Assert.Equal("school", profile.RosterScope);
        Assert.Contains("180000100", profile.RosterCues);
    }

    [Fact]
    public async Task Login_WithSchoolUserWithoutAssignedCues_SetsSchoolScopeAndEmptyCues()
    {
        using var dbContext = CreateDbContext();
        var viewerRole = new Role(NewId(), "Viewer", "Visor de escuela", null, DateTimeOffset.UtcNow);
        var user = CreateUser("empty-school@plancope.test", "Usuario de escuela sin CUEs");
        dbContext.Roles.Add(viewerRole);
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, viewerRole.Id, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);

        var result = await controller.Login(new LoginRequest(user.Email, Password), CancellationToken.None);

        var profile = AssertProfile(result);
        Assert.Equal("school", profile.RosterScope);
        Assert.Empty(profile.RosterCues);
    }

    private const string Password = "Secret123!";

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    private static PlanCopeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
        return new PlanCopeDbContext(options);
    }

    private static AuthController CreateController(PlanCopeDbContext dbContext)
    {
        var options = Options.Create(new AuthOptions
        {
            SigningKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        });
        var tokenService = new TokenService(options);
        return new AuthController(dbContext, tokenService);
    }

    private static User CreateUser(string email, string fullName)
    {
        var now = DateTimeOffset.UtcNow;
        return new User(
            NewId(),
            email,
            BCrypt.Net.BCrypt.HashPassword(Password),
            fullName,
            "Active",
            null,
            null,
            now,
            now);
    }

    private static UserProfileDto AssertProfile(ActionResult<LoginResponse> result)
    {
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<LoginResponse>(okResult.Value).User;
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private sealed class JsonDocumentFriendlyModelCustomizer : ModelCustomizer
    {
        public JsonDocumentFriendlyModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(JsonDocument))
                    {
                        property.SetValueConverter(JsonDocumentConverter.Instance);
                    }
                }
            }
        }
    }

    private sealed class JsonDocumentConverter : ValueConverter<JsonDocument, string>
    {
        public static readonly JsonDocumentConverter Instance = new();

        private JsonDocumentConverter()
            : base(
                static document => document.RootElement.GetRawText(),
                static raw => JsonDocument.Parse(raw))
        {
        }
    }
}