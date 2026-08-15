using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class MigrationTests
{
    [Fact]
    public void A_12_slot_card_profile_keeps_the_lower_two_rows_on_the_4x2_grid()
    {
        var profile = WarKeyProfile.CreateDefault();
        while (profile.Skills.Count < 12)
            profile.Skills.Add(new KeyMap());
        profile.Skills[4].FromVk = 'Q';
        profile.Skills[4].Enabled = true;
        profile.Skills[11].FromVk = 'V';
        profile.Skills[11].Enabled = true;

        profile.NormaliseSlots();

        // Truncating from the end would have thrown away every binding the user had.
        Assert.Equal(WarKeyProfile.SkillSlots, profile.Skills.Count);
        Assert.Equal('Q', profile.Skills[0].FromVk);
        Assert.Equal('V', profile.Skills[7].FromVk);
    }

    [Fact]
    public void The_12_slot_migration_drops_the_old_CustomKeys_letters()
    {
        // Those builds seeded ToVk from the generated CustomKeys.txt scheme, which assumed the
        // file was installed in the game folder. Nothing installs it now, so carrying the
        // letters over would leave a grid that looks configured and casts the wrong ability.
        var profile = WarKeyProfile.CreateDefault();
        while (profile.Skills.Count < 12)
            profile.Skills.Add(new KeyMap());
        profile.Skills[5].FromVk = 'A';
        profile.Skills[5].ToVk = 'Q';      // scheme letter, not this match's ability letter
        profile.Skills[5].Enabled = true;

        profile.NormaliseSlots();

        Assert.Equal('A', profile.Skills[1].FromVk);   // the player's own key is theirs to keep
        Assert.Equal(0, profile.Skills[1].ToVk);       // the stale letter is not
        Assert.False(profile.Skills[1].IsUsable);      // so it cannot cast anything wrong
    }

    [Fact]
    public void All_quickchat_macros_are_kept_now_that_the_list_is_open()
    {
        var profile = new WarKeyProfile();
        foreach (var line in new[] { "-clear", "-ii", "-hhn", "/fps" })
            profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F2, Messages = { line } });

        profile.NormaliseSlots();

        Assert.Equal(4, profile.ChatMacros.Count);
        Assert.Equal(new[] { "-clear", "-ii", "-hhn", "/fps" }, profile.ChatMacros.Select(m => m.Message));
    }

    [Fact]
    public void Multi_line_quickchat_macro_keeps_only_its_first_message()
    {
        var profile = new WarKeyProfile();
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "-clear", "-ii" } });

        profile.NormaliseSlots();

        Assert.Single(profile.ChatMacros);
        Assert.Equal("-clear", profile.ChatMacros[0].Message);
        Assert.Single(profile.ChatMacros[0].Messages);
    }

    [Fact]
    public void A_new_profile_is_born_with_the_grid_and_two_chat_slots_it_will_keep()
    {
        var profile = WarKeyProfile.CreateDefault();

        profile.NormaliseSlots();

        Assert.Equal(WarKeyProfile.SkillSlots, profile.Skills.Count);
        Assert.Equal(WarKeyProfile.QuickChatSlots, profile.ChatMacros.Count);
    }
}
