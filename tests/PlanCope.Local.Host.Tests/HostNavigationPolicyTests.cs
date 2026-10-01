using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

public sealed class HostNavigationPolicyTests
{
    private static readonly Uri ClientApp = new("https://host.plancope.local/index.html");

    [Theory]
    [InlineData("https://host.plancope.local/stats", "Allow")]
    [InlineData("https://HOST.plancope.local/other", "Allow")]
    [InlineData("https://example.org", "OpenExternal")]
    [InlineData("http://127.0.0.1:5055/api/stats/report.html", "OpenExternal")]
    [InlineData("mailto:teacher@example.org", "Cancel")]
    public void Decide_applies_top_frame_origin_policy(string destination, string expected)
    {
        Assert.Equal(Enum.Parse<HostNavigationAction>(expected), HostNavigationPolicy.Decide(ClientApp, destination, isTopFrame: true));
    }

    [Fact]
    public void Decide_allows_cross_origin_subframe_requests()
    {
        Assert.Equal(HostNavigationAction.Allow,
            HostNavigationPolicy.Decide(ClientApp, "https://example.org/frame", isTopFrame: false));
    }

    [Fact]
    public void IsClientAppOrigin_requires_matching_scheme_host_and_port()
    {
        Assert.True(HostNavigationPolicy.IsClientAppOrigin(ClientApp, "https://host.plancope.local/path"));
        Assert.False(HostNavigationPolicy.IsClientAppOrigin(ClientApp, "http://host.plancope.local/path"));
        Assert.False(HostNavigationPolicy.IsClientAppOrigin(ClientApp, "https://host.plancope.local:444/path"));
    }
}
