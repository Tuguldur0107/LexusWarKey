using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Regression tests for the day the app looked configured but did nothing: the master
/// switch was off, calibration had silently failed, and skill keys had no target. Every one of
/// those states must now explain itself instead of failing quietly.</summary>
public class DeadBindingTests
{
    private static WarKeyProfile Working()
    {
        var p = WarKeyProfile.CreateDefault();
        p.Enabled = true;
        p.CommandCard = new CommandCard { TopLeftX = 900, TopLeftY = 760, BottomRightX = 1050, BottomRightY = 820 };
        return p;
    }

    [Fact]
    public void A_fully_working_setup_reports_no_problems()
    {
        var p = Working();
        p.Skills[0].FromVk = 'C';
        p.Skills[0].Enabled = true;

        Assert.Empty(RemapEngine.FindDeadBindings(p));
    }

    [Fact]
    public void The_master_switch_being_off_is_reported_first()
    {
        var p = Working();
        p.Enabled = false;

        var problems = RemapEngine.FindDeadBindings(p);

        Assert.NotEmpty(problems);
        Assert.Contains("унтраалттай", problems[0]);
    }

    [Fact]
    public void Skill_keys_bound_before_calibrating_are_reported()
    {
        var p = Working();
        p.CommandCard = new CommandCard(); // never calibrated
        p.Skills[4].FromVk = 'C';
        p.Skills[4].Enabled = true;
        p.Skills[7].FromVk = 'R';
        p.Skills[7].Enabled = true;

        var problems = RemapEngine.FindDeadBindings(p);

        Assert.Contains(problems, x => x.Contains("2 чадварын товч") && x.Contains("тохируулаагүй"));
    }

    [Fact]
    public void Skill_keys_without_a_target_are_reported_when_position_mode_is_off()
    {
        var p = Working();
        p.SkillsUsePosition = false;
        p.Skills[0].FromVk = 'C';
        p.Skills[0].Enabled = true; // ToVk stays 0

        Assert.Contains(RemapEngine.FindDeadBindings(p), x => x.Contains("зорилтот товч алга"));
    }

    // ---- calibration validation: clicking the same button twice used to "succeed" ----

    [Fact]
    public void Clicking_the_same_point_twice_is_rejected()
    {
        Assert.NotNull(CommandCard.Validate(904, 763, 904, 763));

        var card = new CommandCard { TopLeftX = 904, TopLeftY = 763, BottomRightX = 904, BottomRightY = 763 };
        Assert.False(card.IsCalibrated);
    }

    [Theory]
    [InlineData(900, 760, 905, 820)]   // same column
    [InlineData(900, 760, 1050, 765)]  // same row
    public void Two_points_that_do_not_span_a_grid_are_rejected(int x1, int y1, int x2, int y2)
    {
        Assert.NotNull(CommandCard.Validate(x1, y1, x2, y2));
    }

    [Fact]
    public void A_proper_span_is_accepted()
    {
        Assert.Null(CommandCard.Validate(900, 760, 1050, 820));
        Assert.True(new CommandCard { TopLeftX = 900, TopLeftY = 760, BottomRightX = 1050, BottomRightY = 820 }.IsCalibrated);
    }

    [Fact]
    public void Loading_a_profile_clears_slots_that_are_enabled_but_have_no_key()
    {
        var p = WarKeyProfile.CreateDefault();
        p.Skills[3].Enabled = true;   // enabled with FromVk == 0, as seen in a real profile
        p.Inventory[4].Enabled = true;

        p.NormaliseSlots();

        Assert.False(p.Skills[3].Enabled);
        Assert.False(p.Inventory[4].Enabled);
        Assert.Empty(RemapEngine.FindConflicts(p));
    }
}
