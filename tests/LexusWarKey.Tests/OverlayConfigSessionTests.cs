using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The in-game rebinding flow: the whole point is that the user never leaves the game,
/// so every slot — including empty ones — must be reachable and bindable.</summary>
public class OverlayConfigSessionTests
{
    private static (OverlayConfigSession Session, WarKeyProfile Profile, Func<int> Saves) Create()
    {
        var profile = WarKeyProfile.CreateDefault();
        var saves = 0;
        var session = new OverlayConfigSession(profile, () => saves++);
        return (session, profile, () => saves);
    }

    [Fact]
    public void An_EMPTY_inventory_slot_can_be_given_a_key()
    {
        var (session, profile, saves) = Create();
        Assert.Equal(0, profile.Inventory[4].FromVk); // starts empty by default

        session.SelectSlot(SlotGroup.Inventory, 4);
        Assert.True(session.HandleKey('F'));

        Assert.Equal('F', profile.Inventory[4].FromVk);
        Assert.True(profile.Inventory[4].Enabled);
        Assert.Equal(1, saves());
    }

    [Fact]
    public void An_EMPTY_skill_slot_can_be_given_a_key()
    {
        var (session, profile, saves) = Create();
        Assert.All(profile.Skills, s => Assert.Equal(0, s.FromVk)); // all empty by default

        session.SelectSlot(SlotGroup.Skill, 2);
        Assert.True(session.HandleKey('E'));

        Assert.Equal('E', profile.Skills[2].FromVk);
        Assert.True(profile.Skills[2].Enabled);
        Assert.Equal(1, saves());
    }

    [Fact]
    public void Every_slot_in_both_grids_is_selectable()
    {
        var (session, profile, _) = Create();

        for (var i = 0; i < profile.Inventory.Count; i++)
        {
            session.SelectSlot(SlotGroup.Inventory, i);
            Assert.Equal(OverlayStep.WaitingForKey, session.Step);
            Assert.Equal(SlotGroup.Inventory, session.SelectedGroup);
            Assert.Equal(i, session.SelectedIndex);
            session.Reset();
        }

        for (var i = 0; i < profile.Skills.Count; i++)
        {
            session.SelectSlot(SlotGroup.Skill, i);
            Assert.Equal(SlotGroup.Skill, session.SelectedGroup);
            Assert.Equal(i, session.SelectedIndex);
            session.Reset();
        }
    }

    [Fact]
    public void Digit_shortcut_still_picks_inventory_slots()
    {
        var (session, profile, _) = Create();

        Assert.True(session.HandleKey('3'));
        Assert.Equal(OverlayStep.WaitingForKey, session.Step);
        Assert.Equal(SlotGroup.Inventory, session.SelectedGroup);
        session.HandleKey('G');

        Assert.Equal('G', profile.Inventory[2].FromVk);
    }

    [Fact]
    public void Digits_can_themselves_be_bound_once_a_slot_is_chosen()
    {
        var (session, profile, _) = Create();

        session.SelectSlot(SlotGroup.Skill, 0);
        session.HandleKey('4'); // a digit as the binding, not as a slot picker

        Assert.Equal('4', profile.Skills[0].FromVk);
    }

    [Fact]
    public void Backspace_clears_the_selected_slot()
    {
        var (session, profile, _) = Create();
        Assert.Equal(VirtualKeys.Space, profile.Inventory[0].FromVk);

        session.SelectSlot(SlotGroup.Inventory, 0);
        session.HandleKey(VirtualKeys.Back);

        Assert.Equal(0, profile.Inventory[0].FromVk);
        Assert.False(profile.Inventory[0].Enabled);
    }

    [Fact]
    public void Escape_backs_out_of_a_slot_before_it_closes_the_overlay()
    {
        var (session, _, _) = Create();

        session.SelectSlot(SlotGroup.Skill, 1);
        Assert.True(session.HandleKey(VirtualKeys.Escape));      // deselects, stays open
        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);

        Assert.False(session.HandleKey(VirtualKeys.Escape));     // now closes
    }

    [Fact]
    public void Out_of_range_selections_are_ignored()
    {
        var (session, _, _) = Create();

        session.SelectSlot(SlotGroup.Skill, 99);
        session.SelectSlot(SlotGroup.Inventory, -1);

        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);
    }

    [Fact]
    public void Binding_the_same_key_in_both_grids_is_reported_as_a_conflict()
    {
        var (session, profile, _) = Create();

        session.SelectSlot(SlotGroup.Inventory, 3);
        session.HandleKey('R');
        session.SelectSlot(SlotGroup.Skill, 3);
        session.HandleKey('R');

        Assert.Contains('R', RemapEngine.FindConflicts(profile));
    }

    [Fact]
    public void Prompt_names_the_grid_being_edited()
    {
        var (session, _, _) = Create();
        Assert.Contains("Нүд дээр дар", session.Prompt);

        session.SelectSlot(SlotGroup.Skill, 5);
        Assert.Contains("Чадвар 6", session.Prompt);

        session.Reset();
        session.SelectSlot(SlotGroup.Inventory, 1);
        Assert.Contains("Эд зүйл 2", session.Prompt);
    }

    [Theory]
    [InlineData('1', 0)]
    [InlineData('6', 5)]
    [InlineData(VirtualKeys.NumPad1, 0)]
    [InlineData(VirtualKeys.NumPad6, 5)]
    [InlineData('7', -1)]
    [InlineData('A', -1)]
    public void Inventory_digit_mapping_covers_both_keyboards(int vk, int expected)
    {
        Assert.Equal(expected, OverlayConfigSession.InventoryIndexFor(vk));
    }
}
