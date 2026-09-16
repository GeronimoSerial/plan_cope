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
    public static GradableBlock MapBlock(string blockId, BlockType type, decimal? scoreValue, JsonElement? correctAnswer)
    {
        return new GradableBlock
        {
            BlockId = blockId,
            Type = type,
            ScoreMax = scoreValue ?? 1m,
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
                return new SubmittedAnswer { SelectedOptionIds = ReadStringArray(answer.Value) };
            case BlockType.TrueFalse:
                return new SubmittedAnswer { SelectedBoolean = ReadBoolean(answer.Value) };
            case BlockType.ShortAnswer:
                return new SubmittedAnswer { Text = ReadString(answer.Value) };
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
            case BlockType.ShortAnswer:
                return new GradingAnswerKey { AcceptedAnswers = ReadAcceptedAnswers(correctAnswer.Value) };
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

    private static bool? ReadBoolean(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;
    }

    private static string? ReadString(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static IReadOnlyList<string> ReadAcceptedAnswers(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty("accepted", out var accepted)
            ? ReadStringArray(accepted)
            : Array.Empty<string>();
    }
}