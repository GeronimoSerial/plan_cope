using System.Text.Json;
using PlanCope.Shared.Contracts.Sync;

namespace PlanCope.Central.Api.Sync;

/// <summary>
/// Pure idempotency/checksum policy used by the push receiver. Keeping this
/// decision deterministic makes retries and concurrent deliveries testable
/// without requiring a live PostgreSQL instance.
/// </summary>
public static class SyncPushPolicy
{
    public static PushItemResult Evaluate(PushItem item, JsonElement? existingPayload)
    {
        if (!string.Equals(item.Checksum, SyncPayloadChecksum.Calculate(item.Payload), StringComparison.OrdinalIgnoreCase))
        {
            return new PushItemResult(item.IdempotencyKey, "failed", "Payload checksum does not match.");
        }

        if (existingPayload is not null)
        {
            return string.Equals(
                SyncPayloadChecksum.Calculate(existingPayload.Value),
                item.Checksum,
                StringComparison.OrdinalIgnoreCase)
                ? new PushItemResult(item.IdempotencyKey, "duplicate", null)
                : new PushItemResult(item.IdempotencyKey, "failed", "The idempotency key was already used for a different payload.");
        }

        return new PushItemResult(item.IdempotencyKey, "accepted", null);
    }
}
