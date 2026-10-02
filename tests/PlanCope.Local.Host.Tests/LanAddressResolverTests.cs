using System.Net;
using System.Net.NetworkInformation;
using PlanCope.Local.Host.Services;
using Xunit;

namespace PlanCope.Local.Host.Tests;

public sealed class LanAddressResolverTests
{
    [Fact]
    public void OfflineWifiWithoutGateway_IsShareable()
    {
        var result = LanAddressResolver.SelectAddress([
            Candidate("127.0.0.1", NetworkInterfaceType.Loopback),
            Candidate("192.168.1.42", NetworkInterfaceType.Wireless80211)
        ]);

        Assert.Equal(IPAddress.Parse("192.168.1.42"), result);
    }

    [Fact]
    public void PhysicalOfflineLan_IsPreferredOverVirtualAdapterWithGateway()
    {
        var result = LanAddressResolver.SelectAddress([
            Candidate("172.20.0.1", NetworkInterfaceType.Unknown, true),
            Candidate("192.168.0.12", NetworkInterfaceType.Ethernet)
        ]);

        Assert.Equal(IPAddress.Parse("192.168.0.12"), result);
    }

    [Fact]
    public void GatewayBreaksTieBetweenPhysicalAdapters()
    {
        var result = LanAddressResolver.SelectAddress([
            Candidate("192.168.2.2", NetworkInterfaceType.Wireless80211),
            Candidate("192.168.1.2", NetworkInterfaceType.Ethernet, true)
        ]);

        Assert.Equal(IPAddress.Parse("192.168.1.2"), result);
    }

    [Fact]
    public void UnusableAddresses_DoNotBecomeShareLinks()
    {
        Assert.Null(LanAddressResolver.SelectAddress([
            Candidate("127.0.0.1", NetworkInterfaceType.Ethernet),
            Candidate("169.254.1.2", NetworkInterfaceType.Wireless80211),
            Candidate("::1", NetworkInterfaceType.Ethernet),
            Candidate("0.0.0.0", NetworkInterfaceType.Ethernet),
            Candidate("10.0.0.2", NetworkInterfaceType.Tunnel)
        ]));
    }

    private static LanAddressCandidate Candidate(string address, NetworkInterfaceType type, bool gateway = false) =>
        new(IPAddress.Parse(address), type, gateway);
}
