using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LexusWarKey.Core;

/// <summary>A short, stable identifier for this computer, used to tie an activation code to
/// the machine it was issued for — a code copied to a friend simply does not validate there.
///
/// It is a truncated hash of Windows' own installation GUID plus the system volume serial,
/// so it identifies the install without carrying anything personal: it cannot be reversed,
/// and it says nothing about who the user is, what hardware they own, or where they are.
/// Eight hex characters is short enough to read aloud and far beyond collision range for a
/// community of this size.</summary>
public static class MachineId
{
    private static readonly Lazy<string> Cached = new(Compute);

    /// <summary>Eight uppercase hex characters, e.g. "A3F19C22".</summary>
    public static string Current => Cached.Value;

    private static string Compute()
    {
        var parts = new List<string>();

        try
        {
            // Written once when Windows is installed and stable for the life of that install.
            using var key = Microsoft.Win32.RegistryKey
                .OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid))
                parts.Add(guid);
        }
        catch
        {
            // Locked-down policy or a stripped install — the volume serial alone still works.
        }

        try
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(systemDrive)
                && NativeVolumeSerial(systemDrive) is { } serial and not 0)
                parts.Add(serial.ToString("X8"));
        }
        catch
        {
            // ignored — see above
        }

        // Nothing readable at all: fall back to the machine name so activation still works
        // rather than failing shut. Weaker binding beats a user who cannot activate.
        if (parts.Count == 0)
            parts.Add(Environment.MachineName);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return Convert.ToHexString(digest.AsSpan(0, 4));
    }

    private static uint? NativeVolumeSerial(string rootPath)
    {
        return Windows.NativeMethods.GetVolumeInformation(
            rootPath, null, 0, out var serial, out _, out _, null, 0)
            ? serial
            : null;
    }
}
