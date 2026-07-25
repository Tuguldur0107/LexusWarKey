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

    /// <summary>Handle of the focused window when it belongs to the game, else zero.</summary>
    public IntPtr GameWindowHandle()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && CachedIsGame(pid) ? hwnd : IntPtr.Zero;
    }

    /// <summary>Both public entry points run on the keyboard-hook thread for every keystroke,
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
