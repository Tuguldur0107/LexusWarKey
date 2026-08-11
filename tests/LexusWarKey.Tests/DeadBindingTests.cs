using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class DeadBindingTests
{
    private static WarKeyProfile Working()
    {
        var p = WarKeyProfile.CreateDefault();
        p.Enabled = true;
        return p;
    }

    [Fact]
    public void A_fully_working_setup_reports_no_problems()
    {
        var p = Working();
        p.Skills[0].FromVk = 'C';
        p.Skills[0].ToVk = 'T';
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
        Assert.Contains("disabled", problems[0]);
    }

    [Fact]
    public void Skill_cells_with_no_game_letter_are_reported()
    {
        var p = Working();
        p.Skills[0].FromVk = 'C';
        p.Skills[0].Enabled = true;
        p.Skills[3].FromVk = 'R';
        p.Skills[3].Enabled = true;

        var problems = RemapEngine.FindDeadBindings(p);

        Assert.Contains(problems, x => x.Contains("2 skill slot"));
    }

    [Fact]
    public void Loading_a_profile_clears_slots_that_are_enabled_but_have_no_key()
    {
        var p = WarKeyProfile.CreateDefault();
        p.Skills[3].Enabled = true;

        p.NormaliseSlots();

        Assert.False(p.Skills[3].Enabled);
        Assert.Empty(RemapEngine.FindConflicts(p));
    }
}
