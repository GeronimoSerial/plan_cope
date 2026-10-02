using System.Globalization;
using System.Numerics;
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
    /// Checksums emitted by older Local binaries before object properties were sorted. Central
    /// accepts them while older binaries may still be delivering pending outbox rows.
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
                writer.WriteRawValue(CanonicalizeNumber(element.GetRawText()), skipInputValidation: false);
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

    private static string CanonicalizeNumber(string raw)
    {
        var exponentMarker = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentMarker < 0 ? raw : raw[..exponentMarker];
        var exponent = exponentMarker < 0
            ? BigInteger.Zero
            : BigInteger.Parse(raw[(exponentMarker + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var negative = mantissa[0] == '-';
        if (negative) mantissa = mantissa[1..];

        var decimalPoint = mantissa.IndexOf('.');
        var fractionalDigits = decimalPoint < 0 ? 0 : mantissa.Length - decimalPoint - 1;
        var digits = decimalPoint < 0 ? mantissa : string.Concat(mantissa.AsSpan(0, decimalPoint), mantissa.AsSpan(decimalPoint + 1));
        digits = digits.TrimStart('0');
        if (digits.Length == 0) return "0";
        exponent -= fractionalDigits;

        var trailingZeroCount = 0;
        for (var index = digits.Length - 1; index >= 0 && digits[index] == '0'; index--) trailingZeroCount++;
        if (trailingZeroCount > 0)
        {
            digits = digits[..^trailingZeroCount];
            exponent += trailingZeroCount;
        }

        var sign = negative ? "-" : string.Empty;
        var exponentSuffix = exponent.IsZero ? string.Empty : $"e{exponent.ToString(CultureInfo.InvariantCulture)}";
        return $"{sign}{digits}{exponentSuffix}";
    }
}
