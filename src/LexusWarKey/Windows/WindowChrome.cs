using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LexusWarKey.Windows;

public static class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Paints the Windows title bar dark so it stops clashing with the app.</summary>
    public static void UseDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var on = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
    }

    /// <summary>Makes a window that can never take focus — essential for an in-game overlay,
    /// because stealing focus makes a fullscreen game minimise.</summary>
    public static void MakeNonActivating(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    /// <summary>Non-activating AND click-through: the mouse acts as if the window were not
    /// there at all. For the marker flash that shows where the twelve slots will click — it
    /// must never be able to intercept the very clicks it is visualising.</summary>
    public static void MakeClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow | WsExTransparent);
    }
}
