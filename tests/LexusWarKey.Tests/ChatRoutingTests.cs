using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class ChatRoutingTests
{
    [Fact]
    public void QuickChat_decision_ignores_legacy_allies_only_profile_value()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros[0].Message = "-clear";
        profile.ChatMacros[0].AlliesOnly = true; // legacy profile value is ignored

        var decision = new RemapEngine(() => profile, () => true)
            .Decide(profile.ChatMacros[0].HotkeyVk, true, false, false);

        Assert.Equal(RemapAction.SendChat, decision.Action);
        Assert.Equal(new[] { "-clear" }, decision.ChatLines);
    }
}
