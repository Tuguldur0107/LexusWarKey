using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The updater downloads an executable and runs it. The URL pin is the main defence;
/// the published hash is the second, and it is only worth having if it is actually read from the
/// release notes — which is a different host from the CDN that serves the asset.</summary>
public class UpdateVerificationTests
{
    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Theory]
    [InlineData("SHA256: 9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")]
    [InlineData("Release notes here\n\nSHA256: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08\n")]
    [InlineData("sha-256 = 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void The_hash_is_found_however_the_notes_are_formatted(string notes)
    {
        Assert.Equal(Hash, UpdateChecker.ExtractSha256(notes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No hash in these notes at all")]
    [InlineData("SHA256: tooshort")]
    [InlineData("SHA256: zzzzd081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void Anything_that_is_not_a_real_hash_reads_as_absent(string? notes)
    {
        Assert.Null(UpdateChecker.ExtractSha256(notes));
    }

    [Fact]
    public void A_release_carrying_a_hash_exposes_it_on_the_update_info()
    {
        var json = $$"""
        {
          "tag_name": "v9.9.9",
          "html_url": "https://github.com/Tuguldur0107/LexusWarKey/releases/tag/v9.9.9",
          "body": "What's new\n\nSHA256: {{Hash}}",
          "assets": [{
            "name": "LexusWarKey.exe",
            "size": 71665709,
            "browser_download_url": "https://github.com/Tuguldur0107/LexusWarKey/releases/download/v9.9.9/LexusWarKey.exe"
          }]
        }
        """;

        var info = UpdateChecker.ParseIfNewer(json, "1.0.0");

        Assert.NotNull(info);
        Assert.Equal(Hash, info!.Sha256);
    }

    [Fact]
    public void A_release_without_a_hash_still_parses_but_says_so()
    {
        var json = """
        {
          "tag_name": "v9.9.9",
          "html_url": "https://github.com/Tuguldur0107/LexusWarKey/releases/tag/v9.9.9",
          "body": "no checksum here",
          "assets": [{
            "name": "LexusWarKey.exe",
            "size": 10,
            "browser_download_url": "https://github.com/Tuguldur0107/LexusWarKey/releases/download/v9.9.9/LexusWarKey.exe"
          }]
        }
        """;

        var info = UpdateChecker.ParseIfNewer(json, "1.0.0");

        // Parsing must not fail — the installer is what refuses, with an explanation the user reads.
        Assert.NotNull(info);
        Assert.Null(info!.Sha256);
    }
}
