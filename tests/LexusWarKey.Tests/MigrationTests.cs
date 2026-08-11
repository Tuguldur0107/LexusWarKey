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
        profile.Skills[4].ToVk = 'A';
        profile.Skills[4].Enabled = true;
        profile.Skills[11].FromVk = 'V';
        profile.Skills[11].ToVk = 'D';
        profile.Skills[11].Enabled = true;

        profile.NormaliseSlots();

        Assert.Equal(WarKeyProfile.SkillSlots, profile.Skills.Count);
        Assert.Equal(('Q', 'A'), ((char)profile.Skills[0].FromVk, (char)profile.Skills[0].ToVk));
        Assert.Equal(('V', 'D'), ((char)profile.Skills[7].FromVk, (char)profile.Skills[7].ToVk));
    }

    [Fact]
    public void More_than_two_quickchat_macros_are_trimmed_to_two_slots()
    {
        var profile = new WarKeyProfile();
        foreach (var line in new[] { "-clear", "-ii", "-hhn", "/fps" })
            profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F2, Messages = { line } });

        profile.NormaliseSlots();

        Assert.Equal(2, profile.ChatMacros.Count);
        Assert.Equal("-clear", profile.ChatMacros[0].Message);
        Assert.Equal("-ii", profile.ChatMacros[1].Message);
    }

    [Fact]
    public void Multi_line_quickchat_macro_keeps_only_its_first_message()
    {
        var profile = new WarKeyProfile();
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "-clear", "-ii" } });

        profile.NormaliseSlots();

        Assert.Equal(2, profile.ChatMacros.Count);
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
