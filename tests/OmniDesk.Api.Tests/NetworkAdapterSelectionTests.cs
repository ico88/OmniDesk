using System.Net;
using System.Net.NetworkInformation;
using OmniDesk.Agent.Windows;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class NetworkAdapterSelectionTests
{
    [Fact]
    public void Includes_only_up_interfaces_with_unicast_address_and_gateway()
    {
        Assert.True(NetworkAdapterSelection.IsActiveConnection(OperationalStatus.Up, NetworkInterfaceType.Ethernet,
            [IPAddress.Parse("192.168.1.160"), IPAddress.Parse("fe80::1")], [IPAddress.Parse("192.168.1.1")]));
        Assert.True(NetworkAdapterSelection.IsActiveConnection(OperationalStatus.Up, NetworkInterfaceType.Wireless80211,
            [IPAddress.Parse("10.0.0.25")], [IPAddress.Parse("10.0.0.1")]));

        // Host-only virtual adapters and NDIS filter layers have no default route or no IP address.
        Assert.False(NetworkAdapterSelection.IsActiveConnection(OperationalStatus.Up, NetworkInterfaceType.Ethernet,
            [IPAddress.Parse("192.168.56.1")], []));
        Assert.False(NetworkAdapterSelection.IsActiveConnection(OperationalStatus.Up, NetworkInterfaceType.Ethernet,
            [], [IPAddress.Parse("192.168.1.1")]));
        Assert.False(NetworkAdapterSelection.IsActiveConnection(OperationalStatus.Down, NetworkInterfaceType.Ethernet,
            [IPAddress.Parse("192.168.1.160")], [IPAddress.Parse("192.168.1.1")]));
        Assert.False(NetworkAdapterSelection.IsActiveConnection(OperationalStatus.Up, NetworkInterfaceType.Loopback,
            [IPAddress.Loopback], [IPAddress.Loopback]));
    }
}
