using System.Diagnostics;

namespace Pconnect.Agent.Services;

internal static class ToastNotificationHelper
{
    private static NotifyIcon? _trayIcon;
    private static string? _lastCompletedFile;

    public static void Initialize(NotifyIcon trayIcon)
    {
        _trayIcon = trayIcon;
        if (_trayIcon != null)
        {
            _trayIcon.BalloonTipClicked -= OnBalloonTipClicked;
            _trayIcon.BalloonTipClicked += OnBalloonTipClicked;
        }
    }

    public static void ShowTransferStart(string filename, string? senderDevice)
    {
        var sender = string.IsNullOrWhiteSpace(senderDevice) ? "remote device" : senderDevice;
        _trayIcon?.ShowBalloonTip(3000, "Pconnect File Transfer", $"Receiving {filename} from {sender}...", ToolTipIcon.Info);
    }

    public static void ShowTransferComplete(string targetFilePath, string? senderDevice)
    {
        _lastCompletedFile = targetFilePath;
        var filename = Path.GetFileName(targetFilePath);
        _trayIcon?.ShowBalloonTip(5000, "Pconnect Transfer Completed", $"Received {filename}. Click to open file in folder.", ToolTipIcon.Info);
    }

    private static void OnBalloonTipClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastCompletedFile) && File.Exists(_lastCompletedFile))
        {
            try
            {
                // Open Windows Explorer selecting the received file
                Process.Start("explorer.exe", $"/select,\"{_lastCompletedFile}\"");
            }
            catch { /* ignore */ }
        }
    }
}
