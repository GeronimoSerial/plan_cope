using System.Text.Json;
using FluentValidation;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Grading;

namespace PlanCope.Shared.Infrastructure.Validation;

public sealed class ExamBlockValidator : AbstractValidator<ExamBlock>
{
    public ExamBlockValidator()
    {
        RuleFor(static x => x.Id).NotEmpty();
        RuleFor(static x => x.ExamVersionId).NotEmpty();
        RuleFor(static x => x.OrderIndex).GreaterThanOrEqualTo(0);
        RuleFor(static x => x.BlockType).IsInEnum();
        RuleFor(static x => x.Config.RootElement.ValueKind).Equal(JsonValueKind.Object);
        RuleFor(static x => x).Custom(ValidateTypeSpecificConfig);
    }

    private static void ValidateTypeSpecificConfig(ExamBlock block, ValidationContext<ExamBlock> context)
    {
        var config = block.Config.RootElement;

        if (config.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        if (block.BlockType is BlockType.MultipleChoice)
        {
            if (config.TryGetProperty("scoringPolicy", out var scoringPolicy))
            {
                if (!config.TryGetProperty("multiple", out var multiple) || multiple.ValueKind != JsonValueKind.True)
                {
                    context.AddFailure("config.scoringPolicy", "scoringPolicy is only valid for multiple-choice questions.");
                }
                else if (scoringPolicy.ValueKind != JsonValueKind.String || ScoringPolicyParser.Parse(scoringPolicy.GetString()) is null)
                {
                    context.AddFailure("config.scoringPolicy", "scoringPolicy must be a known scoring policy.");
                }
            }

            if (!config.TryGetProperty("question", out var question) || question.ValueKind is not JsonValueKind.String)
            {
                context.AddFailure("config.question", "multiple_choice requires a question string.");
            }

            if (!config.TryGetProperty("options", out var options) || options.ValueKind is not JsonValueKind.Array || options.GetArrayLength() < 2)
            {
                context.AddFailure("config.options", "multiple_choice requires at least two options.");
            }
        }
        else if (config.TryGetProperty("scoringPolicy", out _))
        {
            context.AddFailure("config.scoringPolicy", "scoringPolicy is only valid for multiple-choice questions.");
        }

        if (block.BlockType is BlockType.TrueFalse &&
            (!config.TryGetProperty("question", out var trueFalseQuestion) || trueFalseQuestion.ValueKind is not JsonValueKind.String))
        {
            context.AddFailure("config.question", "true_false requires a question string.");
        }

    }
}
