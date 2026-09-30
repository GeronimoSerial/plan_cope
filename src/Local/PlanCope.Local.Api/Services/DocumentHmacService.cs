using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using PlanCope.Local.Api.Data;

namespace PlanCope.Local.Api.Services;

public sealed class NominalizationOptions
{
    public const string SectionName = "Nominalization";

    public string DocumentHmacKey { get; init; } = string.Empty;
}

public interface IDocumentHmacService
{
    void EnsureKey();

    string ComputeHash(string document);

    string ComputeLast4(string document);
}

public sealed class DocumentHmacService : IDocumentHmacService
{
    private const string StoredKeyName = "document_hmac_key";
    private static readonly object KeyCreationLock = new();
    private readonly string _configuredKey;
    private readonly ILocalSqliteConnectionFactory? _connectionFactory;
    private byte[]? _resolvedKey;

    // Retained for direct configured-key consumers such as tests and developer tools.
    public DocumentHmacService(IOptions<NominalizationOptions> options)
    {
        _configuredKey = options.Value.DocumentHmacKey;
    }

    public DocumentHmacService(IOptions<NominalizationOptions> options, ILocalSqliteConnectionFactory connectionFactory)
    {
        _configuredKey = options.Value.DocumentHmacKey;
        _connectionFactory = connectionFactory;
    }

    public string ComputeHash(string document)
    {
        var normalized = NormalizeDocument(document);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Document must contain at least one digit.", nameof(document));
        }

        return Convert.ToHexString(HMACSHA256.HashData(ResolveKey(), Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public void EnsureKey() => _ = ResolveKey();

    public string ComputeLast4(string document)
    {
        var normalized = NormalizeDocument(document);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Document must contain at least one digit.", nameof(document));
        }

        return normalized.Length <= 4 ? normalized : normalized[^4..];
    }

    private byte[] ResolveKey()
    {
        if (!string.IsNullOrWhiteSpace(_configuredKey))
        {
            var configured = Encoding.UTF8.GetBytes(_configuredKey);
            if (configured.Length < 32)
                throw new InvalidOperationException("Nominalization:DocumentHmacKey must contain at least 32 UTF-8 bytes.");
            return configured;
        }

        if (_resolvedKey is not null) return _resolvedKey;
        if (_connectionFactory is null)
            throw new InvalidOperationException("A local connection factory is required to create the installation document key.");

        lock (KeyCreationLock)
        {
            if (_resolvedKey is not null) return _resolvedKey;
            using var connection = _connectionFactory.CreateOpenConnection();
            var storedJson = connection.QuerySingleOrDefault<string>(
                "SELECT value_json FROM sync_state WHERE key = @Key LIMIT 1;", new { Key = StoredKeyName });
            if (!string.IsNullOrWhiteSpace(storedJson))
            {
                var encodedKey = JsonSerializer.Deserialize<string>(storedJson);
                if (encodedKey is null) throw new InvalidOperationException("The installation document key is invalid.");
                var storedKey = Convert.FromBase64String(encodedKey);
                if (storedKey.Length != 32) throw new InvalidOperationException("The installation document key has an invalid length.");
                return _resolvedKey = storedKey;
            }

            // Keep the same key across restarts and expiry wipes. Expiry removes roster rows,
            // where document hashes live; attempts and outbox payloads retain only documentLast4.
            var generatedKey = RandomNumberGenerator.GetBytes(32);
            var valueJson = JsonSerializer.Serialize(Convert.ToBase64String(generatedKey));
            connection.Execute("""
                INSERT INTO sync_state (id, key, value_json, updated_at)
                VALUES (@Id, @Key, @ValueJson, @UpdatedAt)
                ON CONFLICT(key) DO NOTHING;
                """, new
            {
                Id = Guid.NewGuid().ToString("N"),
                Key = StoredKeyName,
                ValueJson = valueJson,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
            });
            var persistedJson = connection.QuerySingle<string>(
                "SELECT value_json FROM sync_state WHERE key = @Key LIMIT 1;", new { Key = StoredKeyName });
            var persistedKey = JsonSerializer.Deserialize<string>(persistedJson);
            if (persistedKey is null) throw new InvalidOperationException("The installation document key could not be persisted.");
            var keyBytes = Convert.FromBase64String(persistedKey);
            if (keyBytes.Length != 32) throw new InvalidOperationException("The installation document key has an invalid length.");
            return _resolvedKey = keyBytes;
        }
    }

    private static string NormalizeDocument(string? document)
    {
        return document is null ? string.Empty : new string(document.Where(char.IsDigit).ToArray());
    }
}
