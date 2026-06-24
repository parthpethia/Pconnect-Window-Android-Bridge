using System.Runtime.InteropServices;

namespace Pconnect.Agent.Services;

internal static class SafeStartupResolver
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int FastFailThreshold = 2;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);

    public static SafeStartupOptions Resolve(string[] args, int consecutiveAbnormalExits, bool pairedDevicesLoadFailed)
    {
        var reasons = new List<string>();
        var shiftHeld = IsShiftHeld();
        var ctrlHeld = IsCtrlHeld();

        foreach (var a in args)
        {
            if (string.Equals(a, "--safe-mode", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("cli--safe-mode");
            }
        }

        // Only trigger safe-mode if Shift is held and Ctrl is NOT held.
        // This avoids false positives when starting as administrator via Ctrl+Shift+Enter/Click.
        if (shiftHeld && !ctrlHeld)
        {
            reasons.Add("shift-held-at-launch");
        }

        if (consecutiveAbnormalExits >= FastFailThreshold)
        {
            reasons.Add($"abnormal-exit-streak>={FastFailThreshold}");
        }

        if (pairedDevicesLoadFailed)
        {
            reasons.Add("paired-devices-corrupt");
        }

        // Write diagnostic log to application folder
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pconnect-launch-diagnostics.log");
            var isElevated = false;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                isElevated = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }

            var logMsg = $"[{DateTimeOffset.Now:O}] Elevated={isElevated}, ShiftHeld={shiftHeld}, CtrlHeld={ctrlHeld}, AbnormalExitStreak={consecutiveAbnormalExits}, PairedLoadFailed={pairedDevicesLoadFailed}, Reasons=[{string.Join(", ", reasons)}]\r\n";
            File.AppendAllText(logPath, logMsg);
        }
        catch
        {
            // ignore logging exceptions
        }

        return reasons.Count > 0 ? SafeStartupOptions.Create(reasons) : SafeStartupOptions.Normal;
    }

    private static bool IsShiftHeld() => (GetAsyncKeyState(VkShift) & 0x8000) != 0;
    private static bool IsCtrlHeld() => (GetAsyncKeyState(VkControl) & 0x8000) != 0;
}
