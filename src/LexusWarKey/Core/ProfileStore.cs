using System.IO;
using System.Text.Json;

namespace LexusWarKey.Core;

/// <summary>Loads and saves the profile in %LocalAppData%\LexusWarKey\profile.json.
/// A corrupt file is quarantined and replaced with defaults rather than blocking startup.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ProfileStore(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LexusWarKey");
        FilePath = Path.Combine(Root, "profile.json");
    }

    public string Root { get; }
    public string FilePath { get; }

    /// <summary>Set when the profile could not be read for a reason that does not mean it is
    /// broken — an antivirus or backup agent holding the file open, most likely. Saving is then
    /// refused for the rest of the session, because writing defaults over a file we simply could
    /// not open would destroy every binding the user has.</summary>
    public bool ReadOnly { get; private set; }

    /// <summary>Why the last load fell back to defaults, or null if it did not. Shown to the user —
    /// silently starting from scratch is exactly how a profile gets lost without anyone noticing.</summary>
    public string? LoadWarning { get; private set; }

    public WarKeyProfile Load()
    {
        LoadWarning = null;
        ReadOnly = false;
        try
        {
            if (!File.Exists(FilePath))
                return WarKeyProfile.CreateDefault();

            var loaded = JsonSerializer.Deserialize<WarKeyProfile>(ReadWithRetry(), Options);
            if (loaded is null)
                return WarKeyProfile.CreateDefault();
            loaded.NormaliseSlots();
            return loaded;
        }
        catch (JsonException)
        {
            // Genuinely unreadable content: keep a copy so nothing is destroyed, then start fresh.
            var saved = TryQuarantine();
            LoadWarning = saved is null
                ? "Тохиргооны файл эвдэрсэн тул анхны тохиргоогоор эхэллээ."
                : $"Тохиргооны файл эвдэрсэн тул анхны тохиргоогоор эхэллээ. Хуучин хувь: {Path.GetFileName(saved)}";
            return WarKeyProfile.CreateDefault();
        }
        catch (Exception ex)
        {
            // The file may be perfectly good and merely locked, or on a drive that just went away.
            // Do not touch it, and do not let this session overwrite it.
            ReadOnly = true;
            LoadWarning = "Тохиргооны файлыг нээж чадсангүй тул түр тохиргоогоор ажиллаж байна. " +
                          "Өөрчлөлт хадгалагдахгүй — аппаа хааж дахин нээнэ үү. " +
                          $"({ex.GetType().Name})";
            return WarKeyProfile.CreateDefault();
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
            return;

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
