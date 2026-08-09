using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>CustomKeys mode: a skill cell that carries a game letter SENDS it instead of
/// clicking the cell. The letter is what the generated CustomKeys.txt taught the game, so a
/// keystroke is all it takes — no cursor, no click queue, and it works with the card unlinked.</summary>
public class GameLetterModeTests
{
    private const int Q = 'Q';
    private const int E = 'E';

    private static WarKeyProfile Calibrated(WarKeyProfile profile)
    {
        profile.CommandCard.TopLeftX = 100;
        profile.CommandCard.TopLeftY = 100;
        profile.CommandCard.BottomRightX = 400;
        profile.CommandCard.BottomRightY = 250;
        return profile;
    }

    private static (RemapEngine Engine, WarKeyProfile Profile) Create(bool letters, bool calibrated = true)
    {
        var profile = new WarKeyProfile { UseGameLetters = letters };
        profile.Skills.Add(new KeyMap { FromVk = Q, ToVk = E, Enabled = true });         // lettered cell
        profile.Skills.Add(new KeyMap { FromVk = 'X', ToVk = 0, Enabled = true });       // click-fallback cell
        if (calibrated)
            Calibrated(profile);
        return (new RemapEngine(() => profile, () => true), profile);
    }

    [Fact]
    public void Lettered_cell_sends_its_game_letter_on_both_down_and_up()
    {
        var (engine, _) = Create(letters: true);

        var down = engine.Decide(Q, true, false, false);
        var up = engine.Decide(Q, false, false, false);

        Assert.Equal(RemapAction.SendKey, down.Action);
        Assert.Equal(E, down.SendVk);
        Assert.Equal(RemapAction.SendKey, up.Action);
        Assert.Equal(E, up.SendVk);
    }

    [Fact]
    public void Lettered_cell_works_without_any_calibration()
    {
        var (engine, _) = Create(letters: true, calibrated: false);
        Assert.Equal(E, engine.Decide(Q, true, false, false).SendVk);
    }

    [Fact]
    public void Key_that_already_is_the_letter_passes_through_untouched()
    {
        var (engine, profile) = Create(letters: true);
        profile.Skills[0].FromVk = E; // bound key == game letter

        Assert.Equal(RemapAction.PassThrough, engine.Decide(E, true, false, false).Action);
    }

    [Fact]
    public void Cell_without_a_letter_still_clicks_its_position()
    {
        var (engine, _) = Create(letters: true);
        Assert.Equal(RemapAction.ClickSlot, engine.Decide('X', true, false, false).Action);
    }

    [Fact]
    public void With_the_mode_off_a_lettered_cell_still_clicks()
    {
        var (engine, _) = Create(letters: false);
        Assert.Equal(RemapAction.ClickSlot, engine.Decide(Q, true, false, false).Action);
    }

    [Fact]
    public void Modifiers_ride_along_so_autocast_and_learn_reach_the_letter()
    {
        var (engine, _) = Create(letters: true);

        // Alt+letter toggles auto-cast and Ctrl+letter learns the skill in the game itself,
        // so the decision must stay a plain SendKey with the modifier left un-consumed.
        Assert.Equal(E, engine.Decide(Q, true, ctrlHeld: false, altHeld: true).SendVk);
        Assert.Equal(E, engine.Decide(Q, true, ctrlHeld: true, altHeld: false).SendVk);
    }

    [Fact]
    public void Seeding_fills_only_empty_cells_with_the_scheme_letters()
    {
        var profile = new WarKeyProfile();
        for (var i = 0; i < WarKeyProfile.SkillSlots; i++)
            profile.Skills.Add(new KeyMap());
        profile.Skills[8].ToVk = 'T'; // hand-set letter must survive re-seeding

        profile.SeedSkillLetters();

        Assert.Equal('Q', profile.Skills[5].ToVk);
        Assert.Equal('W', profile.Skills[6].ToVk);
        Assert.Equal('G', profile.Skills[7].ToVk);
        Assert.Equal('T', profile.Skills[8].ToVk);   // untouched
        Assert.Equal('D', profile.Skills[9].ToVk);
        Assert.Equal('F', profile.Skills[10].ToVk);
        Assert.Equal('E', profile.Skills[11].ToVk);
        Assert.Equal(0, profile.Skills[4].ToVk);     // the scheme's unbound cell stays empty
    }

    [Fact]
    public void Dead_binding_warning_counts_only_cells_that_would_click()
    {
        var profile = new WarKeyProfile { UseGameLetters = true }; // card NOT calibrated
        profile.Skills.Add(new KeyMap { FromVk = Q, ToVk = E, Enabled = true });
        profile.Skills.Add(new KeyMap { FromVk = 'X', ToVk = 0, Enabled = true });

        var problems = RemapEngine.FindDeadBindings(profile);

        // Only the click-fallback cell is dead without calibration; the lettered one works.
        Assert.Contains(problems, p => p.StartsWith("1 чадварын товч"));
    }

    [Fact]
    public void Overlay_offers_the_letter_step_and_seeds_the_cell_default()
    {
        var profile = new WarKeyProfile { UseGameLetters = true };
        for (var i = 0; i < WarKeyProfile.SkillSlots; i++)
            profile.Skills.Add(new KeyMap());
        var session = new OverlayConfigSession(profile, () => { });

        session.SelectSlot(SlotGroup.Skill, 11);      // bottom-right cell, scheme letter E
        session.HandleKey('Z');                       // the player's own key

        Assert.Equal(OverlayStep.WaitingForLetter, session.Step);
        Assert.Equal('Z', profile.Skills[11].FromVk);
        Assert.Equal(E, profile.Skills[11].ToVk);     // default arrived without extra input

        session.HandleKey('T');                       // override: this cell answers T today
        Assert.Equal('T', profile.Skills[11].ToVk);
        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);
    }

    [Fact]
    public void Overlay_letter_step_backspace_returns_the_cell_to_click_mode()
    {
        var profile = new WarKeyProfile { UseGameLetters = true };
        for (var i = 0; i < WarKeyProfile.SkillSlots; i++)
            profile.Skills.Add(new KeyMap());
        var session = new OverlayConfigSession(profile, () => { });

        session.SelectSlot(SlotGroup.Skill, 11);
        session.HandleKey('Z');
        session.HandleKey(VirtualKeys.Back);

        Assert.Equal(0, profile.Skills[11].ToVk);
    }

    [Fact]
    public void Switching_the_mode_off_clears_seeded_defaults_but_keeps_hand_set_letters()
    {
        var profile = new WarKeyProfile();
        for (var i = 0; i < WarKeyProfile.SkillSlots; i++)
            profile.Skills.Add(new KeyMap());
        profile.SeedSkillLetters();
        profile.Skills[9].ToVk = 'T'; // the user corrected a displaced skill by hand

        profile.ClearSeededSkillLetters();

        Assert.Equal(0, profile.Skills[5].ToVk);   // seeded Q gone
        Assert.Equal(0, profile.Skills[11].ToVk);  // seeded E gone
        Assert.Equal('T', profile.Skills[9].ToVk); // the user's own letter survives
    }

    [Fact]
    public void Overlay_letter_step_ignores_mouse_controls_and_keeps_waiting()
    {
        var profile = new WarKeyProfile { UseGameLetters = true };
        for (var i = 0; i < WarKeyProfile.SkillSlots; i++)
            profile.Skills.Add(new KeyMap());
        var session = new OverlayConfigSession(profile, () => { });

        session.SelectSlot(SlotGroup.Skill, 11);
        session.HandleKey('Z');
        session.HandleKey(VirtualKeys.WheelUp);    // a stray wheel notch mid-adjustment

        Assert.Equal(OverlayStep.WaitingForLetter, session.Step); // still waiting for a real key
        Assert.Equal(E, profile.Skills[11].ToVk);  // the seeded default is untouched

        session.HandleKey('T');
        Assert.Equal('T', profile.Skills[11].ToVk);
    }

    [Fact]
    public void Legacy_remap_cells_are_not_reported_dead_on_an_unlinked_card()
    {
        var profile = new WarKeyProfile(); // mode off, card NOT calibrated
        profile.Skills.Add(new KeyMap { FromVk = Q, ToVk = 'T', Enabled = true }); // works as a plain remap
        profile.Skills.Add(new KeyMap { FromVk = 'X', ToVk = 0, Enabled = true }); // genuinely dead

        var problems = RemapEngine.FindDeadBindings(profile);

        Assert.Contains(problems, p => p.StartsWith("1 чадварын товч"));
    }

    [Fact]
    public void Overlay_skips_the_letter_step_when_the_mode_is_off()
    {
        var profile = new WarKeyProfile();            // UseGameLetters = false
        for (var i = 0; i < WarKeyProfile.SkillSlots; i++)
            profile.Skills.Add(new KeyMap());
        var session = new OverlayConfigSession(profile, () => { });

        session.SelectSlot(SlotGroup.Skill, 11);
        session.HandleKey('Z');

        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);
        Assert.Equal(0, profile.Skills[11].ToVk);     // nothing seeded behind the user's back
    }
}
