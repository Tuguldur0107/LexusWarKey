using LexusWarKey.Core;

namespace LexusWarKey.Windows;

/// <summary>Owns the low-level keyboard hook and turns <see cref="RemapEngine"/> decisions
/// into real keystrokes.
///
/// Two rules keep this safe:
///  - anything we inject carries <see cref="InputSender.Signature"/> and is ignored on the way
///    back in, so a remap can never feed itself;
///  - the hook callback does almost nothing: chat macros are handed to a worker thread, because
///    blocking inside a keyboard hook freezes input for the whole desktop.</summary>
public sealed class KeyboardHookService : IDisposable
{
    private readonly RemapEngine _engine;
    private readonly NativeMethods.LowLevelKeyboardProc _callback; // kept alive; a collected delegate crashes the hook
    private readonly Func<IntPtr> _gameWindow;
    private readonly Func<bool> _moveCursorForClicks;
    private IntPtr _hook = IntPtr.Zero;
    private long _lastCallbackTicks;

    /// <summary>Which of the app's own shortcuts are physically held, so auto-repeat fires once.</summary>
    private readonly HashSet<int> _shortcutHeld = new();

    public KeyboardHookService(RemapEngine engine, Func<IntPtr> gameWindow, Func<bool> moveCursorForClicks)
    {
        _engine = engine;
        _gameWindow = gameWindow;
        _moveCursorForClicks = moveCursorForClicks;
        _callback = HookCallback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>How long since any key at all reached the hook. Doubles as the app's only
    /// measure of whether the player is actually typing.</summary>
    public TimeSpan SinceLastKey => TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastCallbackTicks);

    /// <summary>Raised when the master on/off shortcut (Ctrl+F5) is pressed.</summary>
    public event Action? ToggleRequested;

    /// <summary>Raised on Ctrl+F6 — opens/closes the in-game overlay.</summary>
    public event Action? OverlayToggleRequested;

    /// <summary>While true every key is swallowed and forwarded to <see cref="ConfigKeyPressed"/>,
    /// so the user can rebind from inside the game without alt-tabbing.</summary>
    public bool ConfigMode { get; set; }

    public event Action<int>? ConfigKeyPressed;

    public void Install()
    {
        if (IsInstalled)
            return;
        var module = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback, module, 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Windows refused the keyboard hook.");
        _lastCallbackTicks = Environment.TickCount64;
    }

    /// <summary>Windows removes a low-level hook whose callback runs too long, and it does not
    /// clear our handle when it does — so <see cref="IsInstalled"/> keeps saying yes while nothing
    /// arrives any more, and the app looks alive while remapping nothing. There is no API to ask,
    /// so the only available signal is silence: if keystrokes should have been reaching us and
    /// none have for this long, put the hook back.
    ///
    /// Returns true if it re-armed. A needless re-arm costs a few microseconds and our place in
    /// the hook chain, which is why the caller only asks while the game is actually in front.</summary>
    public bool ReArmIfSilent(TimeSpan silence)
    {
        if (!IsInstalled || Environment.TickCount64 - _lastCallbackTicks < silence.TotalMilliseconds)
            return false;

        Uninstall();
        Install(); // throws if Windows refuses; the caller reports a dead remapper rather than lying
        return true;
    }

    public void Uninstall()
    {
        if (!IsInstalled)
            return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Proof of life for ReArmIfSilent — recorded before any filtering so even keys we
        // ignore still count as the hook working.
        _lastCallbackTicks = Environment.TickCount64;

        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        try
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            // Ignore what we injected ourselves — otherwise remaps loop forever.
            if (data.dwExtraInfo == InputSender.Signature || (data.flags & NativeMethods.LLKHF_INJECTED) != 0)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            var msg = (int)wParam;
            var isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            var isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
            if (!isDown && !isUp)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            var vk = (int)data.vkCode;
            var ctrl = InputSender.IsKeyHeld(VirtualKeys.Control);
            var alt = InputSender.IsKeyHeld(VirtualKeys.Alt);

            // Master toggle, handled before anything else so it works even when remapping is off.
            // Auto-repeat must not count: holding Ctrl+F5 for half a second would otherwise
            // toggle the app fifteen times and land on whichever state the release happened to hit.
            if (ctrl && vk is VirtualKeys.F5 or VirtualKeys.F6)
            {
                if (isDown && !_shortcutHeld.Contains(vk))
                {
                    _shortcutHeld.Add(vk);
                    if (vk == VirtualKeys.F5)
                        ToggleRequested?.Invoke();
                    else
                        OverlayToggleRequested?.Invoke();
                }
                else if (isUp)
                {
                    _shortcutHeld.Remove(vk);
                }
                return 1;
            }

            // In-game config: the overlay owns the keyboard until the user closes it.
            if (ConfigMode)
            {
                if (isDown && !IsModifier(vk))
                    ConfigKeyPressed?.Invoke(vk);
                return IsModifier(vk) ? NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam) : 1;
            }

            // Watch the player's own Enter/Escape so we know when Warcraft's chat line is
            // open and can get out of the way. Injected keys were filtered out above, so
            // the app's own macro typing never reaches this.
            _engine.ObserveKey(vk, isDown);

            var decision = _engine.Decide(vk, isDown, ctrl, alt);
            switch (decision.Action)
            {
                case RemapAction.SendKey:
                    InputSender.SendKey(decision.SendVk, isDown);
                    return 1; // swallow the original

                case RemapAction.SendChat when isDown:
                    var lines = decision.ChatLines ?? Array.Empty<string>();
                    var allies = decision.AlliesOnly;
                    // Never type inside the hook: SendInput here would deadlock the input queue.
                    ThreadPool.QueueUserWorkItem(_ => SendChatLines(lines, allies));
                    return 1;

                case RemapAction.ClickSlot when decision.ClickAt is { } point:
                    var right = decision.RightClick;
                    var hwnd = _gameWindow();
                    var allowCursorMove = _moveCursorForClicks();
                    // Clicking involves sleeps; doing it inside the hook would freeze input.
                    ThreadPool.QueueUserWorkItem(_ => SafeClick(hwnd, point.X, point.Y, right, allowCursorMove));
                    return 1;

                case RemapAction.ClickSlot:
                    return 1; // key-up (or an uncalibrated card): swallow, do nothing

                case RemapAction.SendChat:
                    return 1; // swallow the matching key-up too

                default:
                    return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }
        }
        catch
        {
            // A hook that throws would be silently removed by Windows — always fall through.
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    /// <summary>Posts the click to the game window first — that never moves the player's
    /// cursor. Only if the user explicitly allowed it do we fall back to moving the cursor.</summary>
    private static void SafeClick(IntPtr hwnd, int x, int y, bool rightClick, bool allowCursorMove)
    {
        try
        {
            if (InputSender.ClickInWindow(hwnd, x, y, rightClick))
                return;
            if (allowCursorMove)
                InputSender.ClickAt(x, y, rightClick);
        }
        catch { /* a failed click must never take the app down */ }
    }

    private static bool IsModifier(int vk) =>
        vk is VirtualKeys.Shift or VirtualKeys.Control or VirtualKeys.Alt
           or VirtualKeys.LShift or VirtualKeys.RShift
           or VirtualKeys.LControl or VirtualKeys.RControl
           or VirtualKeys.LAlt or VirtualKeys.RAlt;

    private void SendChatLines(IReadOnlyList<string> lines, bool alliesOnly)
    {
        try
        {
            _engine.SuspendedForTyping = true;
            foreach (var line in lines)
            {
                // Warcraft III addresses the chat prompt by modifier, per the manual's hotkey
                // card: Ctrl+Enter opens it to "Allies" only, Shift+Enter to "All" players.
                // Plain Enter is deliberately not used for either — it opens whatever channel
                // the player last selected in the F12 chat menu, so it is not a fixed target
                // and a macro meant for the team could land in front of the enemy.
                InputSender.TapWithModifier(
                    alliesOnly ? VirtualKeys.LControl : VirtualKeys.LShift,
                    VirtualKeys.Enter);

                Thread.Sleep(30);
                InputSender.TypeText(line);
                Thread.Sleep(30);
                InputSender.TapKey(VirtualKeys.Enter);     // send it
                Thread.Sleep(60);                          // let the game settle before the next line
            }
        }
        catch
        {
            // a failed macro must never take the app down
        }
        finally
        {
            _engine.SuspendedForTyping = false;
        }
    }

    public void Dispose() => Uninstall();
}
