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

    [Fact]
    public void One_key_can_send_several_messages_in_order()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros[0].HotkeyVk = VirtualKeys.F2;
        profile.ChatMacros[0].Messages = new() { "-clear", "-ii", "-hhn" };

        var decision = new RemapEngine(() => profile, () => true)
            .Decide(VirtualKeys.F2, true, false, false);

        Assert.Equal(RemapAction.SendChat, decision.Action);
        Assert.Equal(new[] { "-clear", "-ii", "-hhn" }, decision.ChatLines);
    }

    [Fact]
    public void Blank_message_lines_are_dropped_from_what_is_sent()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros[0].HotkeyVk = VirtualKeys.F2;
        profile.ChatMacros[0].Messages = new() { "gg", "", "  ", "wp" };

        var decision = new RemapEngine(() => profile, () => true)
            .Decide(VirtualKeys.F2, true, false, false);

        Assert.Equal(new[] { "gg", "wp" }, decision.ChatLines);
    }
}
