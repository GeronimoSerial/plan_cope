using System.Text.Json;
using FluentValidation.TestHelper;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Infrastructure.Validation;
using Xunit;

namespace PlanCope.Shared.Tests;

public sealed class ExamBlockValidatorTests
{
    private readonly ExamBlockValidator _validator = new();

    private static ExamBlock CreateBlock(
        string id = "block-1",
        string examVersionId = "version-1",
        int orderIndex = 0,
        BlockType blockType = BlockType.Text,
        string config = "{\"content\":\"hello\"}")
        => new(
            id,
            examVersionId,
            orderIndex,
            blockType,
            Title: null,
            Description: null,
            JsonDocument.Parse(config),
            Validation: null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void Empty_id_fails()
    {
        var result = _validator.TestValidate(CreateBlock(id: ""));

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Empty_exam_version_id_fails()
    {
        var result = _validator.TestValidate(CreateBlock(examVersionId: ""));

        result.ShouldHaveValidationErrorFor(x => x.ExamVersionId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Negative_order_index_fails(int orderIndex)
    {
        var result = _validator.TestValidate(CreateBlock(orderIndex: orderIndex));

        result.ShouldHaveValidationErrorFor(x => x.OrderIndex);
    }

    [Fact]
    public void Zero_order_index_passes()
    {
        var result = _validator.TestValidate(CreateBlock(orderIndex: 0));

        result.ShouldNotHaveValidationErrorFor(x => x.OrderIndex);
    }

    [Fact]
    public void Out_of_range_block_type_fails_is_in_enum()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: (BlockType)999, config: "{}"));

        result.ShouldHaveValidationErrorFor(x => x.BlockType);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    public void Config_root_that_is_not_a_json_object_fails(string config)
    {
        // An out-of-range BlockType keeps the type-specific rule from touching
        // config (TryGetProperty throws on a non-object root), isolating this rule.
        var result = _validator.TestValidate(CreateBlock(blockType: (BlockType)999, config: config));

        result.ShouldHaveValidationErrorFor(x => x.Config.RootElement.ValueKind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"content\":123}")]
    public void Text_without_a_content_string_fails_on_content(string config)
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.Text, config: config));

        result.ShouldHaveValidationErrorFor("config.content");
    }

    [Fact]
    public void Text_with_a_content_string_passes()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.Text, config: "{\"content\":\"hello\"}"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void MultipleChoice_without_a_question_fails_on_question()
    {
        var result = _validator.TestValidate(
            CreateBlock(blockType: BlockType.MultipleChoice, config: "{\"options\":[\"a\",\"b\"]}"));

        result.ShouldHaveValidationErrorFor("config.question");
    }

    [Theory]
    [InlineData("{\"question\":\"q\"}")]
    [InlineData("{\"question\":\"q\",\"options\":[\"a\"]}")]
    public void MultipleChoice_with_fewer_than_two_options_fails_on_options(string config)
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.MultipleChoice, config: config));

        result.ShouldHaveValidationErrorFor("config.options");
    }

    [Fact]
    public void MultipleChoice_that_is_fully_valid_passes()
    {
        var result = _validator.TestValidate(
            CreateBlock(blockType: BlockType.MultipleChoice, config: "{\"question\":\"q\",\"options\":[\"a\",\"b\"]}"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TrueFalse_without_a_question_fails_on_question()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.TrueFalse, config: "{}"));

        result.ShouldHaveValidationErrorFor("config.question");
    }

    [Fact]
    public void TrueFalse_with_a_question_passes()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.TrueFalse, config: "{\"question\":\"q\"}"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ShortAnswer_without_a_prompt_fails_on_prompt()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.ShortAnswer, config: "{}"));

        result.ShouldHaveValidationErrorFor("config.prompt");
    }

    [Fact]
    public void ShortAnswer_with_a_prompt_passes()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.ShortAnswer, config: "{\"prompt\":\"p\"}"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Image_without_an_asset_id_fails_on_asset_id()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.Image, config: "{}"));

        result.ShouldHaveValidationErrorFor("config.assetId");
    }

    [Fact]
    public void Image_with_an_asset_id_passes()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.Image, config: "{\"assetId\":\"a\"}"));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
