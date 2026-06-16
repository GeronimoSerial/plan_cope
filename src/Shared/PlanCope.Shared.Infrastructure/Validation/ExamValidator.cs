using FluentValidation;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Shared.Infrastructure.Validation;

public sealed class ExamValidator : AbstractValidator<Exam>
{
    public ExamValidator()
    {
        RuleFor(static x => x.Id).NotEmpty();
        RuleFor(static x => x.Code).NotEmpty().MaximumLength(64).Must(ExamCode.IsValid);
        RuleFor(static x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(static x => x.Status).NotEmpty().MaximumLength(32);
    }
}
