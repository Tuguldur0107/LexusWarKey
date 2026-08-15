using System.IO;
using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class FieldProfileRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "LexusWarKeyTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private const string OldFeatureRichJson = """
    {
      "inventory": [
        {"fromVk": 32, "toVk": 103, "enabled": true}, {"fromVk": 9, "toVk": 104, "enabled": true}],
      "skills": [
        {"fromVk": 0, "toVk": 0, "enabled": false}, {"fromVk": 0, "toVk": 0, "enabled": false},
        {"fromVk": 0, "toVk": 0, "enabled": false}, {"fromVk": 0, "toVk": 0, "enabled": false},
        {"fromVk": 69, "toVk": 65, "enabled": true}, {"fromVk": 87, "toVk": 83, "enabled": true},
        {"fromVk": 84, "toVk": 68, "enabled": true}, {"fromVk": 82, "toVk": 70, "enabled": true},
        {"fromVk": 72, "toVk": 71, "enabled": true}, {"fromVk": 81, "toVk": 84, "enabled": true},
        {"fromVk": 67, "toVk": 86, "enabled": true}, {"fromVk": 86, "toVk": 66, "enabled": true}],
      "chatMacros": [
        {"hotkeyVk": 113, "messages": ["-clear"], "enabled": true, "alliesOnly": false},
        {"hotkeyVk": 116, "messages": ["-sds6fnboulso","-hhn","-sddon"], "enabled": true, "alliesOnly": false},
        {"hotkeyVk": 117, "messages": ["/fps"], "enabled": true, "alliesOnly": false}],
      "commandCard": {"topLeftX": 2022, "topLeftY": 835},
      "skillsUsePosition": true, "moveCursorForClicks": false, "usePostedClicks": true,
      "overlayLeft": 1150.0, "overlayTop": 440.0,
      "startWithWindows": true, "autoInstallUpdates": false, "minimiseToTray": true,
      "onlyWhenGameFocused": true, "activationToken": "old", "enabled": true
    }
    """;

    [Fact]
    public void Old_feature_rich_profile_loads_into_the_simplified_shape()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), OldFeatureRichJson);

        var store = new ProfileStore(_root);
        var profile = store.Load();

        Assert.Null(store.LoadWarning);
        Assert.Equal(WarKeyProfile.SkillSlots, profile.Skills.Count);
        Assert.Equal(new[] { 'E', 'W', 'T', 'R', 'H', 'Q', 'C', 'V' },
            profile.Skills.Select(m => (char)m.FromVk).ToArray());
        // QuickChat is an open list now, so all three of the old macros survive (each trimmed to its
        // first message).
        Assert.Equal(3, profile.ChatMacros.Count);
        Assert.Equal("-clear", profile.ChatMacros[0].Message);
        Assert.Equal("-sds6fnboulso", profile.ChatMacros[1].Message);
        Assert.Equal("/fps", profile.ChatMacros[2].Message);
    }

    [Fact]
    public void A_vanished_profile_comes_back_from_the_backup()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), OldFeatureRichJson);

        var store = new ProfileStore(_root);
        store.Save(store.Load());
        File.Delete(Path.Combine(_root, "profile.json"));

        var restored = new ProfileStore(_root).Load();

        Assert.Equal('Q', (char)restored.Skills[5].FromVk);
        Assert.Equal('E', (char)restored.Skills[0].FromVk);
    }

    [Fact]
    public void A_corrupted_profile_comes_back_from_the_backup_too()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), OldFeatureRichJson);

        var store = new ProfileStore(_root);
        store.Save(store.Load());
        File.WriteAllText(Path.Combine(_root, "profile.json"), "{{{ broken");

        var next = new ProfileStore(_root);
        var restored = next.Load();

        Assert.Equal('Q', (char)restored.Skills[5].FromVk);
        Assert.Contains("backup", next.LoadWarning!);
        Assert.NotEmpty(Directory.GetFiles(_root, "profile.json.corrupt-*"));
    }

    [Fact]
    public void A_freshly_reset_profile_never_eats_the_backup()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), OldFeatureRichJson);

        var store = new ProfileStore(_root);
        store.Save(store.Load());

        store.Save(WarKeyProfile.CreateDefault());

        File.Delete(Path.Combine(_root, "profile.json"));
        var restored = new ProfileStore(_root).Load();

        Assert.Equal('Q', (char)restored.Skills[5].FromVk);
    }
}
