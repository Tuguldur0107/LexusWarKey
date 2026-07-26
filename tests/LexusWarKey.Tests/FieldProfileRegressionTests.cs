using System.IO;
using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The user's real profile — six bound skills, hand-placed ring positions — was found
/// reset to near-defaults after an update, with no quarantine file left behind to explain it.
/// This pins the exact pre-wipe document against the current loader: if any current or future
/// code path drops those bindings on load, this fails before it ships.</summary>
public class FieldProfileRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "LexusWarKeyTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Verbatim structure of the profile as it existed before the reset.</summary>
    private const string PreWipeJson = """
    {
      "inventory": [
        {"fromVk": 32, "toVk": 103, "enabled": true}, {"fromVk": 9, "toVk": 104, "enabled": true},
        {"fromVk": 0, "toVk": 105, "enabled": false}, {"fromVk": 0, "toVk": 100, "enabled": false},
        {"fromVk": 0, "toVk": 101, "enabled": false}, {"fromVk": 0, "toVk": 102, "enabled": false}],
      "skills": [
        {"fromVk": 0, "toVk": 0, "enabled": false}, {"fromVk": 0, "toVk": 0, "enabled": false},
        {"fromVk": 0, "toVk": 0, "enabled": false}, {"fromVk": 0, "toVk": 0, "enabled": false},
        {"fromVk": 0, "toVk": 0, "enabled": false}, {"fromVk": 69, "toVk": 0, "enabled": true},
        {"fromVk": 87, "toVk": 0, "enabled": true}, {"fromVk": 0, "toVk": 0, "enabled": false},
        {"fromVk": 84, "toVk": 0, "enabled": true}, {"fromVk": 82, "toVk": 0, "enabled": true},
        {"fromVk": 72, "toVk": 0, "enabled": true}, {"fromVk": 81, "toVk": 0, "enabled": true}],
      "chatMacros": [
        {"hotkeyVk": 113, "messages": ["-clear"], "enabled": true, "alliesOnly": false},
        {"hotkeyVk": 116, "messages": ["-sds6fnboulso","-hhn","-sddon","-water 0 0 0","/distance 2300","/fps"], "enabled": true, "alliesOnly": false}],
      "commandCard": {"topLeftX": 2022, "topLeftY": 835, "bottomRightX": 2301, "bottomRightY": 974,
        "overrides": [{"x":2038,"y":1306},{"x":2178,"y":1297},{"x":2310,"y":1298},{"x":2439,"y":1299},
                      {"x":2035,"y":1413},{"x":2181,"y":1414},{"x":2314,"y":1410},{"x":2458,"y":1418},
                      {"x":2040,"y":1528},{"x":2180,"y":1533},{"x":2320,"y":1534},{"x":2462,"y":1539}]},
      "skillsUsePosition": true, "moveCursorForClicks": false, "usePostedClicks": true,
      "overlayLeft": 1150.0, "overlayTop": 440.0,
      "startWithWindows": true, "autoInstallUpdates": false, "minimiseToTray": true,
      "onlyWhenGameFocused": true, "enabled": true
    }
    """;

    [Fact]
    public void The_users_pre_wipe_profile_loads_with_every_binding_intact()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), PreWipeJson);

        var store = new ProfileStore(_root);
        var profile = store.Load();

        Assert.Null(store.LoadWarning);
        Assert.Equal(new[] { (5, 'E'), (6, 'W'), (8, 'T'), (9, 'R'), (10, 'H'), (11, 'Q') },
            profile.Skills.Select((m, i) => (i, (char)m.FromVk)).Where(p => p.Item2 != (char)0).ToArray());
        Assert.True(profile.CommandCard.IsCalibrated);
        Assert.NotNull(profile.CommandCard.Overrides);
        Assert.Equal(12, profile.CommandCard.Overrides!.Count);
        Assert.Equal(2, profile.ChatMacros.Count);
        Assert.Equal(6, profile.ChatMacros[1].Messages.Count);
    }

    [Fact]
    public void A_vanished_profile_comes_back_from_the_backup()
    {
        // The field incident: profile gone (or reset) with no quarantine file to explain it.
        // With a backup in place, the answer to "why did it reset" stops mattering as much.
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), PreWipeJson);

        var store = new ProfileStore(_root);
        store.Save(store.Load());                      // a meaningful save refreshes the backup
        File.Delete(Path.Combine(_root, "profile.json"));

        var restored = new ProfileStore(_root).Load();

        Assert.Equal('Q', (char)restored.Skills[11].FromVk);
        Assert.Equal('E', (char)restored.Skills[5].FromVk);
        Assert.NotNull(restored.CommandCard.Overrides);
    }

    [Fact]
    public void A_corrupted_profile_comes_back_from_the_backup_too()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), PreWipeJson);

        var store = new ProfileStore(_root);
        store.Save(store.Load());
        File.WriteAllText(Path.Combine(_root, "profile.json"), "{{{ broken");

        var next = new ProfileStore(_root);
        var restored = next.Load();

        Assert.Equal('Q', (char)restored.Skills[11].FromVk);
        Assert.Contains("нөөц", next.LoadWarning);
        Assert.NotEmpty(Directory.GetFiles(_root, "profile.json.corrupt-*")); // evidence kept
    }

    [Fact]
    public void A_freshly_reset_profile_never_eats_the_backup()
    {
        // Whatever wiped the profile is followed by the app saving a near-default state.
        // That save must leave the backup alone, or the recovery copy dies with the original.
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), PreWipeJson);

        var store = new ProfileStore(_root);
        store.Save(store.Load());                      // good backup exists

        var reset = WarKeyProfile.CreateDefault();
        reset.CommandCard = CommandCard.DefaultFor(2560, 1600); // startup seeding also runs
        store.Save(reset);                             // the post-wipe save

        File.Delete(Path.Combine(_root, "profile.json"));
        var restored = new ProfileStore(_root).Load();

        Assert.Equal('Q', (char)restored.Skills[11].FromVk); // the good copy survived
    }

    [Fact]
    public void Saving_and_reloading_it_changes_nothing_that_matters()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), PreWipeJson);

        var store = new ProfileStore(_root);
        store.Save(store.Load());
        var again = store.Load();

        Assert.Equal('Q', (char)again.Skills[11].FromVk);
        Assert.Equal('E', (char)again.Skills[5].FromVk);
        Assert.True(again.CommandCard.PointFor(8) is { } p && p.Y > 1500); // hand-placed row survives
    }
}
