namespace PlanCope.Local.Host.Tests;

using Xunit;
using PlanCope.Local.Host.Services;

public sealed class FakeWebView2RuntimeDetector : IWebView2RuntimeDetector
{
    private readonly bool _isInstalled;

    public FakeWebView2RuntimeDetector(bool isInstalled)
    {
        _isInstalled = isInstalled;
    }

    public bool IsInstalled() => _isInstalled;
}

public sealed class WebView2RuntimeCheckTests
{
    [Fact]
    public void EnsureInstalled_WhenRuntimeInstalled_ReturnsTrueAndDoesNotTriggerInstall()
    {
        var detector = new FakeWebView2RuntimeDetector(isInstalled: true);
        var installTriggered = false;
        var check = new WebView2RuntimeCheck(detector, () => installTriggered = true);

        var result = check.EnsureInstalled();

        Assert.True(result);
        Assert.False(installTriggered);
    }

    [Fact]
    public void EnsureInstalled_WhenRuntimeMissing_ReturnsFalseAndTriggersInstallExactlyOnce()
    {
        var detector = new FakeWebView2RuntimeDetector(isInstalled: false);
        var triggerCount = 0;
        var check = new WebView2RuntimeCheck(detector, () => triggerCount++);

        var result = check.EnsureInstalled();

        Assert.False(result);
        Assert.Equal(1, triggerCount);
    }
}