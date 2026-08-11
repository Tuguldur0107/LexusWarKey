using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class RemapEngineTests
{
    private const int Q = 'Q';
    private const int T = 'T';

    private static (RemapEngine Engine, WarKeyProfile Profile) Create(bool gameFocused = true)
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[0].FromVk = Q;
        profile.Skills[0].ToVk = T;
        profile.Skills[0].Enabled = true;
        profile.ChatMacros[0].Message = "-clear";
        profile.ChatMacros[1].Message = "-ii";
        return (new RemapEngine(() => profile, () => gameFocused), profile);
    }

    [Fact]
    public void Skill_key_is_translated_to_the_current_warcraft_letter()
    {
        var (engine, _) = Create();
        var d = engine.Decide(Q, isKeyDown: true, ctrlHeld: false, altHeld: false);

        Assert.Equal(RemapAction.SendKey, d.Action);
        Assert.Equal(T, d.SendVk);
    }

    [Fact]
    public void Both_key_down_and_key_up_are_remapped_so_the_game_sees_a_full_keystroke()
    {
        var (engine, _) = Create();

        Assert.Equal(T, engine.Decide(Q, true, false, false).SendVk);
        Assert.Equal(T, engine.Decide(Q, false, false, false).SendVk);
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

        Assert.Equal(T, engine.Decide(Q, true, ctrlHeld: false, altHeld: true).SendVk);
        Assert.Equal(T, engine.Decide(Q, true, ctrlHeld: true, altHeld: false).SendVk);
    }

    [Fact]
    public void QuickChat_sends_one_line_from_each_fixed_slot()
    {
        var (engine, profile) = Create();

        var first = engine.Decide(profile.ChatMacros[0].HotkeyVk, true, false, false);
        var second = engine.Decide(profile.ChatMacros[1].HotkeyVk, true, false, false);

        Assert.Equal(new[] { "-clear" }, first.ChatLines);
        Assert.Equal(new[] { "-ii" }, second.ChatLines);
    }

    [Fact]
    public void QuickChat_does_not_fire_on_key_up_or_with_modifiers()
    {
        var (engine, profile) = Create();
        var hotkey = profile.ChatMacros[0].HotkeyVk;

        Assert.NotEqual(RemapAction.SendChat, engine.Decide(hotkey, isKeyDown: false, false, false).Action);
        Assert.Equal(RemapAction.PassThrough, engine.Decide(hotkey, true, ctrlHeld: true, altHeld: false).Action);
    }

    [Fact]
    public void Nothing_happens_outside_warcraft()
    {
        var (engine, _) = Create(gameFocused: false);

        Assert.Equal(RemapAction.PassThrough, engine.Decide(Q, true, false, false).Action);
        Assert.Equal(RemapAction.PassThrough, engine.Decide(VirtualKeys.F2, true, false, false).Action);
    }

    [Fact]
    public void Disabling_the_profile_stops_everything()
    {
        var (engine, profile) = Create();
        profile.Enabled = false;

        Assert.Equal(RemapAction.PassThrough, engine.Decide(Q, true, false, false).Action);
    }

    [Fact]
    public void Incomplete_or_self_maps_are_ignored()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[0] = new KeyMap { FromVk = Q, ToVk = Q, Enabled = true };
        profile.Skills[1] = new KeyMap { FromVk = 'W', ToVk = 0, Enabled = true };
        var engine = new RemapEngine(() => profile, () => true);

        Assert.Equal(RemapAction.PassThrough, engine.Decide(Q, true, false, false).Action);
        Assert.Equal(RemapAction.PassThrough, engine.Decide('W', true, false, false).Action);
    }

    [Fact]
    public void Duplicate_source_keys_are_reported_as_conflicts()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[0] = new KeyMap { FromVk = Q, ToVk = T, Enabled = true };
        profile.ChatMacros[0].HotkeyVk = Q;
        profile.ChatMacros[0].Message = "-clear";

        Assert.Equal(new[] { Q }, RemapEngine.FindConflicts(profile));
    }

    [Fact]
    public void Default_profile_is_empty_skills_plus_two_quickchat_slots()
    {
        var profile = WarKeyProfile.CreateDefault();

        Assert.Equal(WarKeyProfile.SkillSlots, profile.Skills.Count);
        Assert.All(profile.Skills, m => Assert.False(m.IsUsable));
        Assert.Equal(2, profile.ChatMacros.Count);
        Assert.Equal(new[] { VirtualKeys.F2, VirtualKeys.F3 }, profile.ChatMacros.Select(m => m.HotkeyVk));
        Assert.Empty(RemapEngine.FindConflicts(profile));
    }
}
