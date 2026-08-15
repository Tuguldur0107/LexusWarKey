using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class OverlayConfigSessionTests
{
    private static readonly IReadOnlyList<OverlaySkill> Detected = new[]
    {
        new OverlaySkill("A0R5", "Soul Rip", 'R'),
        new OverlaySkill("A0RG", "Smoke Screen", 'C'),
        new OverlaySkill("A0WP", "God's Strength", 'R'),
    };

    private static (OverlayConfigSession Session, WarKeyProfile Profile, Func<int> Saves) Create()
    {
        var profile = WarKeyProfile.CreateDefault();   // 6 inventory slots
        var saves = 0;
        var session = new OverlayConfigSession(profile, () => Detected, () => saves++);
        return (session, profile, () => saves);
    }

    [Fact]
    public void Number_picks_a_skill_then_a_letter_assigns_it_by_id()
    {
        var (session, profile, saves) = Create();

        Assert.True(session.HandleKey('2'));                       // pick Smoke Screen
        Assert.Equal(OverlayStep.WaitingForLetter, session.Step);
        Assert.Equal(0, saves());

        Assert.True(session.HandleKey('W'));                       // assign W

        Assert.Equal("W", profile.SkillLetters["A0RG"]);
        Assert.Equal(OverlayStep.ChoosingTarget, session.Step);
        Assert.Equal(1, saves());
    }

    [Fact]
    public void The_assignment_is_stored_by_ability_id_not_position()
    {
        var (session, profile, _) = Create();

        session.SelectTarget(0);
        session.HandleKey('Q');

        Assert.Equal("Q", profile.SkillLetters["A0R5"]);   // Soul Rip's id
    }

    [Fact]
    public void An_inventory_slot_follows_the_skills_and_captures_your_key()
    {
        var (session, profile, _) = Create();

        // 3 skills detected, so index 3 is the first inventory slot.
        session.SelectTarget(3);
        Assert.True(session.IsInventory(3));
        Assert.Equal(OverlayStep.WaitingForKey, session.Step);

        session.HandleKey(VirtualKeys.Space);

        Assert.Equal(VirtualKeys.Space, profile.Inventory[0].FromVk);
        Assert.True(profile.Inventory[0].Enabled);
        Assert.Equal(OverlayStep.ChoosingTarget, session.Step);
    }

    [Fact]
    public void Backspace_clears_a_skill_s_assignment()
    {
        var (session, profile, _) = Create();
        profile.SkillLetters["A0R5"] = "Q";

        session.SelectTarget(0);
        session.HandleKey(VirtualKeys.Back);

        Assert.False(profile.SkillLetters.ContainsKey("A0R5"));
        Assert.Equal(OverlayStep.ChoosingTarget, session.Step);
    }

    [Fact]
    public void Non_letters_at_the_letter_step_are_ignored()
    {
        var (session, profile, _) = Create();

        session.SelectTarget(0);
        Assert.True(session.HandleKey(VirtualKeys.Enter));   // not a letter
        Assert.Equal(OverlayStep.WaitingForLetter, session.Step);
        Assert.False(profile.SkillLetters.ContainsKey("A0R5"));

        session.HandleKey('E');
        Assert.Equal("E", profile.SkillLetters["A0R5"]);
    }

    [Fact]
    public void Escape_backs_out_of_a_target_before_it_closes_the_overlay()
    {
        var (session, _, _) = Create();

        session.SelectTarget(1);
        Assert.True(session.HandleKey(VirtualKeys.Escape));      // back to choosing
        Assert.Equal(OverlayStep.ChoosingTarget, session.Step);

        Assert.False(session.HandleKey(VirtualKeys.Escape));     // now closes
    }

    [Fact]
    public void Prompt_names_the_target_being_edited()
    {
        var (session, _, _) = Create();
        Assert.Contains("number", session.Prompt, System.StringComparison.OrdinalIgnoreCase);

        session.SelectTarget(2);
        Assert.Contains("God's Strength", session.Prompt);
    }

    [Theory]
    [InlineData('1', 0)]
    [InlineData('9', 8)]
    [InlineData(VirtualKeys.NumPad1, 0)]
    [InlineData('A', -1)]
    public void Number_mapping_covers_both_keyboards(int vk, int expected)
    {
        Assert.Equal(expected, OverlayConfigSession.SkillIndexFor(vk));
    }
}
