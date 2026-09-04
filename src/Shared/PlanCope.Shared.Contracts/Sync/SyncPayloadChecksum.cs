using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlanCope.Shared.Contracts.Sync;

public static class SyncPayloadChecksum
{
    public static string Calculate(JsonElement payload)
    {
        var canonical = JsonSerializer.Serialize(payload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
