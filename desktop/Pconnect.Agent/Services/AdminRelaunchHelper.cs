using System.Diagnostics;
using System.Runtime.Versioning;

namespace Pconnect.Agent.Services;

[SupportedOSPlatform("windows")]
internal static class AdminRelaunchHelper
{
    public static void RelaunchAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--relaunch-from-pid {Environment.ProcessId}",
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            Environment.Exit(0);
        }
        catch
        {
            // User cancelled or rejected UAC prompt
        }
    }
}
