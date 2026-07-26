using System.IO;
using System.Text.Json;

namespace LexusWarKey.Core;

/// <summary>Loads and saves the profile in %LocalAppData%\LexusWarKey\profile.json.
///
/// The profile is the only thing the user truly owns in this app, and it was once found
/// reset in the field with nothing left behind to explain why. So this store now assumes
/// the worst: every meaningful save also refreshes a backup, a reset-to-defaults can never
/// overwrite that backup, every fallback path restores from it, and every decision is
/// written to the diagnostic log so the next mystery has a paper trail.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Action<string>? _log;

    public ProfileStore(string? rootOverride = null, Action<string>? log = null)
    {
        _log = log;
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LexusWarKey");
        FilePath = Path.Combine(Root, "profile.json");
        BackupPath = Path.Combine(Root, "profile.backup.json");
    }

    public string Root { get; }
    public string FilePath { get; }
    public string BackupPath { get; }

    /// <summary>Set when the profile could not be read for a reason that does not mean it is
    /// broken — an antivirus or backup agent holding the file open, most likely. Saving is then
    /// refused for the rest of the session, because writing defaults over a file we simply could
    /// not open would destroy every binding the user has.</summary>
    public bool ReadOnly { get; private set; }

    /// <summary>Why the last load fell back or restored, or null when it read normally. Shown to
    /// the user — silently starting from scratch is exactly how a profile gets lost unnoticed.</summary>
    public string? LoadWarning { get; private set; }

    public WarKeyProfile Load()
    {
        LoadWarning = null;
        ReadOnly = false;
        try
        {
            if (!File.Exists(FilePath))
            {
                if (TryLoadBackup() is { } fromBackup)
                {
                    LoadWarning = "Тохиргооны үндсэн файл олдсонгүй тул нөөц хувиас сэргээлээ.";
                    _log?.Invoke("profile.json MISSING -> restored from backup");
                    TryCopy(BackupPath, FilePath);
                    return fromBackup;
                }
                _log?.Invoke("profile.json missing, no backup -> defaults (first run?)");
                return WarKeyProfile.CreateDefault();
            }

            var loaded = JsonSerializer.Deserialize<WarKeyProfile>(ReadWithRetry(), Options);
            if (loaded is null)
                return CorruptFallback("null document");
            loaded.NormaliseSlots();
            return loaded;
        }
        catch (JsonException ex)
        {
            return CorruptFallback(ex.Message);
        }
        catch (Exception ex)
        {
            // The file may be perfectly good and merely locked, or on a drive that just went
            // away. Do not touch it, do not touch the backup, and do not let this session
            // overwrite either of them.
            ReadOnly = true;
            LoadWarning = "Тохиргооны файлыг нээж чадсангүй тул түр тохиргоогоор ажиллаж байна. " +
                          "Өөрчлөлт хадгалагдахгүй — аппаа хааж дахин нээнэ үү. " +
                          $"({ex.GetType().Name})";
            _log?.Invoke($"profile.json UNREADABLE ({ex.GetType().Name}: {ex.Message}) -> read-only session on defaults");
            return WarKeyProfile.CreateDefault();
        }
    }

    /// <summary>Unreadable content: quarantine the evidence, then prefer the backup over a
    /// blank slate — losing calibration and bindings is the worst outcome this store has.</summary>
    private WarKeyProfile CorruptFallback(string reason)
    {
        var saved = TryQuarantine();
        _log?.Invoke($"profile.json CORRUPT ({reason}); quarantined={saved ?? "failed"}");

        if (TryLoadBackup() is { } fromBackup)
        {
            LoadWarning = "Тохиргооны файл эвдэрсэн байсан тул нөөц хувиас сэргээлээ.";
            _log?.Invoke("restored from backup after corruption");
            TryCopy(BackupPath, FilePath);
            return fromBackup;
        }

        LoadWarning = saved is null
            ? "Тохиргооны файл эвдэрсэн тул анхны тохиргоогоор эхэллээ."
            : $"Тохиргооны файл эвдэрсэн тул анхны тохиргоогоор эхэллээ. Хуучин хувь: {Path.GetFileName(saved)}";
        return WarKeyProfile.CreateDefault();
    }

    private WarKeyProfile? TryLoadBackup()
    {
        try
        {
            if (!File.Exists(BackupPath))
                return null;
            var backup = JsonSerializer.Deserialize<WarKeyProfile>(File.ReadAllText(BackupPath), Options);
            backup?.NormaliseSlots();
            return backup;
        }
        catch
        {
            return null; // a broken backup is just absent
        }
    }

    /// <summary>A scanner opening the file the instant we do is common and transient, and treating
    /// it as corruption is how a good profile gets thrown away.</summary>
    private string ReadWithRetry()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(FilePath);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }
    }

    public void Save(WarKeyProfile profile)
    {
        if (ReadOnly)
        {
            _log?.Invoke("save REFUSED: read-only session");
            return;
        }

        Directory.CreateDirectory(Root);
        var temp = FilePath + ".tmp";

        // Flushed to disk before the rename: without this, a power cut between the two can
        // commit a zero-length file over a good profile — the rename is atomic, the write is not.
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            JsonSerializer.Serialize(writer, profile, Options);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, FilePath, overwrite: true);

        // The backup only ever moves forward to another configured state. A freshly reset
        // profile is NOT one: after whatever wiped the main file, the empty state that follows
        // must not eat the one copy that can still bring the user's setup back.
        if (IsWorthBackingUp(profile))
            TryCopy(FilePath, BackupPath);
    }

    /// <summary>True when losing this profile would actually cost the user something.
    /// Note the default profile binds Space/Tab and startup seeds a calibrated card, so those
    /// two alone must not count — skills, hand-placed rings and the activation code do.</summary>
    private static bool IsWorthBackingUp(WarKeyProfile profile) =>
        profile.Skills.Any(m => m.ClaimsKey)
        || profile.CommandCard.Overrides is { Count: > 0 }
        || !string.IsNullOrEmpty(profile.ActivationToken);

    private void TryCopy(string from, string to)
    {
        try
        {
            File.Copy(from, to, overwrite: true);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"copy {Path.GetFileName(from)} -> {Path.GetFileName(to)} failed: {ex.GetType().Name}");
        }
    }

    private string? TryQuarantine()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            var target = $"{FilePath}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(FilePath, target, overwrite: true);
            return target;
        }
        catch
        {
            return null; // best effort — losing the copy must not also block startup
        }
    }
}
