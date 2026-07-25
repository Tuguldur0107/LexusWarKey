using System.Runtime.InteropServices;

namespace LexusWarKey.Windows;

/// <summary>Injects keystrokes with SendInput. Everything it sends is tagged with
/// <see cref="Signature"/> in dwExtraInfo so our own hook can recognise and ignore it —
/// without that, a remap would feed itself in an endless loop.</summary>
public static class InputSender
{
    public static readonly IntPtr Signature = new(0x4C57_4B31); // "LWK1"

    public static void SendKey(int vk, bool keyDown)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    // A real keyboard reports a scan code alongside the virtual key. Games that
                    // read input through DirectInput look at the scan code, so leaving it zero
                    // makes a synthesised key invisible to them even though Windows accepts it.
                    wScan = (ushort)NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC),
                    dwFlags = keyDown ? 0 : NativeMethods.KEYEVENTF_KEYUP,
                    dwExtraInfo = Signature,
                },
            },
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void TapKey(int vk)
    {
        SendKey(vk, true);
        SendKey(vk, false);
    }

    /// <summary>Taps <paramref name="vk"/> while <paramref name="modifierVk"/> is held, and
    /// guarantees the modifier is released even if the tap throws. A modifier left logically
    /// down is worse than a missed keystroke: every later keypress reaches the game shifted.</summary>
    public static void TapWithModifier(int modifierVk, int vk)
    {
        SendKey(modifierVk, true);
        try
        {
            TapKey(vk);
        }
        finally
        {
            SendKey(modifierVk, false);
        }
    }

    /// <summary>Types a string as Unicode characters, so it does not depend on keyboard layout.</summary>
    public static void TypeText(string text)
    {
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            foreach (var up in new[] { false, true })
            {
                inputs.Add(new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = ch,
                            dwFlags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
                            dwExtraInfo = Signature,
                        },
                    },
                });
            }
        }
        if (inputs.Count > 0)
            NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static bool IsKeyHeld(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Clicks a point inside the game window by posting messages to it.
    /// The physical cursor never moves, so nothing is stolen from the player mid-fight.
    /// Returns false when the window rejects the post, so the caller can fall back.</summary>
    public static bool ClickInWindow(IntPtr hwnd, int screenX, int screenY, bool rightClick)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var point = new NativeMethods.POINT { X = screenX, Y = screenY };
        if (!NativeMethods.ScreenToClient(hwnd, ref point))
            return false;

        var lParam = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
        var down = rightClick ? NativeMethods.WM_RBUTTONDOWN : NativeMethods.WM_LBUTTONDOWN;
        var up = rightClick ? NativeMethods.WM_RBUTTONUP : NativeMethods.WM_LBUTTONUP;
        var button = (IntPtr)(rightClick ? NativeMethods.MK_RBUTTON : NativeMethods.MK_LBUTTON);

        var ok = NativeMethods.PostMessage(hwnd, NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, lParam);
        ok &= NativeMethods.PostMessage(hwnd, (uint)down, button, lParam);
        Thread.Sleep(8);
        ok &= NativeMethods.PostMessage(hwnd, (uint)up, IntPtr.Zero, lParam);
        return ok;
    }

    /// <summary>Fallback that really does move the cursor and puts it back. Only used when
    /// the user explicitly opts in, because a cursor jump during a fight is disruptive.</summary>
    public static void ClickAt(int x, int y, bool rightClick)
    {
        NativeMethods.GetCursorPos(out var original);
        try
        {
            NativeMethods.SetCursorPos(x, y);
            Thread.Sleep(12); // let the game notice the cursor before the button goes down

            var down = rightClick ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_LEFTDOWN;
            var up = rightClick ? NativeMethods.MOUSEEVENTF_RIGHTUP : NativeMethods.MOUSEEVENTF_LEFTUP;
            SendMouse(down);
            Thread.Sleep(12);
            SendMouse(up);
            Thread.Sleep(12);
        }
        finally
        {
            NativeMethods.SetCursorPos(original.X, original.Y);
        }
    }

    private static void SendMouse(uint flags)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT { dwFlags = flags, dwExtraInfo = Signature },
            },
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
