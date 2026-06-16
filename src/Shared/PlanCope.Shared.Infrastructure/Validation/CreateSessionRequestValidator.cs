using FluentValidation;
using PlanCope.Shared.Contracts.Local;

namespace PlanCope.Shared.Infrastructure.Validation;

public sealed class CreateSessionRequestValidator : AbstractValidator<CreateSessionRequest>
{
    public CreateSessionRequestValidator()
    {
        RuleFor(static x => x.ExamVersionId).NotEmpty();
        RuleFor(static x => x.SchoolCode).NotEmpty().MaximumLength(64);
        RuleFor(static x => x.StartedBy).NotEmpty();
        RuleFor(static x => x.ClassroomCode).MaximumLength(64);
        RuleFor(static x => x.CommissionCode).MaximumLength(64);
    }
}
