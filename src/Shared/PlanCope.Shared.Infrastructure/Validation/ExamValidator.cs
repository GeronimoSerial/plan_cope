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
        RuleFor(static x => x.Courses)
            .NotEmpty()
            .Must(static courses => courses.All(ExamCourses.IsValid))
            .WithMessage("At least one valid course is required.");
        RuleFor(static x => x.Area)
            .Must(static area => area is null || !string.IsNullOrWhiteSpace(area))
            .WithMessage("Area cannot be empty when provided.")
            .MaximumLength(256);
    }
}
