namespace LexusWarKey.Windows;

/// <summary>Captures the next left-click anywhere on screen. Used only while calibrating the
/// command card, and uninstalled the moment calibration finishes — a permanent mouse hook
/// would be both wasteful and intrusive.</summary>
public sealed class MouseCaptureService : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback; // same signature works for WH_MOUSE_LL
    private IntPtr _hook = IntPtr.Zero;
    private Action<int, int>? _onClick;

    public MouseCaptureService() => _callback = HookCallback;

    public bool IsCapturing => _hook != IntPtr.Zero;

    public void CaptureNextClick(Action<int, int> onClick)
    {
        Stop();
        _onClick = onClick;
        var module = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _callback, module, 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
            return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _onClick = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (int)wParam != NativeMethods.WM_LBUTTONDOWN)
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        try
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo == InputSender.Signature)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            var handler = _onClick;
            Stop();
            // Swallow this click so it does not also reach the game, then report it.
            handler?.Invoke(data.pt.X, data.pt.Y);
            return 1;
        }
        catch
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    public void Dispose() => Stop();
}
