using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class SkillWritePlannerTests
{
    private static AbilityInfo Ab(string id, char letter, string name) => new(id, letter, name);

    [Fact]
    public void Plans_a_write_only_where_the_assigned_letter_differs_from_the_current_one()
    {
        var detected = new (AbilityInfo, ulong, char)[]
        {
            (Ab("A", 'R', "Soul Rip"), 0x100, 'R'),        // currently R
            (Ab("B", 'C', "Smoke Screen"), 0x200, 'C'),    // currently C
        };
        var desired = new Dictionary<string, char> { ["A"] = 'Q', ["B"] = 'C' };  // A wants Q, B already C

        var (rows, writes) = SkillWritePlanner.Plan(detected, desired);

        var write = Assert.Single(writes);
        Assert.Equal("A", write.AbilityId);
        Assert.Equal(0x100ul, write.CellAddr);
        Assert.Equal('R', write.CurrentLetter);
        Assert.Equal('Q', write.DesiredLetter);
        Assert.Equal(2, rows.Count);   // both skills reported
    }

    [Fact]
    public void Rows_are_sorted_by_name_and_carry_default_and_assigned()
    {
        var detected = new (AbilityInfo, ulong, char)[]
        {
            (Ab("Z", 'D', "Zebra Skill"), 0x100, 'D'),
            (Ab("A", 'R', "Alpha Skill"), 0x200, 'R'),
        };
        var desired = new Dictionary<string, char> { ["A"] = 'Q' };

        var (rows, _) = SkillWritePlanner.Plan(detected, desired);

        Assert.Equal(new[] { "Alpha Skill", "Zebra Skill" }, rows.Select(r => r.Ability.Name));
        Assert.Equal('R', rows[0].Ability.Letter);   // default
        Assert.Equal('Q', rows[0].DesiredLetter);     // assigned
        Assert.Equal('\0', rows[1].DesiredLetter);    // unassigned
    }

    [Fact]
    public void Unassigned_skills_and_unknown_cells_produce_no_writes()
    {
        var detected = new (AbilityInfo, ulong, char)[]
        {
            (Ab("A", 'R', "A"), 0x100, 'R'),   // unassigned
            (Ab("B", 'C', "B"), 0, 'C'),       // assigned but cell unknown (0)
        };
        var desired = new Dictionary<string, char> { ["B"] = 'Y' };

        var (_, writes) = SkillWritePlanner.Plan(detected, desired);

        Assert.Empty(writes);
    }
}
