using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class AbilityDataTests
{
    private const string Csv =
        "id,in_w3a,hotkey,researchhotkey,unhotkey,buttonpos,name,tip\n" +
        "A0AA,False,R,\"\"\"R\"\"\",,\"0,1\",Elune's Arrow,\"Fires an arrow, stunning\"\n" +
        "A0BB,False,C,,,\"0,1\",Arc Lightning,tip\n" +
        "A0CC,False,r,,,\"0,1\",God's Strength,tip\n" +          // lowercase hotkey -> upper-cased
        "Z999,False,,,,\"0,1\",No Hotkey Skill,tip\n" +          // no hotkey -> skipped
        "BADD,False,R,,,\"0,1\",,tip\n";                          // no name -> skipped

    [Fact]
    public void Parses_id_letter_name_and_skips_unusable_rows()
    {
        var data = AbilityData.Parse(Csv);

        Assert.Equal('R', data.ById["A0AA"].Letter);
        Assert.Equal("Elune's Arrow", data.ById["A0AA"].Name);
        Assert.Equal('R', data.ById["A0CC"].Letter);            // 'r' upper-cased
        Assert.False(data.ById.ContainsKey("Z999"));            // letterless skipped
        Assert.False(data.ById.ContainsKey("BADD"));            // nameless skipped
    }

    [Fact]
    public void Comma_inside_quoted_tip_does_not_break_columns()
    {
        var data = AbilityData.Parse(Csv);
        // The tip "Fires an arrow, stunning" contains a comma; name must still be correct.
        Assert.Equal("Elune's Arrow", data.ById["A0AA"].Name);
    }

    [Fact]
    public void Resolve_matches_by_partial_name_case_insensitive()
    {
        var data = AbilityData.Parse(Csv);
        Assert.Equal("A0AA", Assert.Single(data.Resolve("elune")).Id);
        Assert.Equal("A0BB", Assert.Single(data.Resolve("ARC lightning")).Id);
    }

    [Fact]
    public void Resolve_matches_by_exact_id()
    {
        var data = AbilityData.Parse(Csv);
        Assert.Equal("God's Strength", Assert.Single(data.Resolve("A0CC")).Name);
    }

    [Fact]
    public void Embedded_table_loads_and_contains_known_lod_abilities()
    {
        var data = AbilityData.LoadEmbedded();
        Assert.True(data.ById.Count > 1000, $"expected the full table, got {data.ById.Count}");
        // A staple LoD ability that must be present with a real letter.
        var meteor = data.Resolve("Chaos Meteor");
        Assert.NotEmpty(meteor);
        Assert.All(meteor, a => Assert.True(char.IsLetter(a.Letter)));

        // The map wraps names in quotes; they must be stripped so the resolver matches the clean
        // string the game holds in memory (otherwise no skill's cell is ever found and no write lands).
        Assert.NotEmpty(data.Resolve("Living Armor"));
        Assert.All(data.ById.Values, a => Assert.DoesNotContain('"', a.Name));
    }
}
