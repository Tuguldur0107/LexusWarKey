using System.Runtime.InteropServices;

namespace LexusWarKey.Windows;

/// <summary>Injects keystrokes with SendInput. Everything it sends is tagged with
/// <see cref="Signature"/> in dwExtraInfo so our own hook can recognise and ignore it —
/// without that, a remap would feed itself in an endless loop.</summary>
public static class InputSender
{
    public static readonly IntPtr Signature = new(0x4C57_4B31); // "LWK1"

    /// <summary>Returns false when Windows refused the keystroke — most commonly UIPI, when
    /// the game runs elevated and this app does not. Ignoring that used to make a macro type
    /// its text into nothing while looking successful.</summary>
    public static bool SendKey(int vk, bool keyDown)
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
        return NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>()) == 1;
    }

    public static bool TapKey(int vk)
    {
        var down = SendKey(vk, true);
        var up = SendKey(vk, false);
        return down && up;
    }

    /// <summary>Taps <paramref name="vk"/> while <paramref name="modifierVk"/> is held, and
    /// guarantees the modifier is released even if the tap throws. A modifier left logically
    /// down is worse than a missed keystroke: every later keypress reaches the game shifted.</summary>
    public static bool TapWithModifier(int modifierVk, int vk)
    {
        var ok = SendKey(modifierVk, true);
        try
        {
            return TapKey(vk) && ok;
        }
        finally
        {
            SendKey(modifierVk, false);
        }
    }

    /// <summary>Types a string as Unicode characters, so it does not depend on keyboard layout.
    /// Returns false when the batch was rejected or only partially delivered — the caller must
    /// then stop rather than press Enter on a half-typed line.</summary>
    public static bool TypeText(string text)
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
        if (inputs.Count == 0)
            return true;
        return NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>())
               == (uint)inputs.Count;
    }

    public static bool IsKeyHeld(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Clicks a point inside the game window by posting messages to it.
    /// The physical cursor never moves — but note PostMessage succeeding only means the
    /// message was queued, never that the game acted on it, which is why the caller treats
    /// this whole path as an experiment with a cursor-move fallback behind it.</summary>
    public static bool ClickInWindow(IntPtr hwnd, int screenX, int screenY, bool rightClick)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var point = new NativeMethods.POINT { X = screenX, Y = screenY };
        if (!NativeMethods.ScreenToClient(hwnd, ref point))
            return false;

        // A stale calibration — window moved, resolution changed, other monitor — converts to
        // a point outside the client area. The game discards such a message without a sound;
        // returning false instead hands the click to the fallback, which can still land it.
        if (point.X < 0 || point.Y < 0
            || !NativeMethods.GetClientRect(hwnd, out var rect)
            || point.X >= rect.Right || point.Y >= rect.Bottom)
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
