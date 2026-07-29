using System.Diagnostics;

namespace LexusWarKey.Windows;

/// <summary>Answers "is Warcraft III the window the user is typing into right now?".
/// The result is cached briefly because the keyboard hook asks on every keystroke and
/// must never do slow work.</summary>
public sealed class GameWindowWatcher
{
    private static readonly string[] GameProcessNames = { "war3", "Warcraft III", "Frozen Throne" };

    private readonly object _gate = new();
    private uint _cachedPid;
    private bool _cachedIsGame;
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public bool IsGameFocused()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && CachedIsGame(pid);
    }

    /// <summary>IsGameFocused runs on the keyboard-hook thread for every keystroke,
    /// and resolving a pid to a process name opens handles — tens of milliseconds on a bad day.
    /// A slow hook is exactly what makes Windows silently remove it, so the answer is cached.</summary>
    private bool CachedIsGame(uint pid)
    {
        lock (_gate)
        {
            // Same window as last time and checked recently -> reuse the answer.
            if (pid == _cachedPid && (DateTime.UtcNow - _cachedAtUtc).TotalSeconds < 2)
                return _cachedIsGame;

            _cachedPid = pid;
            _cachedAtUtc = DateTime.UtcNow;
            _cachedIsGame = ResolveIsGame(pid);
            return _cachedIsGame;
        }
    }

    /// <summary>Is Warcraft running at all, focused or not? Calibration needs this: the two
    /// corners have to be clicked on the real command card, and with the game closed the user
    /// is just clicking on whatever window happens to be underneath.</summary>
    public bool IsGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                    return true;
            }
            catch
            {
                // a process vanishing mid-enumeration is normal; keep looking
            }
        }
        return false;
    }

    /// <summary>Where the game is actually drawing, in screen pixels — its client area, not
    /// the whole screen. This is the difference between a card default that works and one that
    /// points at empty desktop: Warcraft in a window puts its command card at the WINDOW's
    /// bottom-right, and a screen-fraction guess lands nowhere near it.
    ///
    /// Returns false when the game is not running or has no window yet.</summary>
    /// <summary>Smallest client area that could plausibly be Warcraft drawing a game. Measured
    /// against the real thing: a MINIMISED war3 still answers every geometry call, and answers
    /// 237x39 at (-32000,-32000). Nothing rejected that, so while the game sat in the taskbar the
    /// app believed the game was a 237x39 window off the left of the world — which told the player
    /// their command card was "outside the Warcraft window" and to re-link it, and would have
    /// seeded a fresh card into coordinates no click can ever reach.</summary>
    private const int SmallestPlausibleGameWidth = 640;
    private const int SmallestPlausibleGameHeight = 480;

    /// <summary>Is this a rectangle the game could actually be drawing in? Kept free of the
    /// P/Invokes so the rule itself can be tested without a real Warcraft on screen.
    ///
    /// "No answer" has to beat "a wrong answer" here. Every caller treats false as "I cannot see
    /// the game" and falls back to the whole screen, which is merely imprecise. A bogus rectangle
    /// is worse than imprecise: it seeds cards nothing can click and raises a re-link warning
    /// against a card that was never wrong.</summary>
    public static bool IsPlausibleGameArea(bool minimised, int width, int height) =>
        !minimised && width >= SmallestPlausibleGameWidth && height >= SmallestPlausibleGameHeight;

    public bool TryGetGameArea(out int left, out int top, out int width, out int height)
    {
        left = top = width = height = 0;

        var hwnd = FindGameWindow();
        if (hwnd == IntPtr.Zero
            || !NativeMethods.GetClientRect(hwnd, out var client)
            || !IsPlausibleGameArea(NativeMethods.IsIconic(hwnd), client.Right, client.Bottom))
            return false;

        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
            return false;

        left = origin.X;
        top = origin.Y;
        width = client.Right;
        height = client.Bottom;
        return true;
    }

    /// <summary>The game's window, for posting clicks straight at it. Zero when the game is not
    /// running, has no window yet, or is minimised — a minimised window still answers geometry
    /// calls, and the answers describe the taskbar rather than the game.</summary>
    public IntPtr GameWindowForClicks()
    {
        var hwnd = FindGameWindow();
        return hwnd != IntPtr.Zero && !NativeMethods.IsIconic(hwnd) ? hwnd : IntPtr.Zero;
    }

    /// <summary>The game's main window, focused or not.</summary>
    private static IntPtr FindGameWindow()
    {
        foreach (var name in GameProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                            return process.MainWindowHandle;
                    }
                }
            }
            catch
            {
                // a process vanishing mid-enumeration is normal; keep looking
            }
        }
        return IntPtr.Zero;
    }

    public string? ForegroundProcessName()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static bool ResolveIsGame(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return GameProcessNames.Any(n => string.Equals(n, process.ProcessName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
