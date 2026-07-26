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

    /// <summary>Clicks by moving the real cursor to the point and back — about 20ms. Posting
    /// messages to the game window was tried first and looked perfect in code, but Warcraft
    /// resolves clicks against the actual cursor position, so posted clicks were silently
    /// ignored (confirmed on this user's machine — and it is why every Warcraft tool of the
    /// last twenty years moves the cursor instead).
    ///
    /// The player keeps aiming during those 20ms. Restoring the cursor to where it was when
    /// the excursion STARTED throws that motion away — mid-flick that is a visible backwards
    /// yank several times a second, and it was being read as "the cursor gets stolen". The
    /// restore now lands on where the player was actually pointing by the end.</summary>
    public static void ClickAt(int x, int y, bool rightClick)
    {
        NativeMethods.GetCursorPos(out var original);
        try
        {
            NativeMethods.SetCursorPos(x, y);
            // One frame for the game to sample the new position. This dwell cannot be zero —
            // the game reads the cursor on its own schedule, and a click processed after the
            // cursor has already been put back lands wherever the player was aiming.
            Thread.Sleep(8);

            // Down and up in ONE SendInput call. Windows guarantees no other input interleaves
            // within a single array, but not between two calls — and at 1000Hz the player's
            // own mouse delivers a report every millisecond, each applied relative to wherever
            // the cursor now is, i.e. relative to the command card.
            var down = rightClick ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_LEFTDOWN;
            var up = rightClick ? NativeMethods.MOUSEEVENTF_RIGHTUP : NativeMethods.MOUSEEVENTF_LEFTUP;
            SendMouseBatch(down, up);
            Thread.Sleep(8);
        }
        finally
        {
            // Where the cursor ended up minus where we put it = how far the player moved
            // during the excursion. Give that motion back instead of deleting it.
            NativeMethods.GetCursorPos(out var afterwards);
            NativeMethods.SetCursorPos(
                original.X + (afterwards.X - x),
                original.Y + (afterwards.Y - y));
        }
    }

    /// <summary>Submits mouse events as one uninterruptible batch. Returns false if Windows
    /// rejected or truncated it — UIPI, most likely, which is otherwise invisible.</summary>
    private static bool SendMouseBatch(params uint[] flagsList)
    {
        var inputs = flagsList.Select(flags => new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT { dwFlags = flags, dwExtraInfo = Signature },
            },
        }).ToArray();

        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>())
               == (uint)inputs.Length;
    }
}
