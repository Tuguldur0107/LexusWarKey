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

    /// <summary>Why a posted click could not be attempted, or null when it was sent.</summary>
    public static string? WhyCannotPost(IntPtr hwnd, int screenX, int screenY,
                                        out int clientX, out int clientY)
    {
        clientX = clientY = 0;

        if (hwnd == IntPtr.Zero)
            return "the game window could not be found";
        if (NativeMethods.IsIconic(hwnd))
            return "the game is minimised";

        var point = new NativeMethods.POINT { X = screenX, Y = screenY };
        if (!NativeMethods.ScreenToClient(hwnd, ref point))
            return "ScreenToClient failed";
        clientX = point.X;
        clientY = point.Y;

        if (!NativeMethods.GetClientRect(hwnd, out var rect))
            return "GetClientRect failed";
        if (point.X < 0 || point.Y < 0 || point.X >= rect.Right || point.Y >= rect.Bottom)
            return $"the slot converts to client ({point.X},{point.Y}), outside {rect.Right}x{rect.Bottom}";

        return null;
    }

    /// <summary>Clicks a command-card slot by posting mouse messages to the game's own window.
    /// The real cursor never moves, never has to be put back, and the player's aim is never
    /// touched — so a cast during a flick costs nothing and cannot land where the hand happened
    /// to drag the pointer.
    ///
    /// This path was written once before, concluded not to work, and deleted. That conclusion was
    /// wrong, and the way it was wrong is worth recording. The old code converted the slot with
    /// ScreenToClient and refused to post anything when the result fell outside the client area —
    /// correct in itself. But a MINIMISED Warcraft still answers every geometry call, reporting a
    /// 237x39 client area parked at (-32000,-32000), and fullscreen Warcraft minimises itself the
    /// instant you alt-tab. So every measurement taken while setting the experiment up described a
    /// window in the taskbar, every slot converted to something like (34456,33538), the guard
    /// rejected all of them, and not one message was ever posted. The resulting silence was read
    /// as "Warcraft ignores posted clicks" and the app moved to dragging the cursor around
    /// instead. Re-run with the game actually in front, the game acts on these immediately.
    ///
    /// Returns false only when the messages could not be queued; the caller logs the reason and
    /// falls back to the cursor. Queued is not the same as acted upon, which is why the fallback
    /// stays.</summary>
    /// <summary>Packs a client point the way a mouse message carries it: x in the low word, y in
    /// the high word. Kept apart from the P/Invoke so the arithmetic can be tested — an x that
    /// bleeds into y's half puts the click on a different row of the card, which on a command
    /// card is a different ability rather than a near miss.</summary>
    public static int PackClientPoint(int x, int y) => ((y & 0xFFFF) << 16) | (x & 0xFFFF);

    /// <summary>Move, wait, press, hold, release.
    ///
    /// The wait between the move and the press is the part that matters. Posted back to back they
    /// land in the same queue and are pumped in the same pass, while the game only works out which
    /// button the pointer is over once a frame — so the press gets resolved against wherever the
    /// pointer was BEFORE the move, and casting works about one press in five.
    ///
    /// A second move carrying MK_LBUTTON was tried alongside the press, as insurance in case the
    /// first was missed. It stopped casting working altogether: a move that says the button is
    /// already down reads as a drag in progress, and the press that follows is then a press of a
    /// button the game believes it is already holding. Insurance that changes the meaning of the
    /// message is not insurance.
    ///
    /// Both delays come from the caller rather than being fixed here, because the right values
    /// are a property of the machine and the frame rate, and finding them by shipping a release
    /// per guess is no way to find anything.</summary>
    public static bool PostClick(IntPtr hwnd, int clientX, int clientY, bool rightClick,
                                 int settleMs, int holdMs)
    {
        var lParam = (IntPtr)PackClientPoint(clientX, clientY);
        var down = (uint)(rightClick ? NativeMethods.WM_RBUTTONDOWN : NativeMethods.WM_LBUTTONDOWN);
        var up = (uint)(rightClick ? NativeMethods.WM_RBUTTONUP : NativeMethods.WM_LBUTTONUP);
        var button = (IntPtr)(rightClick ? NativeMethods.MK_RBUTTON : NativeMethods.MK_LBUTTON);

        var ok = NativeMethods.PostMessage(hwnd, (uint)NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, lParam);
        if (settleMs > 0)
            Thread.Sleep(settleMs);
        ok &= NativeMethods.PostMessage(hwnd, down, button, lParam);
        Thread.Sleep(holdMs);
        ok &= NativeMethods.PostMessage(hwnd, up, IntPtr.Zero, lParam);
        return ok;
    }

    /// <summary>Dwell before the press and after the release. Before, so the game has seen the
    /// pointer arrive before the button arrives; after, so it has processed the click before the
    /// pointer is taken away again. Eight milliseconds each is what the build with 365 logged
    /// clicks across four days of real matches used, and the excursion the player feels is the
    /// sum of the two.</summary>
    private const int DwellMs = 8;

    /// <summary>Clicks by moving the real cursor to the point and back — about 35ms. Posting
    /// messages to the game window was tried first and looked perfect in code, but Warcraft
    /// resolves clicks against the actual cursor position, so posted clicks were silently
    /// ignored (confirmed on this user's machine).
    ///
    /// Clicking is the right mechanism for LoD and the wrong one everywhere else, and it is worth
    /// being honest about which. AucT's Hotkeys Tool — the tool LoD 6.74c's own instructions point
    /// players at for exactly this problem — casts skills the same way, by left-clicking the card;
    /// its author also says plainly that it "can not properly work on some pc's" and recommends a
    /// CustomKeys.txt instead when you can. Garena's WarKey and Warkey++ are documented around
    /// inventory keys, quick messages and macros and make no such claim, so this is NOT what every
    /// Warcraft tool does — it is what the tools that must survive RANDOM abilities do.
    ///
    /// The alternative is sending the ability's own letter, which is strictly more reliable — no
    /// calibration, no cursor, no queue — but needs that letter to be knowable in advance. In LoD
    /// it is not: skills are drawn fresh each match and two picks can land on the same key, where
    /// Warcraft's own CustomKeyInfo.txt says the result is undefined and only one of them fires.
    /// Position is the only stable handle, which is the whole reason this method exists.
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
    /// The press and the release go in that SAME array as each other. Holding the button down
    /// across two calls was tried, on the theory that a zero-length press can fall between two of
    /// the game's input samples. The theory is unsupported: the build that sent press and release
    /// back to back is the one with 365 clicks across four days of real matches behind it, and a
    /// press the game could not see would have failed every time rather than sometimes. What the
    /// hold did buy was 24ms in which the button is logically down, the destination has to be
    /// re-asserted on the release to stop the hand dragging it, and an interrupted process leaves
    /// the button stuck. One array costs none of that: Windows guarantees nothing interleaves
    /// inside it, so there is no window to defend.
    ///
    /// The player keeps aiming throughout. Restoring the cursor to where it was when the
    /// excursion STARTED throws that motion away — mid-flick that is a visible backwards yank
    /// several times a second, and it was being read as "the cursor gets stolen". The restore
    /// lands on where the player was actually pointing by the end. That measurement only works
    /// while nothing of ours moves the cursor after the click: a release that re-asserts the
    /// destination resets the very difference being measured, which is the other reason the two
    /// are now one event.</summary>
    /// <returns>False when SendInput reported the batch short. Worth logging rather than
    /// swallowing, but do not read it as an elevation test: Microsoft documents that neither the
    /// return value nor GetLastError indicates a UIPI block, so a false here means something
    /// else, and a true here proves nothing about whether the game acted on the press.</returns>
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
            Thread.Sleep(DwellMs);

            // Press and release in ONE array, each carrying the destination, so no report from
            // the player's own mouse can land between them and no button can be left down.
            var clicked = SendMouseBatch(move.With(down), move.With(up));
            Thread.Sleep(DwellMs);
            return moved && clicked;
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
