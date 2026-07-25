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

    public WarKeyProfile Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return WarKeyProfile.CreateDefault();
            var loaded = JsonSerializer.Deserialize<WarKeyProfile>(File.ReadAllText(FilePath), Options);
            if (loaded is null)
                return WarKeyProfile.CreateDefault();
            loaded.NormaliseSlots();
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryQuarantine();
            return WarKeyProfile.CreateDefault();
        }
    }

    public void Save(WarKeyProfile profile)
    {
        Directory.CreateDirectory(Root);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    private void TryQuarantine()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Move(FilePath, $"{FilePath}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch
        {
            // best effort
        }
    }
}
