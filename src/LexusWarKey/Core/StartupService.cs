using Microsoft.Win32;

namespace LexusWarKey.Core;

/// <summary>Starts the app with Windows via the per-user Run key.
///
/// Per-user (HKCU) on purpose: it needs no administrator rights, affects nobody else on the
/// machine, and the user can remove it from Task Manager's Startup tab like any other app.</summary>
public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LexusWarKey";

    public static string ExePath => Environment.ProcessPath ?? "";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string existing && existing.Contains("LexusWarKey", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
                return false;

            if (enabled)
            {
                if (string.IsNullOrEmpty(ExePath))
                    return false;
                // Quoted: the path may contain spaces, and Windows would otherwise split it.
                key.SetValue(ValueName, $"\"{ExePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Keeps the Run entry pointing at where the exe actually is now — otherwise
    /// moving the portable exe would leave a startup entry aimed at nothing.</summary>
    public void RepairPathIfNeeded()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string current || string.IsNullOrEmpty(ExePath))
                return;
            var expected = $"\"{ExePath}\"";
            if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, expected, RegistryValueKind.String);
        }
        catch
        {
            // not critical
        }
    }
}
