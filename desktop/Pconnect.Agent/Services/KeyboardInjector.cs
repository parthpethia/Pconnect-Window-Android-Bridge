using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Pconnect.Agent.Services;

internal class KeyboardInjector
{
    public static event Action? InputBlocked;
    public static void RaiseInputBlocked() => InputBlocked?.Invoke();

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const ushort VK_BACK = 0x08;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_L = 0x4C;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_A = 0x41;
    private const ushort VK_V = 0x56;

    // IMPORTANT: INPUT must match the Win32 INPUT struct size/layout.
    // On 64-bit Windows, sizeof(INPUT) is 40 bytes because the union must be
    // large enough for MOUSEINPUT (which contains a pointer-sized dwExtraInfo).
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private void SendInputInternal(INPUT[] inputs)
    {
        if (inputs == null || inputs.Length == 0) return;
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 5) // ERROR_ACCESS_DENIED
            {
                InputBlocked?.Invoke();
            }
        }
    }

    private static INPUT MouseAbsolute(double rx, double ry, uint flags, uint mouseData = 0)
    {
        int dx = (int)Math.Clamp(Math.Round(rx * 65535.0), 0, 65535);
        int dy = (int)Math.Clamp(Math.Round(ry * 65535.0), 0, 65535);
        return new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = mouseData,
                    dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE | flags,
                    time = 0,
                    dwExtraInfo = 0,
                }
            }
        };
    }

    public virtual void MoveMouseBy(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var inputs = new[]
        {
            Mouse(dx, dy, 0, MOUSEEVENTF_MOVE),
        };

        SendInputInternal(inputs);
    }

    public virtual void MoveMouseTo(int x, int y)
    {
        SetCursorPos(x, y);
    }

    public virtual void MoveMouseNormalized(double rx, double ry)
    {
        var inputs = new[]
        {
            MouseAbsolute(rx, ry, 0),
        };
        SendInputInternal(inputs);

        int sw = GetSystemMetrics(0); // SM_CXSCREEN
        int sh = GetSystemMetrics(1); // SM_CYSCREEN
        if (sw <= 0) sw = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920;
        if (sh <= 0) sh = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080;
        int x = (int)Math.Round(rx * sw);
        int y = (int)Math.Round(ry * sh);
        SetCursorPos(x, y);
    }

    public virtual void MoveAndClickNormalized(double rx, double ry, string button = "left")
    {
        button = (button ?? "left").Trim().ToLowerInvariant();
        uint downFlag = MOUSEEVENTF_LEFTDOWN;
        uint upFlag = MOUSEEVENTF_LEFTUP;
        if (button == "right")
        {
            downFlag = MOUSEEVENTF_RIGHTDOWN;
            upFlag = MOUSEEVENTF_RIGHTUP;
        }
        else if (button == "middle")
        {
            downFlag = MOUSEEVENTF_MIDDLEDOWN;
            upFlag = MOUSEEVENTF_MIDDLEUP;
        }

        int sw = GetSystemMetrics(0);
        int sh = GetSystemMetrics(1);
        if (sw <= 0) sw = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920;
        if (sh <= 0) sh = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080;
        int x = (int)Math.Round(rx * sw);
        int y = (int)Math.Round(ry * sh);
        SetCursorPos(x, y);

        SendInputInternal(new[]
        {
            MouseAbsolute(rx, ry, 0),
            MouseAbsolute(rx, ry, downFlag),
        });

        Thread.Sleep(15);

        SendInputInternal(new[]
        {
            MouseAbsolute(rx, ry, upFlag),
        });
    }

    public virtual void ScrollWheel(int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var inputs = new[]
        {
            Mouse(0, 0, unchecked((uint)wheelDelta), MOUSEEVENTF_WHEEL),
        };

        SendInputInternal(inputs);
    }

    public virtual void LeftDown() => MouseButton(MOUSEEVENTF_LEFTDOWN);
    public virtual void LeftUp() => MouseButton(MOUSEEVENTF_LEFTUP);
    public virtual void RightDown() => MouseButton(MOUSEEVENTF_RIGHTDOWN);
    public virtual void RightUp() => MouseButton(MOUSEEVENTF_RIGHTUP);
    public virtual void MiddleDown() => MouseButton(MOUSEEVENTF_MIDDLEDOWN);
    public virtual void MiddleUp() => MouseButton(MOUSEEVENTF_MIDDLEUP);

    public virtual void LeftClick()
    {
        SendInputInternal(new[] { Mouse(0, 0, 0, MOUSEEVENTF_LEFTDOWN) });
        Thread.Sleep(15);
        SendInputInternal(new[] { Mouse(0, 0, 0, MOUSEEVENTF_LEFTUP) });
    }

    public virtual void RightClick()
    {
        SendInputInternal(new[] { Mouse(0, 0, 0, MOUSEEVENTF_RIGHTDOWN) });
        Thread.Sleep(15);
        SendInputInternal(new[] { Mouse(0, 0, 0, MOUSEEVENTF_RIGHTUP) });
    }

    public virtual void MiddleClick()
    {
        SendInputInternal(new[] { Mouse(0, 0, 0, MOUSEEVENTF_MIDDLEDOWN) });
        Thread.Sleep(15);
        SendInputInternal(new[] { Mouse(0, 0, 0, MOUSEEVENTF_MIDDLEUP) });
    }

    public virtual void SendVk(ushort vk)
    {
        var inputs = new[]
        {
            Key(vk, '\0', 0),
            Key(vk, '\0', KEYEVENTF_KEYUP),
        };

        SendInputInternal(inputs);
    }

    public virtual void SendVkDown(ushort vk, bool extended)
    {
        var inputs = new[]
        {
            Key(vk, '\0', extended ? KEYEVENTF_EXTENDEDKEY : 0),
        };

        SendInputInternal(inputs);
    }

    public virtual void SendVkUp(ushort vk, bool extended)
    {
        var inputs = new[]
        {
            Key(vk, '\0', (extended ? KEYEVENTF_EXTENDEDKEY : 0) | KEYEVENTF_KEYUP),
        };

        SendInputInternal(inputs);
    }

    public virtual void SendWinL()
    {
        var inputs = new[]
        {
            Key(VK_LWIN, '\0', KEYEVENTF_EXTENDEDKEY),
            Key(VK_L, '\0', 0),
            Key(VK_L, '\0', KEYEVENTF_KEYUP),
            Key(VK_LWIN, '\0', KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP),
        };

        SendInputInternal(inputs);
    }

    public virtual void SendBackspaces(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var inputs = new INPUT[count * 2];
        var idx = 0;
        for (var i = 0; i < count; i++)
        {
            inputs[idx++] = Key(VK_BACK, '\0', 0);
            inputs[idx++] = Key(VK_BACK, '\0', KEYEVENTF_KEYUP);
        }

        SendInputInternal(inputs);
    }

    public virtual void SendUnicode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Each char becomes down+up.
        var inputs = new INPUT[text.Length * 2];
        var idx = 0;
        foreach (var ch in text)
        {
            inputs[idx++] = Key(0, ch, KEYEVENTF_UNICODE);
            inputs[idx++] = Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
        }

        SendInputInternal(inputs);
    }

    public virtual void SendCtrlA()
    {
        var inputs = new[]
        {
            Key(VK_CONTROL, '\0', 0),
            Key(VK_A, '\0', 0),
            Key(VK_A, '\0', KEYEVENTF_KEYUP),
            Key(VK_CONTROL, '\0', KEYEVENTF_KEYUP),
        };

        SendInputInternal(inputs);
    }

    public virtual void SendCtrlV()
    {
        var inputs = new[]
        {
            Key(VK_CONTROL, '\0', 0),
            Key(VK_V, '\0', 0),
            Key(VK_V, '\0', KEYEVENTF_KEYUP),
            Key(VK_CONTROL, '\0', KEYEVENTF_KEYUP),
        };

        SendInputInternal(inputs);
    }

    public virtual bool PasteTextSafely(string text)
    {
        bool result = false;
        var thread = new Thread(() =>
        {
            result = PasteTextSafelyInternal(text);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool joined = thread.Join(TimeSpan.FromSeconds(3)); // Bounded wait
        return joined && result;
    }

    private bool PasteTextSafelyInternal(string text)
    {
        IDataObject? backupData = null;
        Dictionary<string, object?>? savedFormats = null;
        const int retries = 3;

        // 1. Capture original clipboard with retries
        bool getSuccess = false;
        for (int i = 0; i < retries; i++)
        {
            try
            {
                backupData = Clipboard.GetDataObject();
                savedFormats = new Dictionary<string, object?>();
                if (backupData != null)
                {
                    string[] formats = backupData.GetFormats(false);
                    foreach (string format in formats)
                    {
                        try
                        {
                            savedFormats[format] = backupData.GetData(format);
                        }
                        catch
                        {
                            // ignore individual format read failure
                        }
                    }
                }
                getSuccess = true;
                break;
            }
            catch (Exception ex)
            {
                if (i == retries - 1)
                {
                    Console.WriteLine($"[KeyboardInjector] Clipboard get failed after retries: {ex.Message}");
                }
                Thread.Sleep(50);
            }
        }

        if (!getSuccess)
        {
            return false;
        }

        // 2. Set the clipboard to text with retries
        bool setSuccess = false;
        for (int i = 0; i < retries; i++)
        {
            try
            {
                Clipboard.SetText(text);
                setSuccess = true;
                break;
            }
            catch (Exception ex)
            {
                if (i == retries - 1)
                {
                    Console.WriteLine($"[KeyboardInjector] Clipboard set failed after retries: {ex.Message}");
                }
                Thread.Sleep(50);
            }
        }

        if (!setSuccess)
        {
            return false;
        }

        // 3. Send Ctrl+V
        try
        {
            SendCtrlV();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KeyboardInjector] SendCtrlV failed: {ex.Message}");
            return false;
        }

        // 4. Wait for application to process Ctrl+V
        Thread.Sleep(200);

        // 5. Restore the original clipboard with retries
        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (savedFormats != null && savedFormats.Count > 0)
                {
                    DataObject restoreObject = new DataObject();
                    foreach (var kvp in savedFormats)
                    {
                        restoreObject.SetData(kvp.Key, kvp.Value);
                    }
                    Clipboard.SetDataObject(restoreObject, true);
                }
                else
                {
                    Clipboard.Clear();
                }
                break;
            }
            catch (Exception ex)
            {
                if (i == retries - 1)
                {
                    Console.WriteLine($"[KeyboardInjector] Clipboard restore failed after retries: {ex.Message}");
                }
                Thread.Sleep(50);
            }
        }

        return true;
    }

    private static INPUT Key(ushort vk, char scan, uint flags)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                }
            }
        };
    }

    private void MouseButton(uint flags)
    {
        var inputs = new[]
        {
            Mouse(0, 0, 0, flags),
        };

        SendInputInternal(inputs);
    }

    private static INPUT Mouse(int dx, int dy, uint mouseData, uint flags)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = mouseData,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                }
            }
        };
    }
}
