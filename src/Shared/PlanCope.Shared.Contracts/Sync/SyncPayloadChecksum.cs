using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlanCope.Shared.Contracts.Sync;

public static class SyncPayloadChecksum
{
    public static string Calculate(JsonElement payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, payload);
        }

        return Hash(stream.ToArray());
    }

    /// <summary>
    /// Checksums emitted by Local versions before object properties were sorted. Central accepts
    /// these while old SQLite outbox rows drain after an upgrade.
    /// </summary>
    public static string CalculateLegacy(JsonElement payload) =>
        Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    private static string Hash(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unsupported JSON value kind {element.ValueKind}.");
        }
    }
}
