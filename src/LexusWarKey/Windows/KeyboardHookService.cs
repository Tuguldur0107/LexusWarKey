using LexusWarKey.Core;

namespace LexusWarKey.Windows;

/// <summary>Owns the low-level keyboard hook and turns <see cref="RemapEngine"/> decisions
/// into real keystrokes.
///
/// Two rules keep this safe:
///  - anything we inject carries <see cref="InputSender.Signature"/> and is ignored on the way
///    back in, so a remap can never feed itself;
///  - the hook callback does almost nothing: QuickChat typing is handed to a worker thread, because
///    blocking inside a keyboard hook freezes input for the whole desktop.</summary>
public sealed class KeyboardHookService : IDisposable
{
    private readonly RemapEngine _engine;

    private readonly NativeMethods.LowLevelKeyboardProc _callback; // kept alive; a collected delegate crashes the hook
    private IntPtr _hook = IntPtr.Zero;
    private long _lastCallbackTicks;

    /// <summary>Which of the app's own shortcuts are physically held, so auto-repeat fires once.</summary>
    private readonly HashSet<int> _shortcutHeld = new();

    /// <summary>QuickChat and skill keys currently held, so OS auto-repeat fires each once.</summary>
    private readonly HashSet<int> _actionHeld = new();

    /// <summary>The target key sent for each held physical remap key. Key-up must release the
    /// same target key even if the user changes the binding while the physical key is down.</summary>
    private readonly Dictionary<int, int> _heldRemaps = new();

    /// <summary>1 while QuickChat is typing. Two at once would interleave keystrokes.</summary>
    private int _quickChatInFlight;

    public KeyboardHookService(RemapEngine engine)
    {
        _engine = engine;
        _callback = HookCallback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>How long since any key at all reached the hook. Doubles as the app's only
    /// measure of whether the player is actually typing.</summary>
    public TimeSpan SinceLastKey => TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastCallbackTicks);

    /// <summary>Raised on Ctrl+F6 — opens/closes the in-game overlay.</summary>
    public event Action? OverlayToggleRequested;

    /// <summary>While true every key is swallowed and forwarded to <see cref="ConfigKeyPressed"/>,
    /// so the user can rebind from inside the game without alt-tabbing.</summary>
    private bool _configMode;

    public bool ConfigMode
    {
        get => _configMode;
        set
        {
            if (_configMode == value)
                return;
            if (value)
                ReleaseHeldRemaps();
            _configMode = value;
        }
    }

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
        ReleaseHeldRemaps();
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>Carries out a decision. Returns true when the original input was consumed.</summary>
    private bool Dispatch(RemapDecision decision, int vk, bool isDown)
    {
        var isUp = !isDown;

        switch (decision.Action)
        {
            case RemapAction.SendKey:
                var target = decision.SendVk;
                if (isDown)
                {
                    if (_heldRemaps.TryGetValue(vk, out var existingTarget))
                        target = existingTarget;
                    else
                        _heldRemaps[vk] = target;
                }
                else if (_heldRemaps.Remove(vk, out var releasedTarget))
                {
                    target = releasedTarget;
                }

                InputSender.SendKey(target, isDown);
                return true; // swallow the original

            case RemapAction.SendChat when isDown:
                // Auto-repeat must not restart QuickChat, and two messages must never
                // interleave — the second would type into the middle of the first's line.
                if (!_actionHeld.Add(vk))
                    return true;
                if (Interlocked.CompareExchange(ref _quickChatInFlight, 1, 0) != 0)
                    return true;
                // Armed here, not in the worker: the pool can lag, and a second key in
                // that gap used to slip past the SuspendedForTyping check entirely.
                _engine.SuspendedForTyping = true;
                var lines = decision.ChatLines ?? Array.Empty<string>();
                // Never type inside the hook: SendInput here would deadlock the input queue.
                ThreadPool.QueueUserWorkItem(_ => SendChatLines(lines));
                return true;

            case RemapAction.SendChat:
                if (isUp)
                    _actionHeld.Remove(vk);
                return true; // swallow the matching key-up too

            default:
                // A key whose binding changed (or was cleared) while it was physically held:
                // the game still has the OLD target down, so release that one rather than
                // leaving a key stuck down for the rest of the match.
                if (isUp && _heldRemaps.Remove(vk, out var stillHeldTarget))
                {
                    InputSender.SendKey(stillHeldTarget, false);
                    return true;
                }

                // Only the key-UP releases the latch. Clearing it on a passed-through
                // key-DOWN re-armed a still-held QuickChat key the moment typing finished,
                // and a held F5 then re-sent the whole line once a second.
                if (isUp)
                    _actionHeld.Remove(vk);
                return false;
        }
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

            // There is deliberately NO master-toggle hotkey any more. Ctrl+F5 collided with
            // real play, it got pressed by accident, and a
            // latch bug made toggling back on unreliable — so the one way to disable the app
            // is the checkbox in the window, where the state is visible.
            //
            // Ctrl+F6 (overlay) stays, auto-repeat-guarded. The key-up cleanup below is
            // unconditional because Ctrl is often released before F6 — the old code only
            // cleared the latch while Ctrl was still down, leaving F6 permanently "held".
            if (isUp)
                _shortcutHeld.Remove(vk);

            if (ctrl && vk == VirtualKeys.F6)
            {
                if (isDown && _shortcutHeld.Add(vk))
                    OverlayToggleRequested?.Invoke();
                return 1;
            }

            // In-game config: the overlay owns the keyboard until the user closes it.
            if (ConfigMode)
            {
                if (isUp && _heldRemaps.Remove(vk, out var target))
                {
                    InputSender.SendKey(target, false);
                    return 1;
                }

                if (isDown && !IsModifier(vk))
                    ConfigKeyPressed?.Invoke(vk);
                return IsModifier(vk) ? NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam) : 1;
            }

            // While the app is typing QuickChat, the chat line belongs to it: a player's stray
            // Enter or Escape during typing would send a half-typed line or close the prompt, and
            // the rest of the message would land in the game as raw orders. The key that
            // started QuickChat gets the same treatment because auto-repeat can outlast
            // the SendInput typing delay.
            if (_engine.SuspendedForTyping
                && (vk is VirtualKeys.Enter or VirtualKeys.Escape || _actionHeld.Contains(vk)))
            {
                // The release still has to clear the latch, or the key would need a second
                // press after QuickChat before it worked again.
                if (isUp)
                    _actionHeld.Remove(vk);
                return 1;
            }

            // Watch the player's own Enter/Escape so we know when Warcraft's chat line is
            // open and can get out of the way. Injected keys were filtered out above, so
            // the app's own QuickChat typing never reaches this.
            _engine.ObserveKey(vk, isDown);

            var decision = _engine.Decide(vk, isDown, ctrl, alt);
            return Dispatch(decision, vk, isDown)
                ? 1
                : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        catch
        {
            // A hook that throws would be silently removed by Windows — always fall through.
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    private static bool IsModifier(int vk) =>
        vk is VirtualKeys.Shift or VirtualKeys.Control or VirtualKeys.Alt
           or VirtualKeys.LShift or VirtualKeys.RShift
           or VirtualKeys.LControl or VirtualKeys.RControl
           or VirtualKeys.LAlt or VirtualKeys.RAlt;

    private void ReleaseHeldRemaps()
    {
        if (_heldRemaps.Count == 0)
            return;

        var targets = _heldRemaps.Values.Distinct().ToList();
        _heldRemaps.Clear();
        foreach (var target in targets)
            InputSender.SendKey(target, false);
    }

    private void SendChatLines(IReadOnlyList<string> lines)
    {
        // SuspendedForTyping was already set in the hook, before this was queued — setting it
        // here left a gap where a second message could slip in while the pool spun up a thread.
        //
        // Every key is passed through untouched for the whole of this. That is a real window in
        // which a skill key does nothing, so both ends are on the record rather than left to be
        // guessed at from a gap in the clicks.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        DiagnosticLog.Write($"QuickChat typing {lines.Count} line(s) - remapping suspended");
        try
        {
            foreach (var line in lines)
            {
                // QuickChat always uses the fixed all-chat path.
                if (!InputSender.TapWithModifier(VirtualKeys.LShift, VirtualKeys.Enter))
                    return; // Windows refused the injection (UIPI?) — stop, don't type into nothing

                // The prompt must exist before the text arrives, and the game opens it on its
                // own schedule: one frame at best, several under load. Typing early does not
                // queue: the keystrokes land in the game world as orders. These delays are
                // deliberately generous; QuickChat is allowed to take half a second, it is not
                // allowed to feed "-clear" to the hero as movement.
                Thread.Sleep(80);
                if (!InputSender.TypeText(line))
                    return; // partial delivery — pressing Enter now would send a mangled line

                Thread.Sleep(50);
                InputSender.TapKey(VirtualKeys.Enter);     // send it
                Thread.Sleep(120);                         // let the prompt fully close before reopening
            }
        }
        catch
        {
            // a failed QuickChat send must never take the app down
        }
        finally
        {
            _engine.SuspendedForTyping = false;
            Interlocked.Exchange(ref _quickChatInFlight, 0);
            DiagnosticLog.Write($"QuickChat done after {watch.ElapsedMilliseconds}ms - remapping live again");
        }
    }

    public void Dispose()
    {
        Uninstall();
    }
}
