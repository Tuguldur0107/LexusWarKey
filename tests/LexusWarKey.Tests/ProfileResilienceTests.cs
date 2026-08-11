using System.IO;
using System.Text.Json;
using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class ProfileResilienceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "LexusWarKeyTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private ProfileStore Store() => new(_root);

    private void WriteProfile(string json)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.json"), json);
    }

    [Fact]
    public void Null_members_in_the_file_do_not_crash_startup()
    {
        WriteProfile("""{ "inventory": null, "skills": null, "chatMacros": null }""");

        var profile = Store().Load();

        Assert.Equal(WarKeyProfile.SkillSlots, profile.Skills.Count);
        Assert.Equal(WarKeyProfile.QuickChatSlots, profile.ChatMacros.Count);
    }

    [Fact]
    public void A_macro_with_null_messages_is_healed()
    {
        WriteProfile("""{ "chatMacros": [ { "hotkeyVk": 113, "messages": null, "enabled": true } ] }""");

        var profile = Store().Load();

        Assert.Empty(profile.ChatMacros[0].Messages);
        Assert.Empty(RemapEngine.FindConflicts(profile));
    }

    [Fact]
    public void Corrupt_content_is_quarantined_and_reported()
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
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Load();
        }

        Assert.True(store.ReadOnly);
        Assert.NotNull(store.LoadWarning);

        var replacement = WarKeyProfile.CreateDefault();
        replacement.Skills[0] = new KeyMap { FromVk = 'Q', ToVk = 'T', Enabled = true };
        store.Save(replacement);

        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_root, "profile.json.corrupt-*"));
    }

    [Fact]
    public void A_saved_profile_round_trips_with_its_bindings_intact()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[5].FromVk = 'R';
        profile.Skills[5].ToVk = 'D';
        profile.Skills[5].Enabled = true;
        profile.ChatMacros[0].Message = "-clear";

        var store = Store();
        store.Save(profile);
        var loaded = store.Load();

        Assert.Equal('R', loaded.Skills[5].FromVk);
        Assert.Equal('D', loaded.Skills[5].ToVk);
        Assert.Equal("-clear", loaded.ChatMacros[0].Message);
        Assert.Null(store.LoadWarning);
        Assert.False(store.ReadOnly);
    }

    [Fact]
    public void Saving_leaves_no_temp_file_behind()
    {
        var store = Store();
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[0] = new KeyMap { FromVk = 'Q', ToVk = 'T', Enabled = true };

        store.Save(profile);

        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void Old_unknown_members_are_skipped_not_treated_as_corruption()
    {
        WriteProfile("""
                     {
                       "inventory": [ { "fromVk": 32, "toVk": 103, "enabled": true } ],
                       "skills": [ { "fromVk": 81, "toVk": 84, "enabled": true } ],
                       "activationToken": "old",
                       "autoInstallUpdates": true,
                       "enabled": true
                     }
                     """);

        var store = Store();
        var profile = store.Load();

        Assert.Null(store.LoadWarning);
        Assert.Equal('Q', profile.Skills[0].FromVk);
        Assert.Equal('T', profile.Skills[0].ToVk);
    }

    [Fact]
    public void A_missing_file_is_a_first_run_not_a_failure()
    {
        var store = Store();
        var profile = store.Load();

        Assert.Null(store.LoadWarning);
        Assert.False(store.ReadOnly);
        Assert.Equal(WarKeyProfile.SkillSlots, profile.Skills.Count);
        Assert.Equal(WarKeyProfile.QuickChatSlots, profile.ChatMacros.Count);
    }
}
