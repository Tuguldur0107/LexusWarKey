using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class OverlayConfigSessionTests
{
    private static (OverlayConfigSession Session, WarKeyProfile Profile, Func<int> Saves) Create()
    {
        var profile = WarKeyProfile.CreateDefault();
        var saves = 0;
        var session = new OverlayConfigSession(profile, () => saves++);
        return (session, profile, () => saves);
    }

    [Fact]
    public void Empty_skill_slot_is_bound_by_user_key_then_game_letter()
    {
        var (session, profile, saves) = Create();

        session.SelectSlot(2);
        Assert.True(session.HandleKey('E'));
        Assert.Equal(OverlayStep.WaitingForLetter, session.Step);
        Assert.Equal(0, saves());

        Assert.True(session.HandleKey('R'));

        Assert.Equal('E', profile.Skills[2].FromVk);
        Assert.Equal('R', profile.Skills[2].ToVk);
        Assert.True(profile.Skills[2].Enabled);
        Assert.Equal(1, saves());
    }

    [Fact]
    public void Every_skill_slot_is_selectable()
    {
        var (session, profile, _) = Create();

        for (var i = 0; i < profile.Skills.Count; i++)
        {
            session.SelectSlot(i);
            Assert.Equal(OverlayStep.WaitingForKey, session.Step);
            Assert.Equal(i, session.SelectedIndex);
            session.Reset();
        }
    }

    [Fact]
    public void Digit_shortcut_picks_skill_slots()
    {
        var (session, profile, _) = Create();

        Assert.True(session.HandleKey('3'));
        Assert.Equal(OverlayStep.WaitingForKey, session.Step);
        session.HandleKey('G');
        session.HandleKey('T');

        Assert.Equal('G', profile.Skills[2].FromVk);
        Assert.Equal('T', profile.Skills[2].ToVk);
    }

    [Fact]
    public void Digits_can_themselves_be_bound_once_a_slot_is_chosen()
    {
        var (session, profile, _) = Create();

        session.SelectSlot(0);
        session.HandleKey('4');

        Assert.Equal('4', profile.Skills[0].FromVk);
        Assert.Equal(OverlayStep.WaitingForLetter, session.Step);
    }

    [Fact]
    public void Backspace_clears_the_selected_slot()
    {
        var (session, profile, _) = Create();
        profile.Skills[0] = new KeyMap { FromVk = 'Q', ToVk = 'T', Enabled = true };

        session.SelectSlot(0);
        session.HandleKey(VirtualKeys.Back);

        Assert.Equal(0, profile.Skills[0].FromVk);
        Assert.Equal(0, profile.Skills[0].ToVk);
        Assert.False(profile.Skills[0].Enabled);
    }

    [Fact]
    public void Escape_backs_out_of_a_slot_before_it_closes_the_overlay()
    {
        var (session, _, _) = Create();

        session.SelectSlot(1);
        Assert.True(session.HandleKey(VirtualKeys.Escape));
        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);

        Assert.False(session.HandleKey(VirtualKeys.Escape));
    }

    [Fact]
    public void Out_of_range_selections_are_ignored()
    {
        var (session, _, _) = Create();

        session.SelectSlot(99);

        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);
    }

    [Fact]
    public void Binding_the_same_key_twice_is_reported_as_a_conflict()
    {
        var (session, profile, _) = Create();

        session.SelectSlot(3);
        session.HandleKey('R');
        session.HandleKey('T');
        session.SelectSlot(4);
        session.HandleKey('R');
        session.HandleKey('E');

        Assert.Contains('R', RemapEngine.FindConflicts(profile));
    }

    [Fact]
    public void Prompt_names_the_skill_being_edited()
    {
        var (session, _, _) = Create();
        Assert.Contains("Select skill", session.Prompt);

        session.SelectSlot(5);
        Assert.Contains("Skill 6", session.Prompt);
    }

    [Theory]
    [InlineData('1', 0)]
    [InlineData('8', 7)]
    [InlineData(VirtualKeys.NumPad1, 0)]
    [InlineData(VirtualKeys.NumPad8, 7)]
    [InlineData('9', -1)]
    [InlineData('A', -1)]
    public void Skill_digit_mapping_covers_both_keyboards(int vk, int expected)
    {
        Assert.Equal(expected, OverlayConfigSession.SkillIndexFor(vk));
    }
}
