using FluentValidation.TestHelper;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;
using Xunit;

namespace PlanCope.Shared.Tests;

public sealed class ExamValidatorTests
{
    private readonly ExamValidator _validator = new();

    private static Exam ValidExam() => new(
        Id: "exam-1",
        Code: "EXAM-2026-A1",
        Title: "Anatomy Midterm",
        Description: null,
        Level: null,
        Area: null,
        Subject: null,
        Status: "Draft",
        DeletedAt: null,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Fully_valid_exam_passes_with_no_errors()
    {
        var result = _validator.TestValidate(ValidExam());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_Id_fails()
    {
        var result = _validator.TestValidate(ValidExam() with { Id = "" });

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Non_empty_Id_passes()
    {
        var result = _validator.TestValidate(ValidExam() with { Id = "exam-42" });

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Empty_Code_fails()
    {
        var result = _validator.TestValidate(ValidExam() with { Code = "" });

        result.ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void Code_over_64_characters_fails()
    {
        var result = _validator.TestValidate(ValidExam() with { Code = new string('c', 65) });

        result.ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void Code_with_characters_invalid_per_ExamCode_fails()
    {
        var result = _validator.TestValidate(ValidExam() with { Code = "EXAM 2026" });

        result.ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void Valid_Code_passes()
    {
        var result = _validator.TestValidate(ValidExam() with { Code = "EXAM_2026-B2" });

        result.ShouldNotHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void Empty_Title_fails()
    {
        var result = _validator.TestValidate(ValidExam() with { Title = "" });

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Title_over_256_characters_fails()
    {
        var result = _validator.TestValidate(ValidExam() with { Title = new string('t', 257) });

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Valid_Title_passes()
    {
        var result = _validator.TestValidate(ValidExam() with { Title = "Pharmacology Final" });

        result.ShouldNotHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Empty_Status_fails()
    {
        var result = _validator.TestValidate(ValidExam() with { Status = "" });

        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void Status_over_32_characters_fails()
    {
        var result = _validator.TestValidate(ValidExam() with { Status = new string('s', 33) });

        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void Valid_Status_passes()
    {
        var result = _validator.TestValidate(ValidExam() with { Status = "Published" });

        result.ShouldNotHaveValidationErrorFor(x => x.Status);
    }
}