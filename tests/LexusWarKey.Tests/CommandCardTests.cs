using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Position-based skills exist for modes like LoD where abilities are random and their
/// letters unknown, so the grid maths has to be exact — a wrong cell clicks the wrong spell.</summary>
public class CommandCardTests
{
    // The ability area is 4 wide and 2 tall, so calibration marks slot 1 and slot 8.
    private static CommandCard Calibrated() => new()
    {
        TopLeftX = 1000, TopLeftY = 800,         // slot 1 centre
        BottomRightX = 1150, BottomRightY = 900, // slot 8 centre
    };

    [Fact]
    public void Uncalibrated_card_yields_no_points()
    {
        var card = new CommandCard();
        Assert.False(card.IsCalibrated);
        Assert.Null(card.PointFor(0));
    }

    [Theory]
    [InlineData(0, 1000, 800)]   // row 0, col 0
    [InlineData(3, 1150, 800)]   // row 0, col 3
    [InlineData(4, 1000, 900)]   // row 1, col 0
    [InlineData(7, 1150, 900)]   // row 1, col 3
    public void Slots_interpolate_across_the_two_calibrated_corners(int slot, int x, int y)
    {
        var p = Calibrated().PointFor(slot);
        Assert.NotNull(p);
        Assert.Equal(x, p!.X);
        Assert.Equal(y, p.Y);
    }

    [Fact]
    public void Middle_slots_land_on_even_spacing()
    {
        var card = Calibrated();
        Assert.Equal(1050, card.PointFor(1)!.X);
        Assert.Equal(1100, card.PointFor(2)!.X);
        Assert.Equal(1050, card.PointFor(5)!.X);
        Assert.Equal(900, card.PointFor(5)!.Y);
    }

    [Fact]
    public void The_grid_matches_the_games_ability_area()
    {
        Assert.Equal(4, CommandCard.Columns);
        Assert.Equal(2, CommandCard.Rows);
        Assert.Equal(8, CommandCard.Slots);
        Assert.Equal(CommandCard.Slots, WarKeyProfile.CreateDefault().Skills.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(99)]
    public void Out_of_range_slots_return_null(int slot)
    {
        Assert.Null(Calibrated().PointFor(slot));
    }

    [Fact]
    public void Clearing_makes_it_uncalibrated_again()
    {
        var card = Calibrated();
        card.Clear();
        Assert.False(card.IsCalibrated);
    }

    [Fact]
    public void Position_mode_clicks_the_slot_for_the_bound_key()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = Calibrated();
        profile.SkillsUsePosition = true;
        profile.Skills[0].FromVk = 'Q';
        profile.Skills[0].Enabled = true;

        var engine = new RemapEngine(() => profile, () => true);
        var d = engine.Decide('Q', isKeyDown: true, ctrlHeld: false, altHeld: false);

        Assert.Equal(RemapAction.ClickSlot, d.Action);
        Assert.Equal(1000, d.ClickAt!.X);
        Assert.Equal(800, d.ClickAt.Y);
        Assert.False(d.RightClick);
    }

    [Fact]
    public void Alt_becomes_a_right_click_which_is_how_autocast_is_toggled()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = Calibrated();
        profile.Skills[2].FromVk = 'E';
        profile.Skills[2].Enabled = true;

        var engine = new RemapEngine(() => profile, () => true);
        var d = engine.Decide('E', true, ctrlHeld: false, altHeld: true);

        Assert.Equal(RemapAction.ClickSlot, d.Action);
        Assert.True(d.RightClick);
    }

    [Fact]
    public void Key_up_is_swallowed_so_the_click_never_fires_twice()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = Calibrated();
        profile.Skills[0].FromVk = 'Q';
        profile.Skills[0].Enabled = true;

        var engine = new RemapEngine(() => profile, () => true);
        var up = engine.Decide('Q', isKeyDown: false, false, false);

        Assert.Equal(RemapAction.ClickSlot, up.Action);
        Assert.Null(up.ClickAt);
    }

    [Fact]
    public void Without_calibration_position_mode_falls_back_to_plain_remapping()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.SkillsUsePosition = true;           // on, but never calibrated
        profile.Skills[0].FromVk = 'Q';
        profile.Skills[0].ToVk = 'T';
        profile.Skills[0].Enabled = true;

        var engine = new RemapEngine(() => profile, () => true);
        var d = engine.Decide('Q', true, false, false);

        Assert.Equal(RemapAction.SendKey, d.Action);
        Assert.Equal('T', d.SendVk);
    }

    [Fact]
    public void Inventory_still_remaps_normally_while_skills_use_positions()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = Calibrated();
        profile.SkillsUsePosition = true;

        var engine = new RemapEngine(() => profile, () => true);
        var d = engine.Decide(VirtualKeys.Space, true, false, false);

        Assert.Equal(RemapAction.SendKey, d.Action);
        Assert.Equal(VirtualKeys.NumPad7, d.SendVk);
    }

    [Fact]
    public void Cursor_movement_is_off_by_default_so_the_players_aim_is_never_stolen()
    {
        Assert.False(new WarKeyProfile().MoveCursorForClicks);
    }
}
