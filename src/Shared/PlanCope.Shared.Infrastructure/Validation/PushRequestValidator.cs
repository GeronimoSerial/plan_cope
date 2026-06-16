using FluentValidation;
using PlanCope.Shared.Contracts.Sync;

namespace PlanCope.Shared.Infrastructure.Validation;

public sealed class PushRequestValidator : AbstractValidator<PushRequest>
{
    public PushRequestValidator()
    {
        RuleFor(static x => x.NodeId).NotEmpty();
        RuleFor(static x => x.Items).NotNull();
        RuleForEach(static x => x.Items).ChildRules(static item =>
        {
            item.RuleFor(static x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
            item.RuleFor(static x => x.EventType).NotEmpty().MaximumLength(128);
            item.RuleFor(static x => x.AggregateType).NotEmpty().MaximumLength(128);
            item.RuleFor(static x => x.AggregateId).NotEmpty();
            item.RuleFor(static x => x.Checksum).NotEmpty();
            item.RuleFor(static x => x.OccurredAt).NotEmpty();
        });
    }
}
