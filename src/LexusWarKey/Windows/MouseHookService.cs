using LexusWarKey.Core;

namespace LexusWarKey.Windows;

/// <summary>Turns bindable mouse controls — wheel up/down, middle, and the two side buttons —
/// into the same virtual-key codes the keyboard path already understands, so a skill or macro
/// can sit on the wheel exactly as it sits on a letter.
///
/// It is installed ONLY while at least one mouse control is actually bound. A low-level mouse
/// hook sees every movement report — a thousand a second on a gaming mouse — so a player who
/// binds nothing to the mouse pays nothing at all.
///
/// The callback obeys the same rule as the keyboard one: decide and return, never work. Left
/// and right buttons are never touched; the game needs them and the app clicks with them.</summary>
public sealed class MouseHookService : IDisposable
{
    private readonly RemapEngine _engine;
    private readonly Action<int, bool> _onBoundControl;
    private readonly NativeMethods.LowLevelKeyboardProc _callback; // same signature for WH_MOUSE_LL
    private IntPtr _hook = IntPtr.Zero;

    /// <param name="onBoundControl">Called with (virtual key, isDown) for a control the profile
    /// binds. Runs on the hook thread, so it must do no work of its own.</param>
    public MouseHookService(RemapEngine engine, Action<int, bool> onBoundControl)
    {
        _engine = engine;
        _onBoundControl = onBoundControl;
        _callback = HookCallback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Installs or removes the hook to match whether anything is bound to the mouse.</summary>
    public void SetActive(bool active)
    {
        if (active == IsInstalled)
            return;

        if (active)
        {
            var module = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _callback, module, 0);
            DiagnosticLog.Write(_hook == IntPtr.Zero
                ? "mouse hook REFUSED by Windows"
                : "mouse hook installed (a mouse control is bound)");
        }
        else
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            DiagnosticLog.Write("mouse hook removed (nothing bound to the mouse)");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        try
        {
            var msg = (int)wParam;

            // Movement is the overwhelming majority of what arrives here; get out first.
            if (msg is NativeMethods.WM_MOUSEMOVE)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            var data = System.Runtime.InteropServices.Marshal
                .PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // Our own clicks must never come back in as bindings.
            if (data.dwExtraInfo == InputSender.Signature
                || (data.flags & NativeMethods.LLMHF_INJECTED) != 0)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            var (vk, isDown) = Translate(msg, data.mouseData);
            if (vk == 0)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            // Only swallow what the profile actually claims. An unbound wheel must keep
            // zooming the camera and an unbound middle button must keep doing whatever it does.
            if (!_engine.ClaimsMouseControl(vk))
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            _onBoundControl(vk, isDown);
            return 1;
        }
        catch
        {
            // A hook that throws would be silently removed by Windows — always fall through.
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    /// <summary>Maps a mouse message to a virtual key, or 0 when it is not bindable.</summary>
    private static (int Vk, bool IsDown) Translate(int msg, uint mouseData)
    {
        switch (msg)
        {
            case NativeMethods.WM_MOUSEWHEEL:
                // The wheel has no press and release, only notches. The caller gets a complete
                // press so anything downstream that pairs down with up stays balanced.
                var delta = (short)(mouseData >> 16);
                if (delta == 0)
                    return (0, false);
                return (delta > 0 ? VirtualKeys.WheelUp : VirtualKeys.WheelDown, true);

            case NativeMethods.WM_MBUTTONDOWN: return (VirtualKeys.MButton, true);
            case NativeMethods.WM_MBUTTONUP: return (VirtualKeys.MButton, false);

            case NativeMethods.WM_XBUTTONDOWN:
                return ((mouseData >> 16) == 1 ? VirtualKeys.XButton1 : VirtualKeys.XButton2, true);
            case NativeMethods.WM_XBUTTONUP:
                return ((mouseData >> 16) == 1 ? VirtualKeys.XButton1 : VirtualKeys.XButton2, false);

            default:
                return (0, false);
        }
    }

    public void Dispose() => SetActive(false);
}
