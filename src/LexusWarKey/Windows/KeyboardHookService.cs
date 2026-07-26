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
    private IntPtr _hook = IntPtr.Zero;
    private long _lastCallbackTicks;

    /// <summary>Which of the app's own shortcuts are physically held, so auto-repeat fires once.</summary>
    private readonly HashSet<int> _shortcutHeld = new();

    /// <summary>Macro and skill keys currently held, so OS auto-repeat fires each once.</summary>
    private readonly HashSet<int> _actionHeld = new();

    /// <summary>1 while a chat macro is typing. Two at once would interleave keystrokes.</summary>
    private int _macroInFlight;

    /// <summary>All clicks run through one thread, one after another. On the ThreadPool two
    /// rapid casts could interleave their cursor excursions — the cursor ping-ponged and both
    /// clicks could land wrong. Twelve deep: at ~20ms per excursion that is a quarter second
    /// of combo, and a dropped finisher is indistinguishable from a misfire.</summary>
    private readonly System.Collections.Concurrent.BlockingCollection<(int X, int Y, bool Right)> _clickQueue = new(12);

    /// <summary>Contact-bounce debounce, per key. Deliberately far below any human re-cast
    /// interval: an earlier 250ms filter keyed on the CARD POSITION was deleting real second
    /// casts, which is one of the "my ability didn't fire" reports.</summary>
    private const int DebounceMs = 35;
    private readonly Dictionary<int, long> _lastPressTicks = new();

    public KeyboardHookService(RemapEngine engine)
    {
        _engine = engine;
        _callback = HookCallback;

        var clickThread = new Thread(() =>
        {
            foreach (var (x, y, right) in _clickQueue.GetConsumingEnumerable())
                SafeClick(x, y, right);
        })
        {
            IsBackground = true,
            Name = "LexusWarKey clicks",
        };
        clickThread.Start();
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>How long since any key at all reached the hook. Doubles as the app's only
    /// measure of whether the player is actually typing.</summary>
    public TimeSpan SinceLastKey => TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastCallbackTicks);

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

            // There is deliberately NO master-toggle hotkey any more. Ctrl+F5 collided with
            // real play (F5 carries the user's own macros), it got pressed by accident, and a
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
                if (isDown && !IsModifier(vk))
                    ConfigKeyPressed?.Invoke(vk);
                return IsModifier(vk) ? NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam) : 1;
            }

            // While the app is typing a macro, the chat line belongs to it: a player's stray
            // Enter or Escape mid-macro would send a half-typed line or close the prompt, and
            // every later line of the macro would land in the game as raw orders. The key that
            // STARTED the macro gets the same treatment — a six-line macro outlasts the OS
            // auto-repeat delay, and those repeats would otherwise type into the open prompt.
            if (_engine.SuspendedForTyping
                && (vk is VirtualKeys.Enter or VirtualKeys.Escape || _actionHeld.Contains(vk)))
            {
                // The release still has to clear the latch, or the key would need a second
                // press after the macro before it worked again.
                if (isUp)
                    _actionHeld.Remove(vk);
                return 1;
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
                    // Auto-repeat must not restart the macro, and two macros must never
                    // interleave — the second would type into the middle of the first's line.
                    if (!_actionHeld.Add(vk))
                        return 1;
                    if (Interlocked.CompareExchange(ref _macroInFlight, 1, 0) != 0)
                        return 1;
                    // Armed here, not in the worker: the pool can lag, and a second key in
                    // that gap used to slip past the SuspendedForTyping check entirely.
                    _engine.SuspendedForTyping = true;
                    var lines = decision.ChatLines ?? Array.Empty<string>();
                    var allies = decision.AlliesOnly;
                    // Never type inside the hook: SendInput here would deadlock the input queue.
                    ThreadPool.QueueUserWorkItem(_ => SendChatLines(lines, allies));
                    return 1;

                case RemapAction.ClickSlot when decision.ClickAt is { } point:
                    if (!_actionHeld.Add(vk))
                        return 1; // OS auto-repeat of a held key — one press, one click

                    // Debounce only: keyed on the KEY, not the position, and short enough to
                    // absorb a contact bounce and nothing else. The old filter was 250ms keyed
                    // on position, so a deliberate second cast of one ability was deleted
                    // while alternating two abilities defeated it entirely.
                    var now = Environment.TickCount64;
                    if (_lastPressTicks.TryGetValue(vk, out var previous)
                        && now - previous < DebounceMs)
                    {
                        DiagnosticLog.Write($"click SUPPRESSED vk={vk} ({now - previous}ms since last)");
                        return 1;
                    }
                    _lastPressTicks[vk] = now;

                    // Clicking involves sleeps; doing it inside the hook would freeze input.
                    if (!_clickQueue.TryAdd((point.X, point.Y, decision.RightClick)))
                        DiagnosticLog.Write($"click DROPPED vk={vk}: queue full ({_clickQueue.Count})");
                    return 1;

                case RemapAction.ClickSlot:
                    if (isUp)
                        _actionHeld.Remove(vk);
                    return 1; // key-up (or an uncalibrated card): swallow, do nothing

                case RemapAction.SendChat:
                    if (isUp)
                        _actionHeld.Remove(vk);
                    return 1; // swallow the matching key-up too

                default:
                    // Only the key-UP releases the latch. Clearing it on a passed-through
                    // key-DOWN re-armed a still-held macro key the moment the macro finished,
                    // and a held F5 then re-sent the whole block once a second.
                    if (isUp)
                        _actionHeld.Remove(vk);
                    return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }
        }
        catch
        {
            // A hook that throws would be silently removed by Windows — always fall through.
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    /// <summary>Moves the real cursor, clicks, and puts it back — the same thing Garena's
    /// WarKey and every other Warcraft tool does, because the game resolves a click against
    /// the actual cursor. There used to be a PostMessage mode that never moved the cursor;
    /// it was removed after proving, on a real machine, that the game simply ignores it.</summary>
    private static void SafeClick(int x, int y, bool rightClick)
    {
        try
        {
            // Mid-drag-select, an injected button-up would end the player's drag on the command
            // card and their physical release would then orphan a second up — a corrupted
            // selection that lasts for seconds. Wait the drag out; a held button is brief.
            var waited = 0;
            while (InputSender.IsKeyHeld(VirtualKeys.LButton) && waited < 400)
            {
                Thread.Sleep(20);
                waited += 20;
            }
            if (waited > 0)
                DiagnosticLog.Write($"click deferred {waited}ms: mouse button held");

            var watch = System.Diagnostics.Stopwatch.StartNew();
            InputSender.ClickAt(x, y, rightClick);
            watch.Stop();
            DiagnosticLog.Write($"click ({x},{y}) right={rightClick} {watch.ElapsedMilliseconds}ms");
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
        // SuspendedForTyping was already set in the hook, before this was queued — setting it
        // here left a gap where a second macro could slip in while the pool spun up a thread.
        try
        {
            foreach (var line in lines)
            {
                // Warcraft III addresses the chat prompt by modifier, per the manual's hotkey
                // card: Ctrl+Enter opens it to "Allies" only, Shift+Enter to "All" players.
                // Plain Enter is deliberately not used for either — it opens whatever channel
                // the player last selected in the F12 chat menu, so it is not a fixed target
                // and a macro meant for the team could land in front of the enemy.
                if (!InputSender.TapWithModifier(
                        alliesOnly ? VirtualKeys.LControl : VirtualKeys.LShift,
                        VirtualKeys.Enter))
                    return; // Windows refused the injection (UIPI?) — stop, don't type into nothing

                // The prompt must exist before the text arrives, and the game opens it on its
                // own schedule — one frame at best, several under load. Typing early does not
                // queue: the keystrokes land in the game world as orders. These delays are
                // deliberately generous; a macro is allowed to take half a second, it is not
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
            // a failed macro must never take the app down
        }
        finally
        {
            _engine.SuspendedForTyping = false;
            Interlocked.Exchange(ref _macroInFlight, 0);
        }
    }

    public void Dispose()
    {
        Uninstall();
        _clickQueue.CompleteAdding(); // lets the click thread finish its loop and exit
    }
}
