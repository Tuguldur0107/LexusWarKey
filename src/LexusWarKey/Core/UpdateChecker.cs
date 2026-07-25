using System.Net.Http;
using System.Text.Json;

namespace LexusWarKey.Core;

/// <summary><paramref name="Sha256"/> is the hash the release notes claim the exe has, or null
/// if the release does not publish one. It is read from api.github.com while the exe itself comes
/// from the release CDN, so it is not merely a checksum of whatever the same responder sent.</summary>
public sealed record UpdateInfo(
    string Version, string DownloadUrl, long SizeBytes, string ReleaseUrl, string Notes, string? Sha256 = null);

/// <summary>Asks GitHub whether a newer release exists.
///
/// Deliberate limits: it only ever talks to this app's own repository over HTTPS, it only
/// reads metadata here (nothing is downloaded or run at this stage), and the caller decides
/// whether to install. Nothing happens behind the user's back.</summary>
public sealed class UpdateChecker
{
    public const string Owner = "Tuguldur0107";
    public const string Repo = "LexusWarKey";
    public const string AssetName = "LexusWarKey.exe";

    private static readonly Uri LatestReleaseApi =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private readonly HttpClient _http;

    public UpdateChecker(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Repo}/{CurrentVersion}");
    }

    public static string CurrentVersion =>
        typeof(UpdateChecker).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>True when the last <see cref="CheckAsync"/> could not reach GitHub at all.
    /// Without it, "no update" and "could not ask" look identical, and the app cheerfully tells
    /// an offline user they are on the latest version.</summary>
    public bool LastCheckFailed { get; private set; }

    /// <summary>Null when we are up to date, the check failed, or the release has no exe.
    /// Check <see cref="LastCheckFailed"/> to tell the first case from the second.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            LastCheckFailed = false;
            return ParseIfNewer(json, CurrentVersion);
        }
        catch
        {
            LastCheckFailed = true;
            return null; // offline, rate-limited, no releases yet
        }
    }

    /// <summary>Pure parsing so the version comparison is testable without any network.</summary>
    public static UpdateInfo? ParseIfNewer(string releaseJson, string currentVersion)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            return null;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        if (!IsNewer(tag, currentVersion))
            return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (!string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url) || !IsTrustedUrl(url))
                return null;

            var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new UpdateInfo(Normalise(tag), url, size, page, notes, ExtractSha256(notes));
        }
        return null;
    }

    /// <summary>Pulls the "SHA256: &lt;hex&gt;" line the release workflow writes into the notes.
    /// Null when the release does not publish one — the installer then refuses rather than
    /// running an unverified executable.</summary>
    public static string? ExtractSha256(string? notes)
    {
        if (string.IsNullOrEmpty(notes))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            notes, @"SHA-?256\s*[:=]\s*([0-9a-fA-F]{64})\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>Only ever accept a download that really comes from this repository's releases.</summary>
    public static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    public static bool IsNewer(string candidateTag, string currentVersion)
    {
        var candidate = ParseVersion(candidateTag);
        var current = ParseVersion(currentVersion);
        return candidate is not null && current is not null && candidate > current;
    }

    private static string Normalise(string tag) => tag.TrimStart('v', 'V');

    private static Version? ParseVersion(string text)
    {
        var cleaned = Normalise(text.Trim());
        var cut = cleaned.IndexOfAny(new[] { '-', '+' });
        if (cut > 0)
            cleaned = cleaned[..cut];
        return Version.TryParse(cleaned, out var v) ? v : null;
    }
}
