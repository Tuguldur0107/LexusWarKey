using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class LetterRemapTests
{
    private static (RemapEngine Engine, WarKeyProfile Profile) Create()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[0].FromVk = 'Q';
        profile.Skills[0].ToVk = 'T';
        profile.Skills[0].Enabled = true;
        return (new RemapEngine(() => profile, () => true), profile);
    }

    [Fact]
    public void A_skill_cell_sends_its_game_letter_on_both_down_and_up()
    {
        var (engine, _) = Create();

        Assert.Equal('T', engine.Decide('Q', true, false, false).SendVk);
        Assert.Equal('T', engine.Decide('Q', false, false, false).SendVk);
    }

    [Fact]
    public void Nothing_the_engine_returns_moves_the_cursor()
    {
        var (engine, profile) = Create();
        profile.Skills[1].FromVk = 'W';
        profile.Skills[1].Enabled = true;

        foreach (var vk in new[] { 'Q', 'W', 'Z' })
        {
            var action = engine.Decide(vk, true, false, false).Action;
            Assert.True(action is RemapAction.SendKey or RemapAction.PassThrough or RemapAction.SendChat);
        }
    }

    [Fact]
    public void A_cell_with_no_game_letter_does_not_swallow_the_key()
    {
        var (engine, profile) = Create();
        profile.Skills[1].FromVk = 'W';
        profile.Skills[1].Enabled = true;

        Assert.Equal(RemapAction.PassThrough, engine.Decide('W', true, false, false).Action);
    }

    [Fact]
    public void The_overlay_asks_for_the_game_letter_after_the_user_key()
    {
        var profile = WarKeyProfile.CreateDefault();
        var session = new OverlayConfigSession(profile, () => { });

        session.SelectSlot(7);
        session.HandleKey('Z');
        Assert.Equal(OverlayStep.WaitingForLetter, session.Step);

        session.HandleKey('R');
        Assert.Equal('Z', profile.Skills[7].FromVk);
        Assert.Equal('R', profile.Skills[7].ToVk);
        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);
    }

    [Fact]
    public void Backspace_at_the_letter_step_clears_the_whole_binding()
    {
        var profile = WarKeyProfile.CreateDefault();
        var session = new OverlayConfigSession(profile, () => { });

        session.SelectSlot(3);
        session.HandleKey('Z');
        session.HandleKey(VirtualKeys.Back);

        Assert.Equal(0, profile.Skills[3].FromVk);
        Assert.Equal(0, profile.Skills[3].ToVk);
        Assert.False(profile.Skills[3].Enabled);
    }

    [Fact]
    public void Enter_cannot_be_bound_as_a_trigger_key()
    {
        // Warcraft's chat line owns Enter and RemapEngine passes it through before the skill
        // lookup, so a cell bound to Enter would read "Enter->T", look configured, never be
        // reported as dead, and never cast. Refuse it at the source instead.
        var profile = WarKeyProfile.CreateDefault();
        var session = new OverlayConfigSession(profile, () => { });

        session.SelectSlot(4);
        session.HandleKey(VirtualKeys.Enter);

        Assert.Equal(OverlayStep.WaitingForKey, session.Step);   // still waiting for a real key
        Assert.Equal(0, profile.Skills[4].FromVk);

        session.HandleKey('C');
        Assert.Equal('C', profile.Skills[4].FromVk);
    }

    [Fact]
    public void Enter_at_the_letter_step_keeps_the_existing_game_letter()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[2].ToVk = 'D';
        var session = new OverlayConfigSession(profile, () => { });

        session.SelectSlot(2);
        session.HandleKey('E');
        session.HandleKey(VirtualKeys.Enter);

        Assert.Equal('E', profile.Skills[2].FromVk);
        Assert.Equal('D', profile.Skills[2].ToVk);
        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);
    }
}
