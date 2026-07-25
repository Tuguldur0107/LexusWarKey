using System.IO;
using System.Text.Json;
using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The profile is the only thing the user actually owns in this app — every binding,
/// macro and the command-card calibration. These tests cover the ways it was previously possible
/// to lose all of it, or to be locked out of the app entirely, without any warning.
///
/// Everything runs against a temporary directory; the real %LocalAppData% is never touched.</summary>
public class ProfileResilienceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "LexusWarKeyTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ProfileStore Store() => new(_root);

    private void WriteProfile(string json)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), json);
    }

    [Fact]
    public void A_null_member_in_the_file_does_not_crash_the_app_on_every_launch()
    {
        // Hand-edited (or half-migrated) files really do look like this, and this exact shape
        // used to throw NullReferenceException out of Load and take the whole app down at start.
        WriteProfile("""{ "inventory": null, "skills": null, "chatMacros": null, "commandCard": null }""");

        var profile = Store().Load();

        Assert.Equal(WarKeyProfile.InventorySlots, profile.Inventory.Count);
        Assert.Equal(WarKeyProfile.SkillSlots, profile.Skills.Count);
        Assert.NotNull(profile.ChatMacros);
        Assert.NotNull(profile.CommandCard);
    }

    [Fact]
    public void A_macro_with_null_messages_is_healed_rather_than_throwing()
    {
        WriteProfile("""{ "chatMacros": [ { "hotkeyVk": 113, "messages": null, "enabled": true } ] }""");

        var profile = Store().Load();

        Assert.Empty(profile.ChatMacros[0].Messages);
        Assert.Empty(RemapEngine.FindConflicts(profile));
    }

    [Fact]
    public void Genuinely_corrupt_content_is_kept_aside_and_reported_not_silently_discarded()
    {
        WriteProfile("this is not json at all {{{");

        var store = Store();
        store.Load();

        Assert.NotNull(store.LoadWarning);
        Assert.False(store.ReadOnly);
        Assert.NotEmpty(Directory.GetFiles(_root, "profile.json.corrupt-*"));
    }

    [Fact]
    public void A_locked_file_is_left_alone_and_saving_is_refused_for_the_session()
    {
        WriteProfile(JsonSerializer.Serialize(WarKeyProfile.CreateDefault()));
        var path = Path.Combine(_root, "profile.json");
        var original = File.ReadAllText(path);

        var store = Store();
        // An antivirus or backup agent holding the file open is transient and common. Treating
        // it as corruption is how a perfectly good profile used to get overwritten with defaults.
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Load();
        }

        Assert.True(store.ReadOnly);
        Assert.NotNull(store.LoadWarning);

        store.Save(WarKeyProfile.CreateDefault()); // must be a no-op, not an overwrite
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_root, "profile.json.corrupt-*"));
    }

    [Fact]
    public void A_saved_profile_round_trips_with_its_bindings_intact()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[2].FromVk = 'R';
        profile.Skills[2].Enabled = true;
        profile.ChatMacros[0].AlliesOnly = true;
        profile.CommandCard = new CommandCard
        {
            TopLeftX = 900, TopLeftY = 760, BottomRightX = 1050, BottomRightY = 820,
        };

        var store = Store();
        store.Save(profile);
        var loaded = store.Load();

        Assert.Equal('R', loaded.Skills[2].FromVk);
        Assert.True(loaded.ChatMacros[0].AlliesOnly);
        Assert.True(loaded.CommandCard.IsCalibrated);
        Assert.Null(store.LoadWarning);
        Assert.False(store.ReadOnly);
    }

    [Fact]
    public void Saving_leaves_no_temp_file_behind()
    {
        var store = Store();
        store.Save(WarKeyProfile.CreateDefault());

        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void A_missing_file_is_a_first_run_not_a_failure()
    {
        var store = Store();
        var profile = store.Load();

        Assert.Null(store.LoadWarning);
        Assert.False(store.ReadOnly);
        Assert.Equal(WarKeyProfile.InventorySlots, profile.Inventory.Count);
    }
}
