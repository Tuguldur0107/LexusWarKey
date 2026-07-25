using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The allies flag decides who reads the message. Getting it backwards does not fail
/// loudly — it just quietly shows the enemy team what you told your own, which is why it is
/// pinned down here rather than left to the one branch in KeyboardHookService.</summary>
public class ChatRoutingTests
{
    private static RemapEngine EngineFor(WarKeyProfile profile) => new(() => profile, () => true);

    [Fact]
    public void A_macro_marked_team_only_carries_that_through_to_the_decision()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros[0].AlliesOnly = true;

        var decision = EngineFor(profile).Decide(profile.ChatMacros[0].HotkeyVk, true, false, false);

        Assert.Equal(RemapAction.SendChat, decision.Action);
        Assert.True(decision.AlliesOnly);
    }

    [Fact]
    public void An_unmarked_macro_goes_to_everyone()
    {
        var profile = WarKeyProfile.CreateDefault();

        var decision = EngineFor(profile).Decide(profile.ChatMacros[0].HotkeyVk, true, false, false);

        Assert.Equal(RemapAction.SendChat, decision.Action);
        Assert.False(decision.AlliesOnly);
    }

    [Fact]
    public void The_shipped_default_macro_is_public_so_nothing_is_hidden_without_being_asked_for()
    {
        Assert.False(WarKeyProfile.CreateDefault().ChatMacros[0].AlliesOnly);
    }
}
