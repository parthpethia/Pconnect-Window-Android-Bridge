using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Pconnect.Agent.Services;

internal static class LanAddressHelper
{
    /// <summary>All routable LAN IPv4 addresses, best-first for display and QR.</summary>
    public static IReadOnlyList<string> GetLanIpv4CandidatesBestFirst()
    {
        var scored = new List<(int Score, string Ip)>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var score = ScoreInterface(ni);
            if (score < 0)
            {
                continue;
            }

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (IPAddress.IsLoopback(ua.Address))
                {
                    continue;
                }

                var ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (scored.Any(s => string.Equals(s.Ip, ip, StringComparison.Ordinal)))
                {
                    continue;
                }

                scored.Add((score, ip));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Ip, StringComparer.Ordinal)
            .Select(s => s.Ip)
            .ToList();
    }

    public static string? GetPreferredLanIpv4()
    {
        var list = GetLanIpv4CandidatesBestFirst();
        return list.Count > 0 ? list[0] : null;
    }

    private static int ScoreInterface(NetworkInterface ni)
    {
        var name = ni.Name;
        var desc = ni.Description;
        var blob = $"{name} {desc}";

        if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
        {
            return 10;
        }

        if (ContainsIgnoreCase(blob, "hyper-v", "vethernet", "virtualbox", "vmware", "virtual", "wsl", "docker", "npcap", "loopback"))
        {
            return 20;
        }

        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            return 100;
        }

        if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
        {
            return 90;
        }

        return 50;
    }

    private static bool ContainsIgnoreCase(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
