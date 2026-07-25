using System.IO;

namespace LexusWarKey.Windows;

/// <summary>Append-only breadcrumbs in %LocalAppData%\LexusWarKey\diagnostic.log.
///
/// "It doesn't work" reports were impossible to act on because the app kept no record of
/// what it actually did — whether a click fired, where it went, whether Windows refused an
/// injection. This is deliberately tiny: one line per event, capped size, and a failure to
/// log must never take the remapper with it.</summary>
public static class DiagnosticLog
{
    private static readonly object Gate = new();

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LexusWarKey", "diagnostic.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                // A diagnostic aid must never grow into a disk problem.
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 256 * 1024)
                    info.Delete();

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\r\n");
            }
        }
        catch
        {
            // never let logging break the thing it is meant to explain
        }
    }
}
