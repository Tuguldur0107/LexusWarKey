using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class RemapEngineTests
{
    private const int Space = VirtualKeys.Space;
    private const int NumPad7 = VirtualKeys.NumPad7;
    private const int Q = 'Q';
    private const int F2 = VirtualKeys.F2;

    private static (RemapEngine Engine, WarKeyProfile Profile) Create(bool gameFocused = true)
    {
        var profile = new WarKeyProfile
        {
            Inventory = { new KeyMap { FromVk = Space, ToVk = NumPad7 } },
            Skills = { new KeyMap { FromVk = Q, ToVk = 'T' } },
            ChatMacros = { new ChatMacro { HotkeyVk = F2, Messages = { "-clear", "-ii" } } },
        };
        return (new RemapEngine(() => profile, () => gameFocused), profile);
    }

    [Fact]
    public void Inventory_key_is_translated_to_the_game_key()
    {
        var (engine, _) = Create();
        var d = engine.Decide(Space, isKeyDown: true, ctrlHeld: false, altHeld: false);

        Assert.Equal(RemapAction.SendKey, d.Action);
        Assert.Equal(NumPad7, d.SendVk);
    }

    [Fact]
    public void Both_key_down_and_key_up_are_remapped_so_the_game_sees_a_full_keystroke()
    {
        var (engine, _) = Create();
        Assert.Equal(NumPad7, engine.Decide(Space, true, false, false).SendVk);
        Assert.Equal(NumPad7, engine.Decide(Space, false, false, false).SendVk);
    }

    [Fact]
    public void Unmapped_keys_pass_through_untouched()
    {
        var (engine, _) = Create();
        Assert.Equal(RemapAction.PassThrough, engine.Decide('Z', true, false, false).Action);
    }

    [Fact]
    public void Modifiers_are_preserved_autocast_and_learn_still_reach_the_remapped_key()
    {
        var (engine, _) = Create();

        // Alt+Q (auto-cast) and Ctrl+Q (learn skill) must still remap the base key,
        // and must NOT be turned into anything else.
        var alt = engine.Decide(Q, true, ctrlHeld: false, altHeld: true);
        var ctrl = engine.Decide(Q, true, ctrlHeld: true, altHeld: false);

        Assert.Equal(RemapAction.SendKey, alt.Action);
        Assert.Equal('T', alt.SendVk);
        Assert.Equal(RemapAction.SendKey, ctrl.Action);
        Assert.Equal('T', ctrl.SendVk);
    }

    [Fact]
    public void Chat_macro_sends_every_line_in_order_on_one_press()
    {
        var (engine, _) = Create();
        var d = engine.Decide(F2, true, false, false);

        Assert.Equal(RemapAction.SendChat, d.Action);
        Assert.Equal(new[] { "-clear", "-ii" }, d.ChatLines);
    }

    [Fact]
    public void Chat_macro_does_not_fire_on_key_up()
    {
        var (engine, _) = Create();
        Assert.NotEqual(RemapAction.SendChat, engine.Decide(F2, isKeyDown: false, false, false).Action);
    }

    [Fact]
    public void Chat_macro_ignores_modifier_combinations()
    {
        var (engine, _) = Create();
        Assert.Equal(RemapAction.PassThrough, engine.Decide(F2, true, ctrlHeld: true, altHeld: false).Action);
    }

    [Fact]
    public void Nothing_happens_outside_the_game_when_the_guard_is_on()
    {
        var (engine, _) = Create(gameFocused: false);
        Assert.Equal(RemapAction.PassThrough, engine.Decide(Space, true, false, false).Action);
        Assert.Equal(RemapAction.PassThrough, engine.Decide(F2, true, false, false).Action);
    }

    [Fact]
    public void Disabling_the_profile_stops_everything()
    {
        var (engine, profile) = Create();
        profile.Enabled = false;
        Assert.Equal(RemapAction.PassThrough, engine.Decide(Space, true, false, false).Action);
    }

    [Fact]
    public void While_a_macro_is_typing_nothing_else_is_remapped()
    {
        var (engine, _) = Create();
        engine.SuspendedForTyping = true;
        Assert.Equal(RemapAction.PassThrough, engine.Decide(Space, true, false, false).Action);
    }

    [Fact]
    public void A_map_onto_itself_is_ignored_so_it_cannot_loop()
    {
        var profile = new WarKeyProfile { Inventory = { new KeyMap { FromVk = Space, ToVk = Space } } };
        var engine = new RemapEngine(() => profile, () => true);

        Assert.Equal(RemapAction.PassThrough, engine.Decide(Space, true, false, false).Action);
    }

    [Fact]
    public void Disabled_or_incomplete_rows_are_ignored()
    {
        var profile = new WarKeyProfile
        {
            Inventory =
            {
                new KeyMap { FromVk = Space, ToVk = NumPad7, Enabled = false },
                new KeyMap { FromVk = 0, ToVk = NumPad7 },
            },
        };
        var engine = new RemapEngine(() => profile, () => true);

        Assert.Equal(RemapAction.PassThrough, engine.Decide(Space, true, false, false).Action);
    }

    [Fact]
    public void Duplicate_source_keys_are_reported_as_conflicts()
    {
        var profile = new WarKeyProfile
        {
            Inventory = { new KeyMap { FromVk = Space, ToVk = NumPad7 } },
            Skills = { new KeyMap { FromVk = Space, ToVk = 'T' } },
            ChatMacros = { new ChatMacro { HotkeyVk = F2, Messages = { "-clear" } } },
        };

        Assert.Equal(new[] { Space }, RemapEngine.FindConflicts(profile));
    }

    [Fact]
    public void Default_profile_matches_the_users_garena_layout_and_maps_nothing_else()
    {
        var profile = WarKeyProfile.CreateDefault();

        Assert.Equal(6, profile.Inventory.Count);
        Assert.Equal(Space, profile.Inventory[0].FromVk);
        Assert.Equal(VirtualKeys.Tab, profile.Inventory[1].FromVk);
        Assert.Equal(VirtualKeys.DefaultInventory, profile.Inventory.Select(i => i.ToVk));
        Assert.All(profile.Inventory.Skip(2), m => Assert.False(m.IsUsable));
        Assert.Empty(RemapEngine.FindConflicts(profile));
    }
}
