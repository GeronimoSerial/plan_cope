using System.Text.Json;
using PlanCope.Central.Api.Sync;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class SyncPushPolicyTests
{
    [Fact]
    public void New_payload_is_accepted_when_checksum_is_valid()
    {
        var item = CreateItem("key-1", "attempt-1", "GE:42");

        var result = SyncPushPolicy.Evaluate(item, null);

        Assert.Equal("accepted", result.Status);
    }

    [Fact]
    public void Same_idempotency_key_and_payload_is_duplicate()
    {
        var item = CreateItem("key-1", "attempt-1", "GE:42");
        using var existing = JsonDocument.Parse(item.Payload.GetRawText());

        var result = SyncPushPolicy.Evaluate(item, existing.RootElement);

        Assert.Equal("duplicate", result.Status);
    }

    [Fact]
    public void Reusing_idempotency_key_with_a_different_payload_is_rejected()
    {
        var item = CreateItem("key-1", "attempt-1", "GE:42");
        using var existing = JsonDocument.Parse("{\"attempt\":{\"id\":\"attempt-1\",\"studentCode\":\"GE:99\"}}");

        var result = SyncPushPolicy.Evaluate(item, existing.RootElement);

        Assert.Equal("failed", result.Status);
        Assert.Contains("different payload", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_attempt_payload_keeps_nominal_fields_nullable()
    {
        var item = CreateItem("legacy-key", "attempt-legacy", "AUTO-0001");
        using var payload = JsonDocument.Parse(item.Payload.GetRawText());
        var attempt = payload.RootElement.GetProperty("attempt").Deserialize<StudentAttempt>(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(attempt);
        Assert.Null(attempt!.GePersonId);
        Assert.Null(attempt.StudentFirstName);
        Assert.Null(attempt.DocumentLast4);
    }

    private static PushItem CreateItem(string idempotencyKey, string attemptId, string studentCode)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            attempt = new
            {
                id = attemptId,
                deliverySessionId = "session-1",
                studentCode,
                status = "submitted",
                startedAt = "2026-08-20T12:00:00+00:00",
                submittedAt = "2026-08-20T12:05:00+00:00",
                localSequence = 1,
                confirmationCode = "ABC123"
            },
            answers = Array.Empty<object>()
        });
        return new PushItem(
            idempotencyKey,
            SyncEventTypes.AttemptSubmitted,
            "student_attempt",
            attemptId,
            payload,
            SyncPayloadChecksum.Calculate(payload),
            "2026-08-20T12:05:00+00:00");
    }
}
