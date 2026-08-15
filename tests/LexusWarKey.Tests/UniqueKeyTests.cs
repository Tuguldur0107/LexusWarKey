using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>A physical key can only be bound in one place across skills AND inventory: casting a skill
/// and using an item off the same key would collide, so claiming a key frees it from everywhere else.</summary>
public class UniqueKeyTests
{
    [Fact]
    public void Assigning_a_skill_letter_frees_it_from_another_skill()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.SetSkillLetter("AAAA", "Q");

        profile.SetSkillLetter("BBBB", "Q");

        Assert.False(profile.SkillLetters.ContainsKey("AAAA"));
        Assert.Equal("Q", profile.SkillLetters["BBBB"]);
    }

    [Fact]
    public void Assigning_a_skill_letter_frees_the_same_key_from_an_inventory_slot()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.SetInventoryKey(0, 'F');           // item on F

        profile.SetSkillLetter("AAAA", "F");       // now a skill wants F

        Assert.Equal(0, profile.Inventory[0].FromVk);
        Assert.False(profile.Inventory[0].Enabled);
        Assert.Equal("F", profile.SkillLetters["AAAA"]);
    }

    [Fact]
    public void Assigning_an_inventory_key_frees_it_from_a_skill()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.SetSkillLetter("AAAA", "D");

        profile.SetInventoryKey(0, 'D');

        Assert.False(profile.SkillLetters.ContainsKey("AAAA"));
        Assert.Equal((int)'D', profile.Inventory[0].FromVk);
        Assert.True(profile.Inventory[0].Enabled);
    }

    [Fact]
    public void Assigning_an_inventory_key_frees_it_from_another_slot()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.SetInventoryKey(0, VirtualKeys.Space);

        profile.SetInventoryKey(3, VirtualKeys.Space);

        Assert.Equal(0, profile.Inventory[0].FromVk);
        Assert.Equal(VirtualKeys.Space, profile.Inventory[3].FromVk);
    }

    [Fact]
    public void A_non_letter_inventory_key_does_not_touch_skill_letters()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.SetSkillLetter("AAAA", "Q");

        profile.SetInventoryKey(0, VirtualKeys.Space);   // space is not any skill letter

        Assert.Equal("Q", profile.SkillLetters["AAAA"]);
        Assert.Equal(VirtualKeys.Space, profile.Inventory[0].FromVk);
    }
}
