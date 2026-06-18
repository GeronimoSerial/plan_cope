using System.Text.Json;
using FluentValidation;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;

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

        if (block.BlockType is BlockType.Text &&
            (!config.TryGetProperty("content", out var content) || content.ValueKind is not JsonValueKind.String))
        {
            context.AddFailure("config.content", "text requires a content string.");
        }

        if (block.BlockType is BlockType.MultipleChoice)
        {
            if (!config.TryGetProperty("question", out var question) || question.ValueKind is not JsonValueKind.String)
            {
                context.AddFailure("config.question", "multiple_choice requires a question string.");
            }

            if (!config.TryGetProperty("options", out var options) || options.ValueKind is not JsonValueKind.Array || options.GetArrayLength() < 2)
            {
                context.AddFailure("config.options", "multiple_choice requires at least two options.");
            }
        }

        if (block.BlockType is BlockType.TrueFalse &&
            (!config.TryGetProperty("question", out var trueFalseQuestion) || trueFalseQuestion.ValueKind is not JsonValueKind.String))
        {
            context.AddFailure("config.question", "true_false requires a question string.");
        }

        if (block.BlockType is BlockType.ShortAnswer &&
            (!config.TryGetProperty("prompt", out var prompt) || prompt.ValueKind is not JsonValueKind.String))
        {
            context.AddFailure("config.prompt", "short_answer requires a prompt string.");
        }

        if (block.BlockType is BlockType.Image &&
            (!config.TryGetProperty("assetId", out var assetId) || assetId.ValueKind is not JsonValueKind.String))
        {
            context.AddFailure("config.assetId", "image requires an assetId string.");
        }
    }
}
