using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PlanCope.Local.Host.Services;

public sealed record LanAddressCandidate(IPAddress Address, NetworkInterfaceType InterfaceType, bool HasGateway);

public static class LanAddressResolver
{
    public static string GetLocalIpAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network =>
            {
                var properties = network.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(IPAddress.Any));
                return properties.UnicastAddresses.Select(address =>
                    new LanAddressCandidate(address.Address, network.NetworkInterfaceType, hasGateway));
            });

        return SelectAddress(candidates)?.ToString() ?? "127.0.0.1";
    }

    public static IPAddress? SelectAddress(IEnumerable<LanAddressCandidate> candidates) => candidates
        .Where(candidate => candidate.InterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
        .Where(candidate => candidate.Address.AddressFamily == AddressFamily.InterNetwork)
        .Where(candidate => !IPAddress.IsLoopback(candidate.Address) && !candidate.Address.Equals(IPAddress.Any))
        .Where(candidate => candidate.Address.GetAddressBytes() is var bytes && !(bytes[0] == 169 && bytes[1] == 254))
        // A gateway helps choose between adapters, but is not required for an offline LAN.
        .OrderBy(candidate => candidate.InterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet ? 0 : 1)
        .ThenBy(candidate => candidate.HasGateway ? 0 : 1)
        .ThenBy(candidate => candidate.InterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
        .Select(candidate => candidate.Address)
        .FirstOrDefault();
}
