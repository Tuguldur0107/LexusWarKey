using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Every state in here is one a real profile was found in. The app used to describe each
/// of them accurately and uselessly — naming the fault without naming the fix — so these tests
/// assert that the message tells the user what to actually do.</summary>
public class CalibrationGuidanceTests
{
    [Fact]
    public void Skills_bound_before_calibrating_are_told_the_game_must_be_open()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[7].FromVk = 'Q';
        profile.Skills[7].Enabled = true;

        var problem = Assert.Single(RemapEngine.FindDeadBindings(profile), p => p.Contains("командын карт"));

        Assert.Contains("Warcraft III", problem);
        Assert.Contains("Ctrl + F6", problem);
    }

    [Fact]
    public void Several_macros_on_one_key_say_which_one_wins()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros.Clear();
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "-hhn" } });
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "-sddon" } });
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "/fps" } });

        var problem = Assert.Single(RemapEngine.FindDeadBindings(profile), p => p.Contains("макро"));

        Assert.Contains("F5", problem);
        Assert.Contains("3 макро", problem);
        Assert.Contains("-hhn", problem);   // the one that actually fires, named
    }

    [Fact]
    public void Only_the_first_macro_on_a_key_is_the_one_that_fires()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros.Clear();
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "-hhn" } });
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "/fps" } });

        var decision = new RemapEngine(() => profile, () => true).Decide(VirtualKeys.F5, true, false, false);

        Assert.Equal(RemapAction.SendChat, decision.Action);
        Assert.Equal(new[] { "-hhn" }, decision.ChatLines);
    }

    [Fact]
    public void One_macro_per_key_raises_nothing()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros.Clear();
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "-hhn", "-sddon", "/fps" } });

        Assert.DoesNotContain(RemapEngine.FindDeadBindings(profile), p => p.Contains("макро"));
        Assert.Empty(RemapEngine.FindConflicts(profile));
    }

    [Fact]
    public void Disabled_duplicates_are_not_worth_mentioning()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros.Clear();
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "-hhn" } });
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "/fps" }, Enabled = false });

        Assert.DoesNotContain(RemapEngine.FindDeadBindings(profile), p => p.Contains("макро"));
    }
}
