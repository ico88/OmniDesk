using System.Net;
using System.Net.NetworkInformation;

namespace OmniDesk.Agent.Windows;

public static class NetworkAdapterSelection
{
    public static bool IsActiveConnection(OperationalStatus status, NetworkInterfaceType type,
        IEnumerable<IPAddress> unicastAddresses, IEnumerable<IPAddress> gateways) =>
        status == OperationalStatus.Up && type != NetworkInterfaceType.Loopback &&
        unicastAddresses.Any(IsUsableAddress) && gateways.Any(IsUsableAddress);

    private static bool IsUsableAddress(IPAddress address) =>
        !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any);
}
