using FluentValidation;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Shared.Infrastructure.Validation;

public sealed class ExamVersionValidator : AbstractValidator<ExamVersion>
{
    public ExamVersionValidator()
    {
        RuleFor(static x => x.Id).NotEmpty();
        RuleFor(static x => x.ExamId).NotEmpty();
        RuleFor(static x => x.VersionNumber).GreaterThan(0);
        RuleFor(static x => x.SchemaVersion).GreaterThan(0);
        RuleFor(static x => x.Status).NotEmpty().MaximumLength(32);
    }
}
