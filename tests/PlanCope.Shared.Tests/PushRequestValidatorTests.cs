using System.Text.Json;
using FluentValidation.TestHelper;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Infrastructure.Validation;
using Xunit;

namespace PlanCope.Shared.Tests;

public sealed class PushRequestValidatorTests
{
    private readonly PushRequestValidator _validator = new();

    private static PushItem ValidItem() => new(
        IdempotencyKey: "op-001",
        EventType: "exam.updated",
        AggregateType: "exam",
        AggregateId: "exam-1",
        Payload: JsonDocument.Parse("{}").RootElement,
        Checksum: "sha256-abc123",
        OccurredAt: "2026-09-16T10:00:00Z");

    private static PushRequest Request(params PushItem[] items) => new("node-1", items);

    [Fact]
    public void Single_fully_valid_item_passes_with_no_errors()
    {
        var result = _validator.TestValidate(Request(ValidItem()));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_NodeId_fails()
    {
        var result = _validator.TestValidate(Request(ValidItem()) with { NodeId = "" });

        result.ShouldHaveValidationErrorFor(x => x.NodeId);
    }

    [Fact]
    public void Null_Items_fails_validation()
    {
        var result = _validator.TestValidate(new PushRequest("node-1", null!));

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Empty_IdempotencyKey_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { IdempotencyKey = "" }));

        result.ShouldHaveValidationErrorFor("Items[0].IdempotencyKey");
    }

    [Fact]
    public void IdempotencyKey_over_128_characters_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { IdempotencyKey = new string('k', 129) }));

        result.ShouldHaveValidationErrorFor("Items[0].IdempotencyKey");
    }

    [Fact]
    public void Empty_EventType_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { EventType = "" }));

        result.ShouldHaveValidationErrorFor("Items[0].EventType");
    }

    [Fact]
    public void EventType_over_128_characters_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { EventType = new string('e', 129) }));

        result.ShouldHaveValidationErrorFor("Items[0].EventType");
    }

    [Fact]
    public void Empty_AggregateType_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { AggregateType = "" }));

        result.ShouldHaveValidationErrorFor("Items[0].AggregateType");
    }

    [Fact]
    public void AggregateType_over_128_characters_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { AggregateType = new string('a', 129) }));

        result.ShouldHaveValidationErrorFor("Items[0].AggregateType");
    }

    [Fact]
    public void Empty_AggregateId_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { AggregateId = "" }));

        result.ShouldHaveValidationErrorFor("Items[0].AggregateId");
    }

    [Fact]
    public void Empty_Checksum_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { Checksum = "" }));

        result.ShouldHaveValidationErrorFor("Items[0].Checksum");
    }

    [Fact]
    public void Empty_OccurredAt_fails_at_child_path()
    {
        var result = _validator.TestValidate(Request(ValidItem() with { OccurredAt = "" }));

        result.ShouldHaveValidationErrorFor("Items[0].OccurredAt");
    }
}