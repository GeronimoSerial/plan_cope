using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Services;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class DownloadControllerTests
{
    // Tradeoff (reported to the coordinator): the repo's test conventions are direct
    // controller construction (see AuthControllerTests / RostersAuthorizationTests) with no
    // WebApplicationFactory/TestServer anywhere. The actual 401 for the unauthenticated
    // request is produced by the JWT middleware in the real pipeline, which those tests never
    // exercise; the [Authorize] attribute check below is the proportionate proxy for it.

    [Fact]
    public void Controller_RequiresAuthentication()
    {
        var authorize = typeof(DownloadController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
    }

    [Fact]
    public async Task GetLatestInstaller_WithNotConfiguredStorage_Returns503()
    {
        var controller = new DownloadController(
            new NotConfiguredInstallerStorage(NullLogger<NotConfiguredInstallerStorage>.Instance));

        var result = await controller.GetLatestInstaller("stable", CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.NotNull(objectResult.Value);
        var body = JsonSerializer.Serialize(objectResult.Value);
        Assert.Contains("error", body, StringComparison.OrdinalIgnoreCase);
    }
}