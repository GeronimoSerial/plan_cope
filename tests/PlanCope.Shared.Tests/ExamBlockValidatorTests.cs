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
        BlockType blockType = BlockType.MultipleChoice,
        string config = "{\"question\":\"q\",\"options\":[\"a\",\"b\"]}")
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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Removed_block_types_fail_validation(int value)
    {
        var result = _validator.TestValidate(CreateBlock(blockType: (BlockType)value, config: "{}"));

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
    public void MultipleChoice_accepts_known_policy_when_multiple_is_true()
    {
        var result = _validator.TestValidate(CreateBlock(config: "{\"question\":\"q\",\"multiple\":true,\"scoringPolicy\":\"ProportionalPlain\",\"options\":[\"a\",\"b\"]}"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("NotARealPolicy")]
    [InlineData("0")]
    public void MultipleChoice_rejects_unknown_policy_values(string policy)
    {
        var config = JsonSerializer.Serialize(new { question = "q", multiple = true, scoringPolicy = policy, options = new[] { "a", "b" } });
        var result = _validator.TestValidate(CreateBlock(config: config));
        result.ShouldHaveValidationErrorFor("config.scoringPolicy");
    }

    [Fact]
    public void SingleChoice_rejects_a_policy()
    {
        var result = _validator.TestValidate(CreateBlock(config: "{\"question\":\"q\",\"multiple\":false,\"scoringPolicy\":\"AllOrNothing\",\"options\":[\"a\",\"b\"]}"));
        result.ShouldHaveValidationErrorFor("config.scoringPolicy");
    }

    [Fact]
    public void TrueFalse_rejects_a_policy()
    {
        var result = _validator.TestValidate(CreateBlock(blockType: BlockType.TrueFalse, config: "{\"question\":\"q\",\"scoringPolicy\":\"AllOrNothing\"}"));
        result.ShouldHaveValidationErrorFor("config.scoringPolicy");
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
    public void Question_accepts_a_non_empty_image_asset_reference()
    {
        var result = _validator.TestValidate(CreateBlock(config: "{\"question\":\"q\",\"options\":[\"a\",\"b\"],\"imageAssetId\":\"asset-1\"}"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("17")]
    public void Question_rejects_invalid_image_asset_reference(string imageAssetId)
    {
        var result = _validator.TestValidate(CreateBlock(config: $"{{\"question\":\"q\",\"options\":[\"a\",\"b\"],\"imageAssetId\":{imageAssetId}}}"));
        result.ShouldHaveValidationErrorFor("config.imageAssetId");
    }

}
