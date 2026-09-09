using System.Diagnostics;
using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

public sealed class UpdateServiceTests
{
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

        await service.DownloadUpdateAsync();

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

    private sealed class FakeUpdateBackend : IUpdateBackend
    {
        public UpdateChannel? ReceivedChannel { get; private set; }

        public int ApplyCalls { get; private set; }

        public bool UpdateAvailable { get; set; } = true;

        public string? TargetVersion { get; set; } = "1.2.3";

        public TimeSpan CheckDelay { get; set; } = TimeSpan.Zero;

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken)
        {
            ReceivedChannel = channel;

            if (CheckDelay > TimeSpan.Zero)
            {
                await Task.Delay(CheckDelay, cancellationToken).ConfigureAwait(false);
            }

            return new UpdateCheckResult(UpdateAvailable, TargetVersion);
        }

        public Task DownloadUpdatesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void ApplyUpdatesAndRestart()
        {
            ApplyCalls++;
        }
    }
}