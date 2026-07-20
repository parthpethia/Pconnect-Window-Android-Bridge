using System.Runtime.Versioning;
using System.Text;
using Pconnect.Agent.Services;

namespace Pconnect.Agent;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int relaunchFromPid = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--relaunch-from-pid", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                int.TryParse(args[i + 1], out var pid))
            {
                relaunchFromPid = pid;
                break;
            }
        }

        if (relaunchFromPid > 0)
        {
            try
            {
                using var parent = System.Diagnostics.Process.GetProcessById(relaunchFromPid);
                if (!parent.HasExited)
                {
                    parent.WaitForExit(3000);
                }
            }
            catch
            {
                // Process may have already exited
            }
        }

        using var singleInstanceMutex = new Mutex(initiallyOwned: true, name: "Local\\Pconnect.Agent", createdNew: out var createdNew);
        if (!createdNew && relaunchFromPid == 0)
        {
            // Second launch: ask the already-running instance to show the dashboard.
            // This lets users reopen the UI to start/stop the server.
            if (!SingleInstanceIpc.TrySendShowDashboard())
            {
                MessageBox.Show(
                    "Pconnect Agent is already running. Check the tray (system notification area).",
                    "Pconnect",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            Environment.ExitCode = 0;
            return;
        }
        else if (!createdNew && relaunchFromPid > 0)
        {
            bool acquired = false;
            try
            {
                acquired = singleInstanceMutex.WaitOne(3000);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                MessageBox.Show(
                    "Failed to release previous Pconnect Agent process during admin elevation.",
                    "Pconnect",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                Environment.ExitCode = 1;
                return;
            }
        }

        var abnormalExitStreak = StartupCrashTracker.BeginRun();
        Application.ApplicationExit += (_, _) => StartupCrashTracker.MarkCleanExit();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogFatal(ex);
            }
        };

        Application.ThreadException += (_, e) => LogFatal(e.Exception);

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayAppContext(abnormalExitStreak));
        }
        catch (Exception ex)
        {
            LogFatal(ex);
            MessageBox.Show(ex.ToString(), "Pconnect Agent crashed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    private static void LogFatal(Exception ex)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "pconnect-agent.log");
            var sb = new StringBuilder();
            sb.AppendLine("---- Pconnect.Agent fatal ----");
            sb.AppendLine(DateTimeOffset.Now.ToString("O"));
            sb.AppendLine(ex.ToString());
            sb.AppendLine();
            File.AppendAllText(path, sb.ToString());
            CrashLog.Write(ex, null);
        }
        catch
        {
            // ignored
        }
    }
}
