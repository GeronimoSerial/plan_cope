using PlanCope.Central.Api.Services;
using PlanCope.Shared.Domain.Central;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class ActivationKeyServiceTests
{
    private const string Base32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly ActivationKeyService _service = new();

    [Fact]
    public void GeneratedKeys_AreAlwaysWellFormed()
    {
        for (var i = 0; i < 50; i++)
        {
            var generated = _service.Generate();
            Assert.True(ActivationKeyService.IsWellFormed(generated.PlaintextKey));
        }
    }

    [Fact]
    public void Checksum_RejectsSingleCharacterPayloadTypos()
    {
        for (var i = 0; i < 50; i++)
        {
            var generated = _service.Generate();
            var typo = MutatePayloadCharacter(generated.PlaintextKey);
            Assert.False(ActivationKeyService.IsWellFormed(typo));
        }
    }

    [Fact]
    public void Normalization_AcceptsLowercaseAndErrantDashPlacement()
    {
        var generated = _service.Generate();
        var scrambled = generated.PlaintextKey.ToLowerInvariant().Replace("-", string.Empty);

        Assert.True(ActivationKeyService.IsWellFormed(scrambled));
        Assert.True(ActivationKeyService.TryNormalize(scrambled, out var canonical));
        Assert.Equal(generated.PlaintextKey, canonical);
    }

    [Fact]
    public void Normalization_RejectsWrongBrandAndWrongLength()
    {
        var generated = _service.Generate();
        var wrongBrand = "PCOPI-" + generated.PlaintextKey[6..];
        var extraCharacter = generated.PlaintextKey + "A";

        Assert.False(ActivationKeyService.IsWellFormed(wrongBrand));
        Assert.False(ActivationKeyService.IsWellFormed(extraCharacter));
    }

    [Fact]
    public void Argon2_HashMatchesForCorrectKeyAndRejectsWrongKey()
    {
        var generated = _service.Generate();
        var stored = StoredKey(generated: generated);

        Assert.True(_service.HashMatches(generated.PlaintextKey, stored));
        Assert.False(_service.HashMatches(MutatePayloadCharacter(generated.PlaintextKey), stored));
        Assert.False(_service.HashMatches("definitely-not-a-key", stored));
    }

    [Fact]
    public void HashMatches_WithNoStoredKey_ReturnsFalse()
    {
        var generated = _service.Generate();

        Assert.False(_service.HashMatches(generated.PlaintextKey, null));
    }

    [Fact]
    public void IsExpired_OnlyWhenPastExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var expiring = StoredKey(ExpiresAt: now.AddHours(1));

        Assert.False(ActivationKeyService.IsExpired(expiring, now));
        Assert.True(ActivationKeyService.IsExpired(expiring, now.AddHours(2)));

        var neverExpiring = StoredKey(ExpiresAt: null);
        Assert.False(ActivationKeyService.IsExpired(neverExpiring, now));
    }

    [Fact]
    public void IsRevoked_OnlyWhenRevokedAtSet()
    {
        var now = DateTimeOffset.UtcNow;
        var active = StoredKey();
        var revoked = StoredKey(RevokedAt: now);

        Assert.False(ActivationKeyService.IsRevoked(active));
        Assert.True(ActivationKeyService.IsRevoked(revoked));
    }

    [Fact]
    public void IsExhausted_OnlyWhenCountReachesMax()
    {
        var key = StoredKey(MaxActivations: 3, ActivationCount: 2);

        Assert.False(ActivationKeyService.IsExhausted(key));
        Assert.True(ActivationKeyService.IsExhausted(key with { ActivationCount = 3 }));
    }

    private static ActivationKey StoredKey(
        DateTimeOffset? ExpiresAt = null,
        DateTimeOffset? RevokedAt = null,
        int MaxActivations = 1,
        int ActivationCount = 0,
        ActivationKeyService.GeneratedActivationKey? generated = null)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = generated?.KeyHash ?? "unused-hash";
        var prefix = generated?.KeyPrefix ?? "PCOPE-XX";
        return new ActivationKey(
            "key-1",
            hash,
            prefix,
            "test",
            now,
            ExpiresAt,
            MaxActivations,
            ActivationCount,
            RevokedAt,
            null,
            null,
            null);
    }

    private static string MutatePayloadCharacter(string plaintext)
    {
        var chars = plaintext.ToCharArray();
        const int target = 7; // second character of the first payload group
        var replacement = Base32Alphabet[Random.Shared.Next(Base32Alphabet.Length)];
        while (replacement == chars[target])
        {
            replacement = Base32Alphabet[Random.Shared.Next(Base32Alphabet.Length)];
        }

        chars[target] = replacement;
        return new string(chars);
    }
}