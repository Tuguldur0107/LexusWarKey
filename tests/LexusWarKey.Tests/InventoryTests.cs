using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class InventoryTests
{
    [Fact]
    public void Default_profile_has_six_inventory_slots()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.NormaliseSlots();
        Assert.Equal(WarKeyProfile.InventorySlots, profile.Inventory.Count);
        Assert.Equal(6, profile.Inventory.Count);
    }

    [Fact]
    public void Old_profile_without_inventory_gets_six_empty_slots()
    {
        // A profile from before inventory existed: the list is empty/null.
        var profile = new WarKeyProfile { Skills = new(), Inventory = null!, ChatMacros = new() };
        profile.NormaliseSlots();
        Assert.Equal(6, profile.Inventory.Count);
        Assert.All(profile.Inventory, m => Assert.False(m.IsUsable));
    }

    [Fact]
    public void An_inventory_key_is_remapped_to_its_game_key()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Inventory[0].FromVk = 'B';
        profile.Inventory[0].ToVk = VirtualKeys.NumPad7;
        profile.Inventory[0].Enabled = true;
        var engine = new RemapEngine(() => profile, () => true);

        var d = engine.Decide('B', isKeyDown: true, ctrlHeld: false, altHeld: false);

        Assert.Equal(RemapAction.SendKey, d.Action);
        Assert.Equal(VirtualKeys.NumPad7, d.SendVk);
    }

    [Fact]
    public void A_key_used_by_both_a_skill_and_an_inventory_slot_is_a_conflict()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Skills[0] = new KeyMap { FromVk = 'R', ToVk = 'T', Enabled = true };
        profile.Inventory[0] = new KeyMap { FromVk = 'R', ToVk = VirtualKeys.NumPad4, Enabled = true };

        Assert.Equal(new[] { (int)'R' }, RemapEngine.FindConflicts(profile));
    }

    [Fact]
    public void Inventory_game_keys_default_to_the_numpad_hotkeys()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.NormaliseSlots();

        Assert.Equal(VirtualKeys.NumPad7, profile.Inventory[0].ToVk);
        Assert.Equal(VirtualKeys.NumPad2, profile.Inventory[5].ToVk);
    }

    [Fact]
    public void An_old_profile_gets_the_numpad_game_keys_filled_in()
    {
        // A profile from before the defaults existed: inventory keys are all unset.
        var profile = new WarKeyProfile { Skills = new(), Inventory = null!, ChatMacros = new() };
        profile.NormaliseSlots();

        Assert.Equal(VirtualKeys.NumPad7, profile.Inventory[0].ToVk);
        Assert.All(profile.Inventory, m => Assert.NotEqual(0, m.ToVk));
    }
}
