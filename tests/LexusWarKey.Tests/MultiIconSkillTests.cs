using System.Linq;
using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>A multi-icon skill is several ability ids that share one name (a toggle's activate/
/// deactivate, a morph's forms). A hotkey letter assigned on one state must cover them all.</summary>
public class MultiIconSkillTests
{
    private const string Csv =
        "id,in_w3a,hotkey,researchhotkey,unhotkey,buttonpos,name,tip\n" +
        "A21F,True,V,,,\"3,2\",\"Pulse Nova\",x\n" +   // activate
        "A21H,True,V,,,\"3,2\",\"Pulse Nova\",x\n" +   // deactivate
        "A0R5,True,R,,,\"0,0\",\"Soul Rip\",x\n";

    [Fact]
    public void Same_name_abilities_form_one_family()
    {
        var data = AbilityData.Parse(Csv);

        Assert.Equal(new[] { "A21F", "A21H" }, data.IdsWithSameName("A21F").OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "A21F", "A21H" }, data.IdsWithSameName("A21H").OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "A0R5" }, data.IdsWithSameName("A0R5").ToArray());
    }

    [Fact]
    public void Assigning_a_letter_to_a_family_covers_every_state()
    {
        var profile = WarKeyProfile.CreateDefault();

        profile.SetSkillLetterFamily(new[] { "A21F", "A21H" }, "B");

        Assert.Equal("B", profile.SkillLetters["A21F"]);
        Assert.Equal("B", profile.SkillLetters["A21H"]);
    }

    [Fact]
    public void A_family_letter_stays_unique_across_different_skills()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.SetSkillLetterFamily(new[] { "A21F", "A21H" }, "B");

        profile.SetSkillLetterFamily(new[] { "A0R5" }, "B");   // another skill takes B

        Assert.False(profile.SkillLetters.ContainsKey("A21F"));
        Assert.False(profile.SkillLetters.ContainsKey("A21H"));
        Assert.Equal("B", profile.SkillLetters["A0R5"]);
    }

    [Fact]
    public void Clearing_a_family_removes_every_state()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.SetSkillLetterFamily(new[] { "A21F", "A21H" }, "B");

        profile.ClearSkillLetterFamily(new[] { "A21F", "A21H" });

        Assert.False(profile.SkillLetters.ContainsKey("A21F"));
        Assert.False(profile.SkillLetters.ContainsKey("A21H"));
    }
}
