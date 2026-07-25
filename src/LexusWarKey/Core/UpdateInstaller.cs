using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace LexusWarKey.Core;

/// <summary>Downloads a release the user has agreed to install and swaps it in.
///
/// A running single-file exe cannot overwrite itself, so the sequence is:
/// download beside it → rename the running exe out of the way → move the new one in →
/// relaunch → the old file is cleaned up on the next start. Every step is verified, and the
/// download is refused unless the URL belongs to this app's own GitHub releases.</summary>
public sealed class UpdateInstaller
{
    private const string BackupSuffix = ".old";
    private const string StagingSuffix = ".new";

    private readonly HttpClient _http;

    public UpdateInstaller(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{UpdateChecker.Repo}/{UpdateChecker.CurrentVersion}");
    }

    public static string CurrentExePath => Environment.ProcessPath ?? "";

    /// <summary>Removes the previous version left behind by an earlier update.</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            var old = CurrentExePath + BackupSuffix;
            if (File.Exists(old))
                File.Delete(old);
        }
        catch
        {
            // still in use or locked — it will be retried next start
        }
    }

    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!UpdateChecker.IsTrustedUrl(update.DownloadUrl))
            throw new InvalidOperationException("Update URL is not from the official release page.");

        var target = CurrentExePath + StagingSuffix;
        if (File.Exists(target))
            File.Delete(target);

        using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? update.SizeBytes;
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var file = File.Create(target))
        {
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0)
                    progress?.Report(Math.Min(1.0, read / (double)total));
            }
        }

        // A truncated download must never be swapped in. The expected size comes from the API
        // response, not from the download itself, so an absent length is suspicious in its own
        // right rather than a reason to skip the check.
        var downloaded = new FileInfo(target).Length;
        var expected = total > 0 ? total : update.SizeBytes;
        if (expected <= 0 || downloaded != expected)
        {
            File.Delete(target);
            throw new InvalidOperationException($"Download incomplete ({downloaded} of {expected} bytes).");
        }

        VerifyHash(target, update.Sha256);
        return target;
    }

    /// <summary>Refuses anything whose hash the release did not vouch for. Deleting the staged
    /// file on mismatch matters as much as the throw: leaving a rejected executable next to the
    /// real one invites it being run by hand later.</summary>
    private static void VerifyHash(string path, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex))
        {
            File.Delete(path);
            throw new InvalidOperationException(
                "This release publishes no SHA256, so the download cannot be verified. " +
                "Install it by hand from the release page if you trust it.");
        }

        string actual;
        using (var stream = File.OpenRead(path))
            actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));

        if (!actual.Equals(expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException(
                $"The download does not match the hash GitHub published for it (expected {expectedHex}, got {actual.ToLowerInvariant()}).");
        }
    }

    /// <summary>Swaps the staged file in and restarts. Throws before touching anything if the
    /// staged file is missing, so a failed download can never leave the app broken.
    ///
    /// <paramref name="beforeRestart"/> runs only once the swap has actually succeeded. The caller
    /// must not shut itself down any earlier: every move below can throw, and a caller that has
    /// already released its keyboard hook would then sit there looking alive but doing nothing.</summary>
    public void ApplyAndRestart(string stagedPath, Action? beforeRestart = null)
    {
        if (!File.Exists(stagedPath))
            throw new FileNotFoundException("The downloaded update is missing.", stagedPath);

        var current = CurrentExePath;
        var backup = current + BackupSuffix;

        if (File.Exists(backup))
            File.Delete(backup);

        File.Move(current, backup);
        try
        {
            File.Move(stagedPath, current);
        }
        catch
        {
            File.Move(backup, current); // put the working version back
            throw;
        }

        beforeRestart?.Invoke();
        Process.Start(new ProcessStartInfo(current) { UseShellExecute = true });
        Environment.Exit(0);
    }
}
