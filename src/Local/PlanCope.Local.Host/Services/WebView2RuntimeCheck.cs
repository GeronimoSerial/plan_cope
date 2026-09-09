namespace PlanCope.Local.Host.Services;

/// <summary>
/// Detects whether the WebView2 Evergreen Runtime is installed. The production
/// implementation queries Microsoft.Web.WebView2.Core.CoreWebView2Environment's
/// GetAvailableBrowserVersionString(), which throws or returns null/empty when no
/// runtime is installed.
/// </summary>
public interface IWebView2RuntimeDetector
{
    bool IsInstalled();
}

public sealed class WebView2RuntimeDetector : IWebView2RuntimeDetector
{
    public bool IsInstalled()
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (Exception)
        {
            // CoreWebView2Environment throws WebView2RuntimeNotFoundException (or similar)
            // when no Evergreen runtime is installed.
            return false;
        }
    }
}

/// <summary>
/// Checks for the WebView2 Evergreen Runtime and triggers its installation when
/// missing. The install trigger is injected so it can be a real bootstrapper launch
/// in production and a no-op/spy in tests.
/// </summary>
public sealed class WebView2RuntimeCheck
{
    private readonly IWebView2RuntimeDetector _detector;
    private readonly Action _triggerInstall;

    public WebView2RuntimeCheck(IWebView2RuntimeDetector detector, Action triggerInstall)
    {
        _detector = detector;
        _triggerInstall = triggerInstall;
    }

    /// <summary>
    /// Returns true if the runtime is already installed. Otherwise triggers
    /// installation and returns false.
    /// </summary>
    public bool EnsureInstalled()
    {
        if (_detector.IsInstalled())
        {
            return true;
        }

        _triggerInstall();
        return false;
    }
}