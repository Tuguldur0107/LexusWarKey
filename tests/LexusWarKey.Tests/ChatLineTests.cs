using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Warcraft's chat line takes the keyboard, and the game says nothing about it. If the
/// app keeps remapping while the player types, a Space bound to NumPad7 turns "gg wp" into
/// "gg7wp" and a position-bound letter casts the ability instead of appearing in the sentence.
///
/// The opposite mistake is worse: a tracker stuck on "open" disables the whole app in silence,
/// so these tests pin down when it clears as firmly as when it latches.</summary>
public class ChatLineTests
{
    private static (RemapEngine engine, WarKeyProfile profile) Setup(bool focused = true)
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = new CommandCard
        {
            TopLeftX = 900, TopLeftY = 760, BottomRightX = 1050, BottomRightY = 820,
        };
        profile.Skills[0].FromVk = 'Q';
        profile.Skills[0].Enabled = true;
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
        Assert.Equal(RemapAction.SendKey, engine.Decide(VirtualKeys.Space, true, false, false).Action);
    }

    [Fact]
    public void Enter_opens_the_chat_line_and_every_key_then_passes_through_untouched()
    {
        var (engine, _) = Setup();

        Press(engine, VirtualKeys.Enter);

        Assert.True(engine.ChatOpen);
        // The space in "gg wp" must arrive as a space, not as NumPad7.
        Assert.Equal(RemapAction.PassThrough, engine.Decide(VirtualKeys.Space, true, false, false).Action);
        // ...and a position-bound letter must be typed, not cast.
        Assert.Equal(RemapAction.PassThrough, engine.Decide('Q', true, false, false).Action);
    }

    [Fact]
    public void A_second_Enter_sends_the_message_and_remapping_resumes()
    {
        var (engine, _) = Setup();

        Press(engine, VirtualKeys.Enter);
        Press(engine, VirtualKeys.Enter);

        Assert.False(engine.ChatOpen);
        Assert.Equal(RemapAction.SendKey, engine.Decide(VirtualKeys.Space, true, false, false).Action);
    }

    [Fact]
    public void Escape_abandons_the_message_and_remapping_resumes()
    {
        var (engine, _) = Setup();

        Press(engine, VirtualKeys.Enter);
        Press(engine, VirtualKeys.Escape);

        Assert.False(engine.ChatOpen);
        Assert.Equal(RemapAction.SendKey, engine.Decide(VirtualKeys.Space, true, false, false).Action);
    }

    [Fact]
    public void Chat_macros_do_not_fire_while_the_player_is_writing()
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
        engine.ObserveKey('A', isKeyDown: true); // any key pressed elsewhere resyncs us

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
    public void The_apps_own_macro_typing_never_moves_the_tracker()
    {
        var (engine, _) = Setup();

        // SendChatLines raises this flag around its injected keystrokes. Those are filtered out
        // by signature before reaching the hook, but a stray Enter must not toggle us either.
        engine.SuspendedForTyping = true;
        Press(engine, VirtualKeys.Enter);
        engine.SuspendedForTyping = false;

        Assert.False(engine.ChatOpen);
    }

    [Fact]
    public void Enter_and_Escape_are_never_remapped_because_they_drive_the_chat_line()
    {
        var (engine, profile) = Setup();
        profile.Inventory[0].FromVk = VirtualKeys.Enter;
        profile.Inventory[0].Enabled = true;
        profile.Inventory[1].FromVk = VirtualKeys.Escape;
        profile.Inventory[1].Enabled = true;

        Assert.Equal(RemapAction.PassThrough, engine.Decide(VirtualKeys.Enter, true, false, false).Action);
        Assert.Equal(RemapAction.PassThrough, engine.Decide(VirtualKeys.Escape, true, false, false).Action);
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
