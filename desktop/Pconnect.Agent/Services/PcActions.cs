using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pconnect.Agent.Services;

internal sealed class PcActions
{
    private readonly KeyboardInjector _keyboard;

    public PcActions() : this(new KeyboardInjector())
    {
    }

    internal PcActions(KeyboardInjector keyboard)
    {
        _keyboard = keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    public bool Lock()
    {
        // Prefer the OS API. In some environments this may return false (policy/session issues).
        if (LockWorkStation())
        {
            return true;
        }

        // Fallback #1: rundll32 invocation of the same API.
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = "user32.dll,LockWorkStation",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is not null)
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        // Fallback #2: simulate Win+L.
        try
        {
            _keyboard.SendWinL();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void TypeText(int backspaces, string text)
    {
        if (backspaces > 0)
        {
            _keyboard.SendBackspaces(backspaces);
        }

        if (!string.IsNullOrEmpty(text))
        {
            _keyboard.SendUnicode(text);
        }
    }

    public void ReplaceAllText(string text)
    {
        _keyboard.SendCtrlA();
        bool pasteSuccess = false;
        try
        {
            pasteSuccess = _keyboard.PasteTextSafely(text);
        }
        catch
        {
            pasteSuccess = false;
        }

        if (!pasteSuccess)
        {
            _keyboard.SendBackspaces(1); // clear the selection
            if (!string.IsNullOrEmpty(text))
            {
                _keyboard.SendUnicode(text);
            }
        }
    }

    public void Launch(string command, IReadOnlyList<string>? args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = true,
        };

        if (args is not null)
        {
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
        }

        Process.Start(psi);
    }

    public record DisplayInfo(
        int Index,
        string Name,
        int DeviceIndex,
        int X,
        int Y,
        int Width,
        int Height,
        bool IsPrimary
    );

    public IReadOnlyList<DisplayInfo> GetMonitors()
    {
        var screens = Screen.AllScreens;
        var list = new List<DisplayInfo>();
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            string name = string.IsNullOrWhiteSpace(s.DeviceName) ? $"Display {i + 1}" : s.DeviceName.Replace(@"\.\", "").Replace(@"\", "");
            if (s.Primary) name += " (Primary)";
            list.Add(new DisplayInfo(
                Index: i,
                Name: name,
                DeviceIndex: i,
                X: s.Bounds.X,
                Y: s.Bounds.Y,
                Width: s.Bounds.Width,
                Height: s.Bounds.Height,
                IsPrimary: s.Primary
            ));
        }
        return list;
    }

    public void MouseMove(int dx, int dy)
    {
        _keyboard.MoveMouseBy(dx, dy);
    }

    public void MoveMouseTo(int x, int y)
    {
        _keyboard.MoveMouseTo(x, y);
    }

    public void MoveMouseNormalized(double rx, double ry, int displayIndex = 0)
    {
        _keyboard.MoveMouseNormalized(rx, ry, displayIndex);
    }

    public void MoveAndClickNormalized(double rx, double ry, string button = "left", int displayIndex = 0)
    {
        _keyboard.MoveAndClickNormalized(rx, ry, button, displayIndex);
    }

    public void MouseScroll(int wheelDelta)
    {
        _keyboard.ScrollWheel(wheelDelta);
    }

    public void MouseButton(string button, string action)
    {
        // Normalize
        button = button.Trim().ToLowerInvariant();
        action = action.Trim().ToLowerInvariant();

        if (action == "click")
        {
            switch (button)
            {
                case "left":
                    _keyboard.LeftClick();
                    return;
                case "right":
                    _keyboard.RightClick();
                    return;
                case "middle":
                    _keyboard.MiddleClick();
                    return;
            }
        }

        if (action == "down")
        {
            switch (button)
            {
                case "left":
                    _keyboard.LeftDown();
                    return;
                case "right":
                    _keyboard.RightDown();
                    return;
                case "middle":
                    _keyboard.MiddleDown();
                    return;
            }
        }

        if (action == "up")
        {
            switch (button)
            {
                case "left":
                    _keyboard.LeftUp();
                    return;
                case "right":
                    _keyboard.RightUp();
                    return;
                case "middle":
                    _keyboard.MiddleUp();
                    return;
            }
        }
    }

    public void Key(ushort vk, string action, bool extended)
    {
        action = action.Trim().ToLowerInvariant();

        switch (action)
        {
            case "press":
                _keyboard.SendVk(vk);
                break;
            case "down":
                _keyboard.SendVkDown(vk, extended);
                break;
            case "up":
                _keyboard.SendVkUp(vk, extended);
                break;
        }
    }

    public bool SetVolume(int level)
    {
        return SystemVolume.TrySetPercent(level);
    }

    public bool SetBrightness(int level)
    {
        return SystemBrightness.TrySetPercent(level);
    }

    public bool Shutdown()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/s /t 0",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return p is not null;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public bool Sleep()
    {
        try
        {
            return SetSuspendState(false, false, false);
        }
        catch
        {
            return false;
        }
    }

    public bool Restart()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 0",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return p is not null;
        }
        catch
        {
            return false;
        }
    }

    public void TaskView()
    {
        KeyComboService.Execute(new[] { "Win", "Tab" });
    }

    public void ShowDesktop()
    {
        KeyComboService.Execute(new[] { "Win", "D" });
    }

    public void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }

    public bool ToggleMuteAudio()
    {
        return MediaKeyService.Send("volume_mute");
    }

    public void SetClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Clipboard.SetText requires STA thread; WebSocket handler runs on MTA.
        // Marshal to a dedicated STA thread to avoid ExternalException.
        var thread = new Thread(() =>
        {
            try { Clipboard.SetText(text); }
            catch { /* Fail silently - clipboard may be in use */ }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(2)); // Bounded wait
    }
}
