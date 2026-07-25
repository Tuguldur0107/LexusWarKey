using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The updater downloads and runs an executable, so its guard rails matter more than
/// its happy path: only this repository's own release assets may ever be accepted.</summary>
public class UpdateCheckerTests
{
    private static string Release(string tag, string assetName = "LexusWarKey.exe", string? url = null,
        bool draft = false, bool prerelease = false) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "draft": {{draft.ToString().ToLowerInvariant()}},
          "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
          "html_url": "https://github.com/Tuguldur0107/LexusWarKey/releases/tag/{{tag}}",
          "body": "notes",
          "assets": [
            {
              "name": "{{assetName}}",
              "size": 71000000,
              "browser_download_url": "{{url ?? $"https://github.com/Tuguldur0107/LexusWarKey/releases/download/{tag}/LexusWarKey.exe"}}"
            }
          ]
        }
        """;

    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.0", "1.0.0", false)]
    public void Version_comparison_only_moves_forward(string tag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(tag, current));
    }

    [Fact]
    public void A_newer_release_is_offered_with_its_download()
    {
        var info = UpdateChecker.ParseIfNewer(Release("v1.2.0"), "1.0.0");

        Assert.NotNull(info);
        Assert.Equal("1.2.0", info!.Version);
        Assert.StartsWith("https://github.com/Tuguldur0107/LexusWarKey/releases/download/", info.DownloadUrl);
    }

    [Fact]
    public void The_same_version_is_not_offered()
    {
        Assert.Null(UpdateChecker.ParseIfNewer(Release("v1.0.0"), "1.0.0"));
    }

    [Fact]
    public void Drafts_and_prereleases_are_ignored()
    {
        Assert.Null(UpdateChecker.ParseIfNewer(Release("v9.0.0", draft: true), "1.0.0"));
        Assert.Null(UpdateChecker.ParseIfNewer(Release("v9.0.0", prerelease: true), "1.0.0"));
    }

    [Fact]
    public void A_release_without_our_exe_is_ignored()
    {
        Assert.Null(UpdateChecker.ParseIfNewer(Release("v2.0.0", assetName: "something-else.zip"), "1.0.0"));
    }

    [Theory]
    [InlineData("https://evil.example.com/LexusWarKey.exe")]                                  // wrong host
    [InlineData("http://github.com/Tuguldur0107/LexusWarKey/releases/download/v2/x.exe")]     // not https
    [InlineData("https://github.com/someoneelse/LexusWarKey/releases/download/v2/x.exe")]     // wrong owner
    [InlineData("https://github.com/Tuguldur0107/OtherRepo/releases/download/v2/x.exe")]      // wrong repo
    [InlineData("https://github.com/Tuguldur0107/LexusWarKey/raw/main/x.exe")]                // not a release asset
    public void Downloads_from_anywhere_but_our_own_releases_are_refused(string url)
    {
        Assert.False(UpdateChecker.IsTrustedUrl(url));
        Assert.Null(UpdateChecker.ParseIfNewer(Release("v2.0.0", url: url), "1.0.0"));
    }

    [Fact]
    public void The_official_release_url_is_trusted()
    {
        Assert.True(UpdateChecker.IsTrustedUrl(
            "https://github.com/Tuguldur0107/LexusWarKey/releases/download/v1.2.0/LexusWarKey.exe"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\": \"\"}")]
    [InlineData("{\"tag_name\": \"banana\"}")]
    public void Malformed_or_odd_release_data_never_throws_into_the_app(string json)
    {
        var result = Record.Exception(() =>
        {
            try { UpdateChecker.ParseIfNewer(json, "1.0.0"); }
            catch (System.Text.Json.JsonException) { /* the caller wraps this */ }
        });
        Assert.Null(result);
    }
}
