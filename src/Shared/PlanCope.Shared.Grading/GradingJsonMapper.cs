using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using PlanCope.Shared.Domain;

namespace PlanCope.Shared.Grading;

/// <summary>
/// Maps author answer keys and student submissions from JSON into the engine's plain types.
/// Pure and convention-only: missing or malformed data degrades to an empty key or blank, never throws.
/// </summary>
public static class GradingJsonMapper
{
    /// <summary>
    /// Builds a gradable block from its JSON answer key, defaulting an unset score to one point.
    /// </summary>
    public static GradableBlock MapBlock(string blockId, BlockType type, decimal? scoreValue, JsonElement? correctAnswer, JsonElement? config = null)
    {
        var hasConfig = config.HasValue && config.Value.ValueKind == JsonValueKind.Object;
        var configElement = hasConfig ? config!.Value : default;
        var allowsMultipleAnswers = type == BlockType.MultipleChoice &&
            hasConfig && configElement.TryGetProperty("multiple", out var multiple) && multiple.ValueKind == JsonValueKind.True;
        ScoringPolicy? scoringPolicy = null;
        if (allowsMultipleAnswers && configElement.TryGetProperty("scoringPolicy", out var policyValue) && policyValue.ValueKind == JsonValueKind.String)
        {
            scoringPolicy = ScoringPolicyParser.Parse(policyValue.GetString());
        }

        return new GradableBlock
        {
            BlockId = blockId,
            Type = type,
            ScoreMax = scoreValue ?? 1m,
            AllowsMultipleAnswers = allowsMultipleAnswers,
            ScoringPolicy = scoringPolicy,
            AnswerKey = MapAnswerKey(type, correctAnswer)
        };
    }

    /// <summary>
    /// Maps a submitted answer to the engine's plain shape, or null when the answer is absent (blank).
    /// </summary>
    public static SubmittedAnswer? MapSubmittedAnswer(BlockType type, JsonElement? answer)
    {
        if (IsAbsent(answer))
        {
            return null;
        }

        switch (type)
        {
            case BlockType.MultipleChoice:
                return new SubmittedAnswer { SelectedOptionIds = ReadOptionIds(answer.Value) };
            case BlockType.TrueFalse:
                return new SubmittedAnswer { SelectedBoolean = ReadBoolean(answer.Value) };
            default:
                return null;
        }
    }

    private static GradingAnswerKey MapAnswerKey(BlockType type, JsonElement? correctAnswer)
    {
        if (IsAbsent(correctAnswer))
        {
            return new GradingAnswerKey();
        }

        switch (type)
        {
            case BlockType.MultipleChoice:
                return new GradingAnswerKey { CorrectOptionIds = ReadStringArray(correctAnswer.Value) };
            case BlockType.TrueFalse:
                return new GradingAnswerKey { CorrectBoolean = ReadBoolean(correctAnswer.Value) };
            default:
                return new GradingAnswerKey();
        }
    }

    private static bool IsAbsent([NotNullWhen(false)] JsonElement? element)
    {
        return element is null ||
               element.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.GetString() is { } text)
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ReadOptionIds(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String && element.GetString() is { } optionId)
        {
            return string.IsNullOrWhiteSpace(optionId) ? Array.Empty<string>() : [optionId];
        }

        return ReadStringArray(element);
    }

    private static bool? ReadBoolean(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;
    }

}
