using System.Collections.Concurrent;
using System.IO;

namespace LexusWarKey.Windows;

/// <summary>Append-only breadcrumbs in %LocalAppData%\LexusWarKey\diagnostic.log.
///
/// Writes are handed to a background thread and never touch the caller. That is not an
/// optimisation: this is called from the low-level keyboard hook, and Windows removes a hook
/// whose callback overruns LowLevelHooksTimeout — so a single slow disk or an antivirus
/// scanning the log file could kill the remapper outright, and it would stay dead until the
/// re-arm watchdog noticed. Logging must never be able to do that.</summary>
public static class DiagnosticLog
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LexusWarKey", "diagnostic.log");

    /// <summary>Bounded: if the writer cannot keep up, lines are dropped rather than allowed
    /// to accumulate. A diagnostic aid must not become a memory leak.</summary>
    private static readonly BlockingCollection<string> Pending = new(512);

    static DiagnosticLog()
    {
        var writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "LexusWarKey diagnostics",
            Priority = ThreadPriority.BelowNormal,
        };
        writer.Start();
    }

    /// <summary>Queues one line. Returns immediately; safe to call from the hook.</summary>
    public static void Write(string message)
    {
        // With the date, because this file is read days after the session it describes and a bare
        // clock time cannot say which evening a run belongs to — the log already holds stretches
        // that are impossible to place against "it broke last night".
        Pending.TryAdd($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");
    }

    private static void WriteLoop()
    {
        foreach (var line in Pending.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                // A diagnostic aid must never grow into a disk problem.
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 256 * 1024)
                    info.Delete();

                // Drain whatever else arrived while that was happening, so a burst costs one
                // open instead of one per line.
                var batch = line + "\r\n";
                while (Pending.TryTake(out var more))
                    batch += more + "\r\n";

                File.AppendAllText(FilePath, batch);
            }
            catch
            {
                // never let logging break the thing it is meant to explain
            }
        }
    }
}
