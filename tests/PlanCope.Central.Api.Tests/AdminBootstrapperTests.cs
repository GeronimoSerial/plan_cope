using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class AdminBootstrapperTests
{
    private const string Email = "bootstrap-admin@plancope.test";
    private const string FullName = "Bootstrap Test Admin";
    // Obviously-fake in-test password only — never a real secret (same idea as AuthControllerTests.Password).
    private const string Password = "BootstrapTest123!";

    [Fact]
    public async Task Bootstrap_WithValidConfig_CreatesAdminUserWithVerifiableHashAndRole()
    {
        using var dbContext = CreateDbContext();
        var configuration = CreateConfiguration();

        await AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger);

        var users = await dbContext.Users.ToListAsync();
        var user = Assert.Single(users);
        Assert.Equal(Email, user.Email);
        Assert.Equal("Active", user.Status);
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash));

        var adminRole = await dbContext.Roles.SingleAsync(x => x.Code == "Admin");
        var assignment = await dbContext.UserRoles
            .Where(x => x.UserId == user.Id && x.RoleId == adminRole.Id)
            .ToListAsync();
        Assert.Single(assignment);
    }

    [Fact]
    public async Task Bootstrap_SecondRun_IsNoOp_KeepsHashAndDoesNotDuplicate()
    {
        using var dbContext = CreateDbContext();
        var configuration = CreateConfiguration();

        await AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger);
        var firstHash = (await dbContext.Users.SingleAsync(x => x.Email == Email)).PasswordHash;

        await AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger);

        Assert.Single(await dbContext.Users.ToListAsync());
        var user = await dbContext.Users.SingleAsync(x => x.Email == Email);
        Assert.Equal(firstHash, user.PasswordHash);
        Assert.Single(await dbContext.Roles.Where(x => x.Code == "Admin").ToListAsync());

        var adminRole = await dbContext.Roles.SingleAsync(x => x.Code == "Admin");
        Assert.Single(await dbContext.UserRoles
            .Where(x => x.UserId == user.Id && x.RoleId == adminRole.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_NoVariablesConfigured_ReturnsNormallyAndCreatesNoUser()
    {
        using var dbContext = CreateDbContext();
        var configuration = CreateConfiguration(email: null, password: null, fullName: null);

        await AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger);

        Assert.Empty(await dbContext.Users.ToListAsync());
        Assert.Empty(await dbContext.Roles.ToListAsync());
        Assert.Empty(await dbContext.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_PartialConfig_ThrowsAndCreatesNoUser()
    {
        using var dbContext = CreateDbContext();
        // Only one of the three variables configured — a half-configured
        // bootstrap must still crash loudly.
        var configuration = CreateConfiguration(password: null, fullName: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger));

        Assert.Empty(await dbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_MissingEmail_ThrowsAndCreatesNoUser()
    {
        using var dbContext = CreateDbContext();
        var configuration = CreateConfiguration(email: "   ");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger));

        Assert.Empty(await dbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_MissingPassword_ThrowsAndCreatesNoUser()
    {
        using var dbContext = CreateDbContext();
        var configuration = CreateConfiguration(password: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger));

        Assert.Empty(await dbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_WeakPassword_ThrowsAndCreatesNoUser()
    {
        using var dbContext = CreateDbContext();
        var configuration = CreateConfiguration(password: "short1!");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger));

        Assert.Empty(await dbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_KnownDefaultPassword_IsRejectedAndCreatesNoUser()
    {
        using var dbContext = CreateDbContext();
        var configuration = CreateConfiguration(password: "Admin123!");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger));

        Assert.Empty(await dbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_ExistingUserWithoutAdminRole_HealsRoleAndKeepsPasswordHash()
    {
        using var dbContext = CreateDbContext();
        var existingHash = BCrypt.Net.BCrypt.HashPassword("PreExistingHash999!");
        var now = DateTimeOffset.UtcNow;
        var existingUser = new User(NewId(), Email, existingHash, FullName, "Active", null, null, now, now);
        dbContext.Users.Add(existingUser);
        await dbContext.SaveChangesAsync();

        var configuration = CreateConfiguration();

        await AdminBootstrapper.BootstrapAsync(dbContext, configuration, Logger);

        Assert.Single(await dbContext.Users.ToListAsync());
        var user = await dbContext.Users.SingleAsync(x => x.Email == Email);
        Assert.Equal(existingHash, user.PasswordHash);

        var adminRole = await dbContext.Roles.SingleAsync(x => x.Code == "Admin");
        Assert.Single(await dbContext.UserRoles
            .Where(x => x.UserId == user.Id && x.RoleId == adminRole.Id)
            .ToListAsync());
    }

    private static readonly ILogger Logger = NullLoggerFactory.Instance.CreateLogger("AdminBootstrapper");

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

    private static IConfiguration CreateConfiguration(
        string? email = Email,
        string? password = Password,
        string? fullName = FullName)
    {
        var values = new Dictionary<string, string?>();
        if (email is not null)
        {
            values["PLANCOPE_BOOTSTRAP_ADMIN_EMAIL"] = email;
        }

        if (password is not null)
        {
            values["PLANCOPE_BOOTSTRAP_ADMIN_PASSWORD"] = password;
        }

        if (fullName is not null)
        {
            values["PLANCOPE_BOOTSTRAP_ADMIN_FULL_NAME"] = fullName;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
