using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class ChatLineTests
{
    private static (RemapEngine engine, WarKeyProfile profile) Setup(bool focused = true)
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[0].FromVk = 'Q';
        profile.Skills[0].ToVk = 'T';
        profile.Skills[0].Enabled = true;
        profile.ChatMacros[0].Message = "-clear";
        return (new RemapEngine(() => profile, () => focused), profile);
    }

    private static void Press(RemapEngine engine, int vk)
    {
        engine.ObserveKey(vk, isKeyDown: true);
        engine.ObserveKey(vk, isKeyDown: false);
    }

    [Fact]
    public void Chat_starts_closed_and_keys_are_remapped_normally()
    {
        var (engine, _) = Setup();

        Assert.False(engine.ChatOpen);
        Assert.Equal(RemapAction.SendKey, engine.Decide('Q', true, false, false).Action);
    }

    [Fact]
    public void Enter_opens_the_chat_line_and_every_key_then_passes_through()
    {
        var (engine, _) = Setup();

        Press(engine, VirtualKeys.Enter);

        Assert.True(engine.ChatOpen);
        Assert.Equal(RemapAction.PassThrough, engine.Decide('Q', true, false, false).Action);
    }

    [Fact]
    public void A_second_Enter_sends_the_message_and_remapping_resumes()
    {
        var (engine, _) = Setup();

        Press(engine, VirtualKeys.Enter);
        Press(engine, VirtualKeys.Enter);

        Assert.False(engine.ChatOpen);
        Assert.Equal(RemapAction.SendKey, engine.Decide('Q', true, false, false).Action);
    }

    [Fact]
    public void Escape_abandons_the_message_and_remapping_resumes()
    {
        var (engine, _) = Setup();

        Press(engine, VirtualKeys.Enter);
        Press(engine, VirtualKeys.Escape);

        Assert.False(engine.ChatOpen);
        Assert.Equal(RemapAction.SendKey, engine.Decide('Q', true, false, false).Action);
    }

    [Fact]
    public void QuickChat_does_not_fire_while_the_player_is_writing()
    {
        var (engine, profile) = Setup();
        var hotkey = profile.ChatMacros[0].HotkeyVk;

        Assert.Equal(RemapAction.SendChat, engine.Decide(hotkey, true, false, false).Action);

        Press(engine, VirtualKeys.Enter);

        Assert.Equal(RemapAction.PassThrough, engine.Decide(hotkey, true, false, false).Action);
    }

    [Fact]
    public void Losing_the_game_clears_a_chat_line_that_was_left_open()
    {
        var profile = WarKeyProfile.CreateDefault();
        var focused = true;
        var engine = new RemapEngine(() => profile, () => focused);

        Press(engine, VirtualKeys.Enter);
        Assert.True(engine.ChatOpen);

        focused = false;
        engine.ObserveKey('A', isKeyDown: true);

        Assert.False(engine.ChatOpen);
    }

    [Fact]
    public void ResetChatState_is_the_escape_hatch_when_we_cannot_be_sure()
    {
        var (engine, _) = Setup();

        Press(engine, VirtualKeys.Enter);
        engine.ResetChatState();

        Assert.False(engine.ChatOpen);
    }

    [Fact]
    public void The_apps_own_quickchat_typing_never_moves_the_tracker()
    {
        var (engine, _) = Setup();

        engine.SuspendedForTyping = true;
        Press(engine, VirtualKeys.Enter);
        engine.SuspendedForTyping = false;

        Assert.False(engine.ChatOpen);
    }

    [Fact]
    public void Enter_and_Escape_are_never_remapped_because_they_drive_the_chat_line()
    {
        var (engine, profile) = Setup();
        profile.Skills[1].FromVk = VirtualKeys.Enter;
        profile.Skills[1].ToVk = 'R';
        profile.Skills[1].Enabled = true;
        profile.Skills[2].FromVk = VirtualKeys.Escape;
        profile.Skills[2].ToVk = 'D';
        profile.Skills[2].Enabled = true;

        Assert.Equal(RemapAction.PassThrough, engine.Decide(VirtualKeys.Enter, true, false, false).Action);
        Assert.Equal(RemapAction.PassThrough, engine.Decide(VirtualKeys.Escape, true, false, false).Action);
    }

    [Fact]
    public void A_chat_line_reports_how_long_it_has_been_open_regardless_of_typing()
    {
        var profile = WarKeyProfile.CreateDefault();
        var clock = 1_000L;
        var engine = new RemapEngine(() => profile, () => true, null, () => clock);

        Assert.Equal(TimeSpan.Zero, engine.ChatOpenFor);

        Press(engine, VirtualKeys.Enter);
        Assert.True(engine.ChatOpen);

        clock += 30_000;
        Press(engine, 'W');
        Press(engine, 'E');

        Assert.True(engine.ChatOpenFor > TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Closing_the_line_stops_the_clock()
    {
        var profile = WarKeyProfile.CreateDefault();
        var clock = 1_000L;
        var engine = new RemapEngine(() => profile, () => true, null, () => clock);

        Press(engine, VirtualKeys.Enter);
        clock += 30_000;
        Press(engine, VirtualKeys.Enter);

        Assert.False(engine.ChatOpen);
        Assert.Equal(TimeSpan.Zero, engine.ChatOpenFor);
    }

    [Fact]
    public void Reopening_restarts_the_clock()
    {
        var profile = WarKeyProfile.CreateDefault();
        var clock = 1_000L;
        var engine = new RemapEngine(() => profile, () => true, null, () => clock);

        Press(engine, VirtualKeys.Enter);
        clock += 30_000;
        Press(engine, VirtualKeys.Enter);
        clock += 5_000;
        Press(engine, VirtualKeys.Enter);

        Assert.True(engine.ChatOpen);
        Assert.Equal(TimeSpan.Zero, engine.ChatOpenFor);
    }

    [Fact]
    public void Opening_and_closing_raises_the_event_the_status_line_listens_to()
    {
        var (engine, _) = Setup();
        var seen = new List<bool>();
        engine.ChatOpenChanged += seen.Add;

        Press(engine, VirtualKeys.Enter);
        Press(engine, VirtualKeys.Enter);

        Assert.Equal(new[] { true, false }, seen);
    }
}
