using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Shared.Domain.Central;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class ExamWriteAuthorizationTests
{
    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();
    [Fact]
    public void Every_exam_write_action_requires_the_exam_author_policy()
    {
        var writeActions = new[]
        {
            nameof(ExamsController.Create),
            nameof(ExamsController.CreateVersion),
            nameof(ExamsController.UpdateExam),
            nameof(ExamsController.UpsertBlock),
            nameof(ExamsController.CreateAsset),
            nameof(ExamsController.PublishVersion),
            nameof(ExamsController.ReplaceDocument)
        };

        foreach (var method in typeof(ExamsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                     .Where(method => writeActions.Contains(method.Name)))
        {
            var attribute = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal("ExamAuthor", attribute!.Policy);
        }

        Assert.Equal(2, typeof(ExamsController).GetMethods()
            .Count(method => method.Name == nameof(ExamsController.UpsertBlock)));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("ExamAuthor", true)]
    [InlineData("Grader", false)]
    [InlineData("Operator", false)]
    public async Task Exam_author_policy_allows_only_admins_and_exam_authors(string role, bool expected)
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddAuthorization(options => options.AddPolicy("ExamAuthor", policy => policy.RequireRole("Admin", "ExamAuthor")))
            .BuildServiceProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, "ExamAuthor");

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task Exam_author_policy_challenges_an_unauthenticated_principal()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddAuthorization(options => options.AddPolicy("ExamAuthor", policy => policy.RequireRole("Admin", "ExamAuthor")))
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, "ExamAuthor");

        Assert.False(result.Succeeded);
        Assert.NotNull(typeof(ExamsController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Publication_packages_have_a_unique_exam_version_index()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
        using var dbContext = new PlanCopeDbContext(options);
        var entity = dbContext.Model.FindEntityType(typeof(PublicationPackage));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(PublicationPackage.ExamVersionId)]));
    }
}
