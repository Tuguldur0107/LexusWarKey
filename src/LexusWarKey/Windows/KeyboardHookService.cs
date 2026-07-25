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

    public KeyboardHookService(RemapEngine engine, Func<IntPtr> gameWindow, Func<bool> moveCursorForClicks)
    {
        _engine = engine;
        _gameWindow = gameWindow;
        _moveCursorForClicks = moveCursorForClicks;
        _callback = HookCallback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

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
            if (isDown && ctrl && vk == VirtualKeys.F5)
            {
                ToggleRequested?.Invoke();
                return 1;
            }

            if (isDown && ctrl && vk == VirtualKeys.F6)
            {
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
                if (alliesOnly)
                {
                    InputSender.SendKey(VirtualKeys.Shift, true);
                    InputSender.TapKey(VirtualKeys.Enter);
                    InputSender.SendKey(VirtualKeys.Shift, false);
                }
                else
                {
                    InputSender.TapKey(VirtualKeys.Enter); // open the chat line
                }

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
