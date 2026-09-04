using FluentValidation;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Shared.Infrastructure.Validation;

public sealed class CreateSessionRequestValidator : AbstractValidator<CreateSessionRequest>
{
    public CreateSessionRequestValidator()
    {
        RuleFor(static x => x.ExamVersionId).NotEmpty();
        RuleFor(static x => x.SchoolCode)
            .Must(static value => CueCode.TryNormalize(value, out _))
            .WithMessage($"El CUE debe contener exactamente {CueCode.Length} dígitos.");
        RuleFor(static x => x.StartedBy).NotEmpty();
        RuleFor(static x => x.ExpectedStudentCount).InclusiveBetween(1, 500);
        RuleFor(static x => x.ClassroomCode).MaximumLength(64);
        RuleFor(static x => x.CommissionCode).MaximumLength(64);
        RuleFor(static x => x.SchoolYear).MaximumLength(16);
        RuleFor(static x => x.RosterSnapshotId).MaximumLength(128);
        RuleFor(static x => x.RosterSectionId).MaximumLength(128);
        RuleFor(static x => x)
            .Must(static request =>
                (request.SchoolYear is null && request.RosterSnapshotId is null && request.RosterSectionId is null) ||
                (!string.IsNullOrWhiteSpace(request.SchoolYear) &&
                 !string.IsNullOrWhiteSpace(request.RosterSnapshotId) &&
                 !string.IsNullOrWhiteSpace(request.RosterSectionId)))
            .WithMessage("schoolYear, rosterSnapshotId y rosterSectionId deben enviarse juntos para una sesión nominal.");
    }
}
