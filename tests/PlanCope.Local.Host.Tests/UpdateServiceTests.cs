using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

public sealed class UpdateServiceTests
{
    private const string AnySha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsInFlightTaskImmediately_DoesNotBlockCaller()
    {
        var backend = new FakeUpdateBackend
        {
            CheckDelay = TimeSpan.FromMilliseconds(1000),
        };
        var service = new UpdateService(backend, UpdateChannel.Stable);

        var stopwatch = Stopwatch.StartNew();
        var checkTask = service.CheckForUpdatesAsync();
        stopwatch.Stop();

        Assert.False(checkTask.IsCompleted);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"CheckForUpdatesAsync blocked the caller for {stopwatch.ElapsedMilliseconds} ms.");

        var firstToComplete = await Task.WhenAny(checkTask, Task.Delay(250));
        Assert.False(checkTask.IsCompleted);
        Assert.NotSame(checkTask, firstToComplete);

        await checkTask;
    }

    [Fact]
    public async Task TryApplyAndRestart_WithoutUserConfirmation_DoesNotApply_AndConfirmedCallDoes()
    {
        var backend = new FakeUpdateBackend();
        var service = new UpdateService(backend, UpdateChannel.Stable);

        await service.DownloadUpdateAsync(AnySha256);

        var appliedWithoutConfirmation = service.TryApplyAndRestart(userConfirmedRestart: false);
        Assert.False(appliedWithoutConfirmation);
        Assert.Equal(0, backend.ApplyCalls);

        var appliedAfterConfirmation = service.TryApplyAndRestart(userConfirmedRestart: true);
        Assert.True(appliedAfterConfirmation);
        Assert.Equal(1, backend.ApplyCalls);
    }

    [Fact]
    public void TryApplyAndRestart_NoDownloadCompletedYet_ReturnsFalseAndDoesNotApply()
    {
        var backend = new FakeUpdateBackend();
        var service = new UpdateService(backend, UpdateChannel.Stable);

        var applied = service.TryApplyAndRestart(userConfirmedRestart: true);

        Assert.False(applied);
        Assert.Equal(0, backend.ApplyCalls);
    }

    [Theory]
    [InlineData(UpdateChannel.Stable)]
    [InlineData(UpdateChannel.Beta)]
    public async Task CheckForUpdatesAsync_PropagatesConfiguredChannel(UpdateChannel channel)
    {
        var backend = new FakeUpdateBackend();
        var service = new UpdateService(backend, channel);

        await service.CheckForUpdatesAsync();

        Assert.Equal((UpdateChannel?)channel, backend.ReceivedChannel);
    }

    [Fact]
    public async Task DownloadUpdateAsync_MatchingChecksum_MarksUpdateReadyAndAllowsApply()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("intact-update-package-bytes");
        var expected = Convert.ToHexString(SHA256.HashData(payload));
        var backend = new FakeUpdateBackend { DownloadPayload = payload };
        var service = new UpdateService(backend, UpdateChannel.Stable);

        await service.DownloadUpdateAsync(expected);

        Assert.False(service.LastDownloadIntegrityFailed);
        Assert.Equal(expected, backend.ReceivedExpectedSha256);
        Assert.True(service.TryApplyAndRestart(userConfirmedRestart: true));
        Assert.Equal(1, backend.ApplyCalls);
    }

    [Fact]
    public async Task DownloadUpdateAsync_MismatchedChecksum_ReportsIntegrityFailureAndRefusesApply()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("tampered-or-truncated-package");
        var expected = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("the-package-we-expected")));
        var backend = new FakeUpdateBackend { DownloadPayload = payload };
        var service = new UpdateService(backend, UpdateChannel.Stable);

        await service.DownloadUpdateAsync(expected);

        Assert.True(service.LastDownloadIntegrityFailed);
        Assert.Equal(expected, backend.ReceivedExpectedSha256);
        Assert.False(service.TryApplyAndRestart(userConfirmedRestart: true));
        Assert.Equal(0, backend.ApplyCalls);
    }

    [Fact]
    public void Sha256Matches_FileMatchesExpectedHash_ReturnsTrue_RegardlessOfHexCase()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes("some-package-content"));
            var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

            Assert.True(VelopackUpdateBackend.Sha256Matches(path, expected));
            Assert.True(VelopackUpdateBackend.Sha256Matches(path, expected.ToLowerInvariant()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sha256Matches_FileHashDiffersFromExpected_ReturnsFalse()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes("tampered-payload"));
            var other = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("original-payload")));

            Assert.False(VelopackUpdateBackend.Sha256Matches(path, other));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sha256Matches_MissingFileOrBlankExpectedHash_FailsClosed()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes("anything"));
            Assert.False(VelopackUpdateBackend.Sha256Matches(path, string.Empty));
            Assert.False(VelopackUpdateBackend.Sha256Matches(path, "   "));
        }
        finally
        {
            File.Delete(path);
        }

        var expected = Convert.ToHexString(SHA256.HashData(new byte[4]));
        Assert.False(VelopackUpdateBackend.Sha256Matches(Path.Combine(Path.GetTempPath(), "missing-package.nupkg"), expected));
    }

    [Fact]
    public void BearerAuthFileDownloader_AttachesBearerTokenFromProvider()
    {
        var downloader = new BearerAuthFileDownloader(() => "node-access-token-123");

        using var client = CreateHttpClientForTest(downloader);

        Assert.NotNull(client.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("node-access-token-123", client.DefaultRequestHeaders.Authorization.Parameter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BearerAuthFileDownloader_ProviderReturnsNoToken_LeavesAuthorizationUnset(string? token)
    {
        var downloader = new BearerAuthFileDownloader(() => token);

        using var client = CreateHttpClientForTest(downloader);

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void BearerAuthFileDownloader_ReadsTokenFreshPerRequest_NotCachedAtConstruction()
    {
        var token = "first-token";
        var downloader = new BearerAuthFileDownloader(() => token);

        using var first = CreateHttpClientForTest(downloader);
        token = "rotated-token";
        using var second = CreateHttpClientForTest(downloader);

        Assert.Equal("first-token", first.DefaultRequestHeaders.Authorization!.Parameter);
        Assert.Equal("rotated-token", second.DefaultRequestHeaders.Authorization!.Parameter);
    }

    /// <summary>
    /// Invokes the protected CreateHttpClient override without making any network call, so the
    /// test proves the Authorization header is attached without needing a real feed endpoint.
    /// </summary>
    private static HttpClient CreateHttpClientForTest(BearerAuthFileDownloader downloader)
    {
        var method = typeof(BearerAuthFileDownloader).GetMethod(
            "CreateHttpClient",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (HttpClient)method!.Invoke(downloader, new object?[] { null, null, 1.0 })!;
    }

    private sealed class FakeUpdateBackend : IUpdateBackend
    {
        public UpdateChannel? ReceivedChannel { get; private set; }

        public int ApplyCalls { get; private set; }

        public string? ReceivedExpectedSha256 { get; private set; }

        public bool UpdateAvailable { get; set; } = true;

        public string? TargetVersion { get; set; } = "1.2.3";

        public TimeSpan CheckDelay { get; set; } = TimeSpan.Zero;

        public byte[]? DownloadPayload { get; set; }

        public bool DownloadResult { get; set; } = true;

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken)
        {
            ReceivedChannel = channel;

            if (CheckDelay > TimeSpan.Zero)
            {
                await Task.Delay(CheckDelay, cancellationToken).ConfigureAwait(false);
            }

            return new UpdateCheckResult(UpdateAvailable, TargetVersion);
        }

        public Task<bool> DownloadUpdatesAsync(string expectedSha256, CancellationToken cancellationToken)
        {
            ReceivedExpectedSha256 = expectedSha256;

            if (DownloadPayload is not null)
            {
                var actual = Convert.ToHexString(SHA256.HashData(DownloadPayload));
                return Task.FromResult(string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult(DownloadResult);
        }

        public void ApplyUpdatesAndRestart()
        {
            ApplyCalls++;
        }
    }
}