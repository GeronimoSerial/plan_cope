using System.Text.Json;
using PlanCope.Shared.Domain;
using Xunit;

namespace PlanCope.Shared.Grading.Tests;

public sealed class GradingJsonMapperTests
{
    [Fact]
    public void MapBlock_defaults_score_to_one_when_score_value_is_unset()
    {
        var block = GradingJsonMapper.MapBlock("b1", BlockType.MultipleChoice, null, Json("[\"a\"]"));

        Assert.Equal(1m, block.ScoreMax);
        Assert.Equal("b1", block.BlockId);
    }

    [Fact]
    public void MapBlock_uses_the_supplied_score_when_present()
    {
        var block = GradingJsonMapper.MapBlock("b1", BlockType.MultipleChoice, 2.5m, Json("[\"a\"]"));

        Assert.Equal(2.5m, block.ScoreMax);
    }

    [Fact]
    public void MapBlock_maps_a_multiple_choice_answer_key()
    {
        var block = GradingJsonMapper.MapBlock("b1", BlockType.MultipleChoice, null, Json("[\"b\",\"c\"]"));

        Assert.Equal(new[] { "b", "c" }, block.AnswerKey.CorrectOptionIds);
    }

    [Fact]
    public void MapBlock_maps_a_true_false_answer_key()
    {
        var block = GradingJsonMapper.MapBlock("b1", BlockType.TrueFalse, null, Json("true"));

        Assert.True(block.AnswerKey.CorrectBoolean);
    }

    [Fact]
    public void MapBlock_maps_a_short_answer_answer_key()
    {
        var block = GradingJsonMapper.MapBlock(
            "b1", BlockType.ShortAnswer, null, Json("{\"accepted\":[\"texto1\",\"texto2\"]}"));

        Assert.Equal(new[] { "texto1", "texto2" }, block.AnswerKey.AcceptedAnswers);
    }

    [Theory]
    [InlineData(BlockType.Text)]
    [InlineData(BlockType.Image)]
    public void MapBlock_returns_an_empty_answer_key_for_text_and_image_blocks(BlockType type)
    {
        var block = GradingJsonMapper.MapBlock("b1", type, null, null);

        Assert.Empty(block.AnswerKey.CorrectOptionIds);
        Assert.Empty(block.AnswerKey.AcceptedAnswers);
        Assert.Null(block.AnswerKey.CorrectBoolean);
    }

    [Fact]
    public void MapBlock_returns_an_empty_answer_key_when_correct_answer_is_missing()
    {
        var block = GradingJsonMapper.MapBlock("b1", BlockType.MultipleChoice, null, null);

        Assert.Empty(block.AnswerKey.CorrectOptionIds);
        Assert.Null(block.AnswerKey.CorrectBoolean);
    }

    [Fact]
    public void MapSubmittedAnswer_maps_a_multiple_choice_selection()
    {
        var answer = GradingJsonMapper.MapSubmittedAnswer(BlockType.MultipleChoice, Json("[\"a\",\"c\"]"));

        Assert.NotNull(answer);
        Assert.Equal(new[] { "a", "c" }, answer.SelectedOptionIds);
    }

    [Fact]
    public void MapSubmittedAnswer_maps_a_true_false_selection()
    {
        var answer = GradingJsonMapper.MapSubmittedAnswer(BlockType.TrueFalse, Json("false"));

        Assert.NotNull(answer);
        Assert.False(answer.SelectedBoolean);
    }

    [Fact]
    public void MapSubmittedAnswer_maps_a_short_answer_text()
    {
        var answer = GradingJsonMapper.MapSubmittedAnswer(BlockType.ShortAnswer, Json("\"texto1\""));

        Assert.NotNull(answer);
        Assert.Equal("texto1", answer.Text);
    }

    [Theory]
    [InlineData(BlockType.Text)]
    [InlineData(BlockType.Image)]
    [InlineData(BlockType.MultipleChoice)]
    [InlineData(BlockType.TrueFalse)]
    [InlineData(BlockType.ShortAnswer)]
    public void MapSubmittedAnswer_returns_null_for_an_absent_answer(BlockType type)
    {
        Assert.Null(GradingJsonMapper.MapSubmittedAnswer(type, null));
    }

    [Fact]
    public void MapSubmittedAnswer_returns_null_for_an_undefined_element()
    {
        Assert.Null(GradingJsonMapper.MapSubmittedAnswer(BlockType.MultipleChoice, new JsonElement()));
    }

    [Fact]
    public void MapSubmittedAnswer_returns_null_for_a_json_null_true_false_answer()
    {
        Assert.Null(GradingJsonMapper.MapSubmittedAnswer(BlockType.TrueFalse, Json("null")));
    }

    [Fact]
    public void MapSubmittedAnswer_returns_a_blank_submission_for_an_empty_multiple_choice_array()
    {
        var answer = GradingJsonMapper.MapSubmittedAnswer(BlockType.MultipleChoice, Json("[]"));

        Assert.NotNull(answer);
        Assert.Empty(answer.SelectedOptionIds);
    }

    [Fact]
    public void MapSubmittedAnswer_returns_a_blank_submission_for_an_empty_short_answer_string()
    {
        var answer = GradingJsonMapper.MapSubmittedAnswer(BlockType.ShortAnswer, Json("\"\""));

        Assert.NotNull(answer);
        Assert.Equal(string.Empty, answer.Text);
    }

    private static JsonElement? Json(string raw)
    {
        return JsonDocument.Parse(raw).RootElement.Clone();
    }
}