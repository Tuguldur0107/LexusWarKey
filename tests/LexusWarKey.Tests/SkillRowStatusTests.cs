using LexusWarKey.ViewModels;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The per-row status: a freshly assigned letter is "applying" until the background writer
/// puts it on the game's card, then "applied".</summary>
public class SkillRowStatusTests
{
    private static DetectedSkillRow Row(char def) =>
        new("A000", "Test", def, assigned: "", _ => { });

    [Fact]
    public void No_assignment_is_neither_applying_nor_applied()
    {
        var row = Row('R');
        row.CurrentLetter = "R";

        Assert.False(row.IsApplying);
        Assert.False(row.IsApplied);
    }

    [Fact]
    public void A_just_assigned_letter_is_applying_until_the_card_catches_up()
    {
        var row = Row('R');
        row.CurrentLetter = "R";   // game still on default
        row.Assigned = "Q";        // player wants Q

        Assert.True(row.IsApplying);
        Assert.False(row.IsApplied);
    }

    [Fact]
    public void Once_the_card_reads_back_the_letter_it_is_applied()
    {
        var row = Row('R');
        row.Assigned = "Q";
        row.CurrentLetter = "Q";   // writer applied it; next read matches

        Assert.False(row.IsApplying);
        Assert.True(row.IsApplied);
    }

    [Fact]
    public void While_capturing_it_does_not_show_applying()
    {
        var row = Row('R');
        row.CurrentLetter = "R";
        row.Assigned = "Q";
        row.IsCapturing = true;

        Assert.False(row.IsApplying);
    }
}
