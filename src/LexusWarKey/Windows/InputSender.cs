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

    /// <summary>Milliseconds the button stays down. A press and release delivered back to back
    /// occupy no time at all, and anything sampling input once a frame can look before and after
    /// and never see the button down — the cursor visibly darts to the slot and no skill fires.
    /// This outlasts a frame at 60fps with margin.</summary>
    private const int HoldMs = 24;

    /// <summary>Settle time before the press and after the release.</summary>
    private const int SettleMs = 6;

    /// <summary>Clicks by moving the real cursor to the point and back — about 35ms. Posting
    /// messages to the game window was tried first and looked perfect in code, but Warcraft
    /// resolves clicks against the actual cursor position, so posted clicks were silently
    /// ignored (confirmed on this user's machine — and it is why every Warcraft tool of the
    /// last twenty years moves the cursor instead).
    ///
    /// Two things make the press actually land, and both were missing:
    ///
    /// The destination rides WITH each button event, as an absolute move in the same SendInput
    /// array. Windows guarantees nothing interleaves inside one array but nothing at all between
    /// two calls, and the player's mouse delivers a report every millisecond — each one applied
    /// relative to wherever the cursor now is. Moving first and pressing afterwards meant the
    /// press landed whereever the hand had dragged the cursor in the meantime, which during a
    /// fast flick is far outside a 35px slot. That is the intermittent miss: it fails exactly
    /// when the player is moving, which in a fight is most of the time.
    ///
    /// And the button is held for <see cref="HoldMs"/> rather than released instantly, so a
    /// press cannot fall between two of the game's input samples.
    ///
    /// The player keeps aiming throughout. Restoring the cursor to where it was when the
    /// excursion STARTED throws that motion away — mid-flick that is a visible backwards yank
    /// several times a second, and it was being read as "the cursor gets stolen". The restore
    /// lands on where the player was actually pointing by the end.</summary>
    /// <returns>False when Windows refused the press. Worth reporting rather than swallowing:
    /// if the game is elevated and this app is not, UIPI drops injected input silently, and
    /// every cast then fails identically with nothing on screen to explain it.</returns>
    public static bool ClickAt(int x, int y, bool rightClick)
    {
        NativeMethods.GetCursorPos(out var original);
        var down = rightClick ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_LEFTDOWN;
        var up = rightClick ? NativeMethods.MOUSEEVENTF_RIGHTUP : NativeMethods.MOUSEEVENTF_LEFTUP;
        var move = AbsoluteMove(x, y);

        try
        {
            // A real move event, not SetCursorPos: SetCursorPos relocates the pointer without
            // announcing it, so anything watching the input stream rather than polling the
            // pointer never learns the cursor went anywhere.
            var moved = SendMouseBatch(move);
            Thread.Sleep(SettleMs);

            var pressed = SendMouseBatch(move.With(down));
            Thread.Sleep(HoldMs);
            var released = SendMouseBatch(move.With(up));   // re-asserted: the hand moved during the hold
            Thread.Sleep(SettleMs);
            return moved && pressed && released;
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

    /// <summary>One mouse event: an absolute destination plus whatever buttons ride along.</summary>
    private readonly record struct MouseMove(int Dx, int Dy, uint Flags)
    {
        public MouseMove With(uint extraFlags) => this with { Flags = Flags | extraFlags };
    }

    private static MouseMove AbsoluteMove(int x, int y)
    {
        var (dx, dy) = ToAbsolute(
            x, y,
            NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
        return new MouseMove(dx, dy,
            NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE
            | NativeMethods.MOUSEEVENTF_VIRTUALDESK);
    }

    /// <summary>Maps a screen pixel onto the 0-65535 grid an absolute mouse event uses, across the
    /// whole virtual desktop.
    ///
    /// Every pixel owns a band of that grid, and Windows converts back by truncating — so aiming
    /// at the START of a pixel's band lands on the pixel BEFORE it, every time. This aims at the
    /// middle of the band instead, which is both correct and the furthest from either neighbour.
    /// A click a pixel short does not matter in the middle of a slot and decides the cast at its
    /// edge. Kept clear of the P/Invokes so the arithmetic can be tested.</summary>
    public static (int Dx, int Dy) ToAbsolute(int x, int y, int left, int top, int width, int height)
    {
        // ((offset + 0.5) * 65536) / span, in integers so nothing rounds twice. Clamped because
        // a card calibrated on a monitor that has since been unplugged sits outside the desktop,
        // and an out-of-range value here is not a click somewhere odd — it is a garbage event.
        static int Map(int value, int origin, int span) => span <= 0
            ? 0
            : (int)Math.Clamp(((value - origin) * 2L + 1) * 65536 / (2L * span), 0, 65535);

        return (Map(x, left, width), Map(y, top, height));
    }

    /// <summary>Submits mouse events as one uninterruptible batch. Returns false if Windows
    /// rejected or truncated it — UIPI, most likely, which is otherwise invisible.</summary>
    private static bool SendMouseBatch(params MouseMove[] events)
    {
        var inputs = events.Select(e => new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = e.Dx, dy = e.Dy, dwFlags = e.Flags, dwExtraInfo = Signature,
                },
            },
        }).ToArray();

        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>())
               == (uint)inputs.Length;
    }
}
