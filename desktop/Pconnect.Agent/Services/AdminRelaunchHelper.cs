using System.Diagnostics;
using System.IO;
using System.Reflection;
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

            string fileName = exePath;
            string arguments;

            var exeName = Path.GetFileName(exePath);
            if (exeName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                exeName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
#pragma warning disable IL3000 // Assembly.Location is only evaluated when running via dotnet.exe CLI
                var entryAssembly = Assembly.GetEntryAssembly()?.Location;
#pragma warning restore IL3000
                if (!string.IsNullOrEmpty(entryAssembly) && File.Exists(entryAssembly))
                {
                    arguments = $"exec \"{entryAssembly}\" --relaunch-from-pid {Environment.ProcessId}";
                }
                else
                {
                    arguments = $"--relaunch-from-pid {Environment.ProcessId}";
                }
            }
            else
            {
                arguments = $"--relaunch-from-pid {Environment.ProcessId}";
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
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

