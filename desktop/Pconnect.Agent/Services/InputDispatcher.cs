using System;

namespace Pconnect.Agent.Services;

internal sealed class InputDispatcher
{
    private readonly KeyboardInjector _keyboard;

    public InputDispatcher(KeyboardInjector keyboard)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
    }

    public void Dispatch(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 10)
        {
            return;
        }

        byte type = packet[0];
        int x = (packet[1] << 24) | (packet[2] << 16) | (packet[3] << 8) | packet[4];
        int y = (packet[5] << 24) | (packet[6] << 16) | (packet[7] << 8) | packet[8];
        byte extra = packet[9];

        switch (type)
        {
            case 0x01: // Mouse Move (relative)
                _keyboard.MoveMouseBy(x, y);
                break;

            case 0x02: // Mouse Button Down
                switch (extra)
                {
                    case 0:
                        _keyboard.LeftDown();
                        break;
                    case 1:
                        _keyboard.RightDown();
                        break;
                    case 2:
                        _keyboard.MiddleDown();
                        break;
                }
                break;

            case 0x03: // Mouse Button Up
                switch (extra)
                {
                    case 0:
                        _keyboard.LeftUp();
                        break;
                    case 1:
                        _keyboard.RightUp();
                        break;
                    case 2:
                        _keyboard.MiddleUp();
                        break;
                }
                break;

            case 0x04: // Keyboard Key Event
                ushort vk = (ushort)(x & 0xFFFF);
                int action = y;
                bool extended = extra == 1;

                if (action == 0) // press
                {
                    _keyboard.SendVk(vk);
                }
                else if (action == 1) // down
                {
                    _keyboard.SendVkDown(vk, extended);
                }
                else if (action == 2) // up
                {
                    _keyboard.SendVkUp(vk, extended);
                }
                break;

            case 0x05: // Scroll (vertical wheel)
                // y holds the wheel delta, e.g., 120 or -120
                _keyboard.ScrollWheel(y);
                break;

            case 0x06: // Mouse Move Absolute (Normalized if extra == 1, or pixels if extra == 0)
                if (extra == 1)
                {
                    double rx = Math.Clamp(x / 65535.0, 0.0, 1.0);
                    double ry = Math.Clamp(y / 65535.0, 0.0, 1.0);
                    _keyboard.MoveMouseNormalized(rx, ry);
                }
                else
                {
                    _keyboard.MoveMouseTo(x, y);
                }
                break;

            case 0x07: // Mouse Move & Click Normalized
                double crx = Math.Clamp(x / 65535.0, 0.0, 1.0);
                double cry = Math.Clamp(y / 65535.0, 0.0, 1.0);
                string btnStr = extra switch
                {
                    1 => "right",
                    2 => "middle",
                    _ => "left"
                };
                _keyboard.MoveAndClickNormalized(crx, cry, btnStr);
                break;
        }
    }
}
