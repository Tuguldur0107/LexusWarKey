using System.IO;
using System.Text.Json;

namespace LexusWarKey.Core;

/// <summary>Loads and saves %LocalAppData%\LexusWarKey\profile.json.</summary>
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
    public bool ReadOnly { get; private set; }
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
                    LoadWarning = "Profile file was missing, so settings were restored from backup.";
                    _log?.Invoke("profile.json missing -> restored from backup");
                    TryCopy(BackupPath, FilePath);
                    return fromBackup;
                }

                _log?.Invoke("profile.json missing, no backup -> defaults");
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
            ReadOnly = true;
            LoadWarning = $"Profile file could not be opened. Changes will not be saved this session. ({ex.GetType().Name})";
            _log?.Invoke($"profile.json unreadable ({ex.GetType().Name}: {ex.Message}) -> read-only defaults");
            return WarKeyProfile.CreateDefault();
        }
    }

    private WarKeyProfile CorruptFallback(string reason)
    {
        var saved = TryQuarantine();
        _log?.Invoke($"profile.json corrupt ({reason}); quarantined={saved ?? "failed"}");

        if (TryLoadBackup() is { } fromBackup)
        {
            LoadWarning = "Profile file was corrupt, so settings were restored from backup.";
            TryCopy(BackupPath, FilePath);
            return fromBackup;
        }

        LoadWarning = saved is null
            ? "Profile file was corrupt, so defaults were loaded."
            : $"Profile file was corrupt, so defaults were loaded. Old copy: {Path.GetFileName(saved)}";
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
            return null;
        }
    }

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
            _log?.Invoke("save refused: read-only session");
            return;
        }

        profile.NormaliseSlots();
        Directory.CreateDirectory(Root);
        var temp = FilePath + ".tmp";

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            JsonSerializer.Serialize(writer, profile, Options);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, FilePath, overwrite: true);

        if (IsWorthBackingUp(profile))
            TryCopy(FilePath, BackupPath);
    }

    private static bool IsWorthBackingUp(WarKeyProfile profile) =>
        profile.Skills.Any(m => m.ClaimsKey)
        || profile.ChatMacros.Any(m => m.IsUsable);

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
            return null;
        }
    }
}
