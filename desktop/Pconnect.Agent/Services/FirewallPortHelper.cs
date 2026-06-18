using System.Diagnostics;

namespace Pconnect.Agent.Services;

/// <summary>Best-effort inbound firewall rules so phones can reach LAN ports (requires admin once).</summary>
internal static class FirewallPortHelper
{
    private static int _attempted;

    public static void EnsureLanRulesOnce(int wsPort, int wssPort, int discoveryPort)
    {
        if (Interlocked.Exchange(ref _attempted, 1) != 0)
        {
            return;
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                return;
            }

            var exeQuoted = $"\"{exe}\"";
            AddRule($"Pconnect Agent WS ({wsPort})", "TCP", wsPort, exeQuoted);
            AddRule($"Pconnect Agent WSS ({wssPort})", "TCP", wssPort, exeQuoted);
            AddRule($"Pconnect Agent Discovery ({discoveryPort})", "UDP", discoveryPort, exeQuoted);
        }
        catch
        {
            // Non-admin or policy blocked; user can allow manually.
        }
    }

    private static void AddRule(string name, string protocol, int port, string programPath)
    {
        var args =
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} localport={port} program={programPath} profile=private enable=yes";
        RunNetsh(args);
    }

    private static void RunNetsh(string arguments)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        p.Start();
        p.WaitForExit(5000);
    }
}
