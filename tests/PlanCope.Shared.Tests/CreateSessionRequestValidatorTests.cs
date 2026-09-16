using System.Text.Json;
using FluentValidation.TestHelper;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Domain.ValueObjects;
using PlanCope.Shared.Infrastructure.Validation;
using Xunit;

namespace PlanCope.Shared.Tests;

public sealed class CreateSessionRequestValidatorTests
{
    private const string NominalFieldsMessage =
        "schoolYear, rosterSnapshotId y rosterSectionId deben enviarse juntos para una sesión nominal.";

    private readonly CreateSessionRequestValidator _validator = new();

    private static CreateSessionRequest CreateRequest(
        string examVersionId = "version-1",
        string schoolCode = "180055400",
        string? classroomCode = null,
        string? commissionCode = null,
        string startedBy = "operator",
        int expectedStudentCount = 10,
        JsonElement? config = null,
        string? schoolYear = null,
        string? rosterSnapshotId = null,
        string? rosterSectionId = null)
        => new(
            examVersionId,
            schoolCode,
            classroomCode,
            commissionCode,
            startedBy,
            expectedStudentCount,
            config,
            schoolYear,
            rosterSnapshotId,
            rosterSectionId);

    [Fact]
    public void Empty_exam_version_id_fails()
    {
        var result = _validator.TestValidate(CreateRequest(examVersionId: ""));

        result.ShouldHaveValidationErrorFor(x => x.ExamVersionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1800554")]
    [InlineData("1800554000")]
    [InlineData("CUE180055400")]
    public void School_code_that_does_not_normalize_fails_with_the_exact_message(string schoolCode)
    {
        var result = _validator.TestValidate(CreateRequest(schoolCode: schoolCode));

        result
            .ShouldHaveValidationErrorFor(x => x.SchoolCode)
            .WithErrorMessage($"El CUE debe contener exactamente {CueCode.Length} dígitos.");
    }

    [Theory]
    [InlineData("180055400")]
    [InlineData("1800554-00")]
    public void School_code_that_normalizes_passes(string schoolCode)
    {
        var result = _validator.TestValidate(CreateRequest(schoolCode: schoolCode));

        result.ShouldNotHaveValidationErrorFor(x => x.SchoolCode);
    }

    [Fact]
    public void Empty_started_by_fails()
    {
        var result = _validator.TestValidate(CreateRequest(startedBy: ""));

        result.ShouldHaveValidationErrorFor(x => x.StartedBy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public void Expected_student_count_outside_one_to_five_hundred_fails(int expectedStudentCount)
    {
        var result = _validator.TestValidate(CreateRequest(expectedStudentCount: expectedStudentCount));

        result.ShouldHaveValidationErrorFor(x => x.ExpectedStudentCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void Expected_student_count_at_the_inclusive_bounds_passes(int expectedStudentCount)
    {
        var result = _validator.TestValidate(CreateRequest(expectedStudentCount: expectedStudentCount));

        result.ShouldNotHaveValidationErrorFor(x => x.ExpectedStudentCount);
    }

    [Fact]
    public void Classroom_code_longer_than_64_fails()
    {
        var result = _validator.TestValidate(CreateRequest(classroomCode: new string('a', 65)));

        result.ShouldHaveValidationErrorFor(x => x.ClassroomCode);
    }

    [Fact]
    public void Commission_code_longer_than_64_fails()
    {
        var result = _validator.TestValidate(CreateRequest(commissionCode: new string('a', 65)));

        result.ShouldHaveValidationErrorFor(x => x.CommissionCode);
    }

    [Fact]
    public void School_year_longer_than_16_fails()
    {
        var result = _validator.TestValidate(CreateRequest(schoolYear: new string('a', 17)));

        result.ShouldHaveValidationErrorFor(x => x.SchoolYear);
    }

    [Fact]
    public void Roster_snapshot_id_longer_than_128_fails()
    {
        var result = _validator.TestValidate(CreateRequest(rosterSnapshotId: new string('a', 129)));

        result.ShouldHaveValidationErrorFor(x => x.RosterSnapshotId);
    }

    [Fact]
    public void Roster_section_id_longer_than_128_fails()
    {
        var result = _validator.TestValidate(CreateRequest(rosterSectionId: new string('a', 129)));

        result.ShouldHaveValidationErrorFor(x => x.RosterSectionId);
    }

    [Fact]
    public void All_nominal_fields_null_together_passes()
    {
        var result = _validator.TestValidate(CreateRequest());

        result.ShouldNotHaveValidationErrorFor(x => x);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void All_nominal_fields_set_together_passes()
    {
        var result = _validator.TestValidate(CreateRequest(
            schoolYear: "2026",
            rosterSnapshotId: "snapshot-1",
            rosterSectionId: "section-1"));

        result.ShouldNotHaveValidationErrorFor(x => x);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void School_year_alone_fails_the_nominal_fields_rule()
    {
        var result = _validator.TestValidate(CreateRequest(schoolYear: "2026"));

        result.ShouldHaveValidationErrorFor(x => x).WithErrorMessage(NominalFieldsMessage);
    }

    [Fact]
    public void Roster_snapshot_id_alone_fails_the_nominal_fields_rule()
    {
        var result = _validator.TestValidate(CreateRequest(rosterSnapshotId: "snapshot-1"));

        result.ShouldHaveValidationErrorFor(x => x).WithErrorMessage(NominalFieldsMessage);
    }

    [Fact]
    public void Two_nominal_fields_without_the_third_fail_the_nominal_fields_rule()
    {
        var result = _validator.TestValidate(CreateRequest(
            schoolYear: "2026",
            rosterSnapshotId: "snapshot-1"));

        result.ShouldHaveValidationErrorFor(x => x).WithErrorMessage(NominalFieldsMessage);
    }

    [Fact]
    public void A_blank_nominal_field_alongside_others_fails_the_nominal_fields_rule()
    {
        var result = _validator.TestValidate(CreateRequest(
            schoolYear: "2026",
            rosterSnapshotId: "snapshot-1",
            rosterSectionId: "   "));

        result.ShouldHaveValidationErrorFor(x => x).WithErrorMessage(NominalFieldsMessage);
    }
}
