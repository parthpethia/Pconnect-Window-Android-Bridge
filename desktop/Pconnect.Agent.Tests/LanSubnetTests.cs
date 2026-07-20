using System.Net;
using Pconnect.Agent.Services;
using Xunit;

namespace Pconnect.Agent.Tests;

public sealed class LanSubnetTests
{
    [Fact]
    public void IsSameSubnet_returns_true_for_loopback()
    {
        Assert.True(LanAddressHelper.IsSameSubnet(IPAddress.Loopback));
        Assert.True(LanAddressHelper.IsSameSubnet(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void IsSameSubnet_returns_false_for_null()
    {
        Assert.False(LanAddressHelper.IsSameSubnet(null));
    }

    [Fact]
    public void IsSameSubnet_returns_false_for_public_ip()
    {
        var publicIp = IPAddress.Parse("8.8.8.8");
        Assert.False(LanAddressHelper.IsSameSubnet(publicIp));
    }

    [Fact]
    public void IsSameSubnet_returns_false_when_public_network_profile_active()
    {
        LanAddressHelper.ForcePublicNetworkForTesting = true;
        try
        {
            var lanIp = IPAddress.Parse("192.168.1.50");
            Assert.False(LanAddressHelper.IsSameSubnet(lanIp));
        }
        finally
        {
            LanAddressHelper.ForcePublicNetworkForTesting = false;
        }
    }
}
