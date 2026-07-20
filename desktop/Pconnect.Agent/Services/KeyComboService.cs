using System.Runtime.InteropServices;

namespace Pconnect.Agent.Services;

/// <summary>
/// Executes keyboard shortcut combos from named key arrays (e.g. ["ctrl", "shift", "esc"]).
/// All modifier keys are pressed down, then the final key is pressed + released,
/// and finally all modifiers are released in reverse order.
/// </summary>
internal static class KeyComboService
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // IMPORTANT: The INPUT struct size must match Win32's sizeof(INPUT) exactly.
    // On x64, the InputUnion must be sized to its largest member (MOUSEINPUT = 28 bytes),
    // making INPUT 40 bytes total. If the union only contains KEYBDINPUT (24 bytes),
    // Marshal.SizeOf<INPUT>() returns a smaller value and SendInput silently fails
    // because cbSize doesn't match the expected stride.
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

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern nuint GetMessageExtraInfo();

    // Modifier keys
    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctrl", "control", "lctrl", "rctrl",
        "shift", "lshift", "rshift",
        "alt", "lalt", "ralt", "option",
        "win", "windows", "lwin", "rwin", "meta", "super", "cmd", "command"
    };

    /// <summary>
    /// Executes a key combo from an array of named keys.
    /// Returns true if all keys were recognized and sent.
    /// </summary>
    public static bool Execute(IReadOnlyList<string> keys)
    {
        if (keys == null || keys.Count == 0) return false;

        // Separate modifiers and action keys
        var modifierVks = new List<(ushort vk, bool extended)>();
        var actionVks = new List<(ushort vk, bool extended)>();

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var trimmed = key.Trim();
            var resolved = ResolveKey(trimmed.ToLowerInvariant());
            if (resolved == null) return false;

            if (Modifiers.Contains(trimmed))
            {
                modifierVks.Add(resolved.Value);
            }
            else
            {
                actionVks.Add(resolved.Value);
            }
        }

        if (modifierVks.Count == 0 && actionVks.Count == 0) return false;

        // 1. Press modifier keys down first
        if (modifierVks.Count > 0)
        {
            var modDownInputs = new INPUT[modifierVks.Count];
            for (int i = 0; i < modifierVks.Count; i++)
            {
                var (vk, ext) = modifierVks[i];
                modDownInputs[i] = KeyDown(vk, ext);
            }

            if (!SendBatch(modDownInputs)) return false;

            // Small delay for OS Shell (e.g. explorer.exe AltTab handler) to register modifier state
            Thread.Sleep(15);
        }

        // 2. Press and release action keys
        if (actionVks.Count > 0)
        {
            var actionInputs = new INPUT[actionVks.Count * 2];
            int idx = 0;
            foreach (var (vk, ext) in actionVks)
            {
                actionInputs[idx++] = KeyDown(vk, ext);
                actionInputs[idx++] = KeyUp(vk, ext);
            }

            if (!SendBatch(actionInputs))
            {
                // Clean up modifiers on error
                ReleaseModifiers(modifierVks);
                return false;
            }

            // Small pause before releasing modifiers if modifiers were active
            if (modifierVks.Count > 0)
            {
                Thread.Sleep(15);
            }
        }

        // 3. Release modifier keys in reverse order
        if (modifierVks.Count > 0)
        {
            ReleaseModifiers(modifierVks);
        }

        return true;
    }

    private static bool SendBatch(INPUT[] inputs)
    {
        if (inputs == null || inputs.Length == 0) return true;
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine($"[KeyComboService] SendInput failed: sent=0, error={err}");
            if (err == 5) // ERROR_ACCESS_DENIED (UIPI)
            {
                KeyboardInjector.RaiseInputBlocked();
            }
            return false;
        }

        return true;
    }

    private static void ReleaseModifiers(List<(ushort vk, bool extended)> modifierVks)
    {
        var modUpInputs = new INPUT[modifierVks.Count];
        int idx = 0;
        for (int i = modifierVks.Count - 1; i >= 0; i--)
        {
            var (vk, ext) = modifierVks[i];
            modUpInputs[idx++] = KeyUp(vk, ext);
        }
        SendBatch(modUpInputs);
    }

    internal static (ushort vk, bool extended)? ResolveKey(string key)
    {
        return key switch
        {
            // Modifiers
            "ctrl" or "control" or "lctrl" => (0x11, false),    // VK_CONTROL
            "rctrl" => (0xA5, true),                            // VK_RCONTROL
            "shift" or "lshift" => (0x10, false),               // VK_SHIFT
            "rshift" => (0xA1, false),                          // VK_RSHIFT
            "alt" or "lalt" or "option" => (0x12, false),       // VK_MENU
            "ralt" => (0xA5, true),                             // VK_RMENU
            "win" or "windows" or "lwin" or "meta" or "super" or "cmd" or "command" => (0x5B, true), // VK_LWIN
            "rwin" => (0x5C, true),                             // VK_RWIN

            // Navigation
            "enter" or "return" => (0x0D, false),
            "tab" => (0x09, false),
            "esc" or "escape" => (0x1B, false),
            "space" => (0x20, false),
            "backspace" => (0x08, false),
            "delete" or "del" => (0x2E, true),
            "insert" or "ins" => (0x2D, true),
            "home" => (0x24, true),
            "end" => (0x23, true),
            "pageup" or "pgup" => (0x21, true),
            "pagedown" or "pgdn" => (0x22, true),

            // Arrow keys
            "up" => (0x26, true),
            "down" => (0x28, true),
            "left" => (0x25, true),
            "right" => (0x27, true),

            // Function keys
            "f1" => (0x70, false),
            "f2" => (0x71, false),
            "f3" => (0x72, false),
            "f4" => (0x73, false),
            "f5" => (0x74, false),
            "f6" => (0x75, false),
            "f7" => (0x76, false),
            "f8" => (0x77, false),
            "f9" => (0x78, false),
            "f10" => (0x79, false),
            "f11" => (0x7A, false),
            "f12" => (0x7B, false),

            // Special
            "printscreen" or "prtsc" => (0x2C, false),
            "scrolllock" => (0x91, false),
            "pause" => (0x13, false),
            "capslock" => (0x14, false),
            "numlock" => (0x90, false),

            // Volume & Media keys
            "vol_up" or "volume_up" or "volup" or "volumeup" or "volume_up_key" or "vol+" => (0xAF, true), // VK_VOLUME_UP
            "vol_down" or "volume_down" or "voldown" or "volumedown" or "volume_down_key" or "vol-" => (0xAE, true), // VK_VOLUME_DOWN
            "mute" or "vol_mute" or "volume_mute" or "volumemute" => (0xAD, true), // VK_VOLUME_MUTE
            "play_pause" or "play" or "pause" or "playpause" => (0xB3, true), // VK_MEDIA_PLAY_PAUSE
            "next" or "next_track" or "nexttrack" => (0xB0, true), // VK_MEDIA_NEXT_TRACK
            "prev" or "previous" or "prev_track" or "prevtrack" => (0xB1, true), // VK_MEDIA_PREV_TRACK
            "stop" => (0xB2, true), // VK_MEDIA_STOP

            // Letters a-z → VK 0x41-0x5A
            var s when s.Length == 1 && s[0] >= 'a' && s[0] <= 'z' =>
                ((ushort)(0x41 + (s[0] - 'a')), false),

            // Digits 0-9 → VK 0x30-0x39
            var s when s.Length == 1 && s[0] >= '0' && s[0] <= '9' =>
                ((ushort)(0x30 + (s[0] - '0')), false),

            // Punctuation
            ";" or "semicolon" => (0xBA, false),
            "=" or "equals" or "plus" => (0xBB, false),
            "," or "comma" => (0xBC, false),
            "-" or "minus" or "hyphen" => (0xBD, false),
            "." or "period" => (0xBE, false),
            "/" or "slash" => (0xBF, false),
            "`" or "backtick" or "tilde" => (0xC0, false),
            "[" or "lbracket" => (0xDB, false),
            "\\" or "backslash" => (0xDC, false),
            "]" or "rbracket" => (0xDD, false),
            "'" or "quote" => (0xDE, false),

            _ => null,
        };
    }

    private static INPUT KeyDown(ushort vk, bool extended)
    {
        var scan = (ushort)MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = extended ? KEYEVENTF_EXTENDEDKEY : 0,
                    time = 0,
                    dwExtraInfo = GetMessageExtraInfo(),
                }
            }
        };
    }

    private static INPUT KeyUp(ushort vk, bool extended)
    {
        var scan = (ushort)MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = GetMessageExtraInfo(),
                }
            }
        };
    }
}
