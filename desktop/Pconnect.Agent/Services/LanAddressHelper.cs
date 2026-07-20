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

    internal static int ScoreInterface(NetworkInterface ni)
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

        var score = 50;

        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            score = 100;
        }
        else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
        {
            score = 90;
        }

        if (ni.GetIPProperties().GatewayAddresses.Count > 0)
        {
            score += 100;
        }

        return score;
    }

    internal static bool ForcePublicNetworkForTesting { get; set; }

    public static bool IsPublicNetworkProfile()
    {
        if (ForcePublicNetworkForTesting) return true;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles");
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using var profileKey = key.OpenSubKey(subKeyName);
                        if (profileKey != null)
                        {
                            // Windows NLM Categories: 0 = Public (Untrusted), 1 = Private (Trusted), 2 = Domain-Authenticated (Trusted)
                            var category = profileKey.GetValue("Category");
                            if (category is int catValue && catValue == 0) // Only Public network category (0) is treated as untrusted
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch { /* best-effort network category check */ }
        return false;
    }

    public static bool IsSameSubnet(IPAddress? remoteIp)
    {
        if (remoteIp == null) return false;
        if (IPAddress.IsLoopback(remoteIp)) return true;
        if (IsPublicNetworkProfile()) return false;

        if (remoteIp.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var remoteBytes = remoteIp.GetAddressBytes();

        var scoredInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(ni => (Score: ScoreInterface(ni), Interface: ni))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score);

        foreach (var item in scoredInterfaces)
        {
            var ni = item.Interface;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address))
                {
                    continue;
                }

                var localBytes = ua.Address.GetAddressBytes();
                var maskBytes = ua.IPv4Mask?.GetAddressBytes();

                if (maskBytes != null && maskBytes.Length == 4 && maskBytes[0] != 0)
                {
                    bool match = true;
                    for (int i = 0; i < 4; i++)
                    {
                        if ((remoteBytes[i] & maskBytes[i]) != (localBytes[i] & maskBytes[i]))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
                else
                {
                    // Fallback to /24 matching
                    if (remoteBytes[0] == localBytes[0] && remoteBytes[1] == localBytes[1] && remoteBytes[2] == localBytes[2])
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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

