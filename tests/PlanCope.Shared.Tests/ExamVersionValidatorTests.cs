using FluentValidation.TestHelper;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;
using Xunit;

namespace PlanCope.Shared.Tests;

public sealed class ExamVersionValidatorTests
{
    private readonly ExamVersionValidator _validator = new();

    private static ExamVersion ValidVersion() => new(
        Id: "ev-1",
        ExamId: "exam-1",
        VersionNumber: 1,
        SchemaVersion: 1,
        Status: "Draft",
        Metadata: null,
        CreatedBy: null,
        ReviewedBy: null,
        ApprovedBy: null,
        PublishedBy: null,
        PublishedAt: null,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        ScoringPolicy: null);

    [Fact]
    public void Fully_valid_exam_version_passes_with_no_errors()
    {
        var result = _validator.TestValidate(ValidVersion());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_Id_fails()
    {
        var result = _validator.TestValidate(ValidVersion() with { Id = "" });

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Non_empty_Id_passes()
    {
        var result = _validator.TestValidate(ValidVersion() with { Id = "ev-2" });

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Empty_ExamId_fails()
    {
        var result = _validator.TestValidate(ValidVersion() with { ExamId = "" });

        result.ShouldHaveValidationErrorFor(x => x.ExamId);
    }

    [Fact]
    public void Non_empty_ExamId_passes()
    {
        var result = _validator.TestValidate(ValidVersion() with { ExamId = "exam-7" });

        result.ShouldNotHaveValidationErrorFor(x => x.ExamId);
    }

    [Fact]
    public void Zero_VersionNumber_fails()
    {
        var result = _validator.TestValidate(ValidVersion() with { VersionNumber = 0 });

        result.ShouldHaveValidationErrorFor(x => x.VersionNumber);
    }

    [Fact]
    public void Negative_VersionNumber_fails()
    {
        var result = _validator.TestValidate(ValidVersion() with { VersionNumber = -1 });

        result.ShouldHaveValidationErrorFor(x => x.VersionNumber);
    }

    [Fact]
    public void VersionNumber_equal_to_one_passes()
    {
        var result = _validator.TestValidate(ValidVersion());

        result.ShouldNotHaveValidationErrorFor(x => x.VersionNumber);
    }

    [Fact]
    public void Zero_SchemaVersion_fails()
    {
        var result = _validator.TestValidate(ValidVersion() with { SchemaVersion = 0 });

        result.ShouldHaveValidationErrorFor(x => x.SchemaVersion);
    }

    [Fact]
    public void Negative_SchemaVersion_fails()
    {
        var result = _validator.TestValidate(ValidVersion() with { SchemaVersion = -1 });

        result.ShouldHaveValidationErrorFor(x => x.SchemaVersion);
    }

    [Fact]
    public void Positive_SchemaVersion_passes()
    {
        var result = _validator.TestValidate(ValidVersion() with { SchemaVersion = 2 });

        result.ShouldNotHaveValidationErrorFor(x => x.SchemaVersion);
    }

    [Fact]
    public void Empty_Status_fails()
    {
        var result = _validator.TestValidate(ValidVersion() with { Status = "" });

        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void Status_over_32_characters_fails()
    {
        var result = _validator.TestValidate(ValidVersion() with { Status = new string('s', 33) });

        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void Valid_Status_passes()
    {
        var result = _validator.TestValidate(ValidVersion() with { Status = "Published" });

        result.ShouldNotHaveValidationErrorFor(x => x.Status);
    }
}