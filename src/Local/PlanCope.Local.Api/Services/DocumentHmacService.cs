using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PlanCope.Local.Api.Services;

public sealed class NominalizationOptions
{
    public const string SectionName = "Nominalization";

    public string DocumentHmacKey { get; init; } = string.Empty;
}

public interface IDocumentHmacService
{
    string ComputeHash(string document);

    string ComputeLast4(string document);
}

public sealed class DocumentHmacService(IOptions<NominalizationOptions> options) : IDocumentHmacService
{
    private readonly string _configuredKey = options.Value.DocumentHmacKey;

    public string ComputeHash(string document)
    {
        var normalized = NormalizeDocument(document);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Document must contain at least one digit.", nameof(document));
        }

        var key = Encoding.UTF8.GetBytes(RequireKey(_configuredKey));
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public string ComputeLast4(string document)
    {
        var normalized = NormalizeDocument(document);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Document must contain at least one digit.", nameof(document));
        }

        return normalized.Length <= 4 ? normalized : normalized[^4..];
    }

    private static string RequireKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Nominalization:DocumentHmacKey must be configured with at least 32 UTF-8 bytes before importing a roster.");
        }

        return key;
    }

    private static string NormalizeDocument(string? document)
    {
        return document is null ? string.Empty : new string(document.Where(char.IsDigit).ToArray());
    }
}
