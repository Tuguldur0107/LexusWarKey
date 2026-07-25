using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The in-game rebinding flow: the whole point is that the user never leaves the game,
/// so every step must be reachable with the keyboard alone.</summary>
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
    public void Picking_a_slot_then_a_key_binds_it_and_saves()
    {
        var (session, profile, saves) = Create();

        Assert.True(session.HandleKey('3'));                 // slot 3
        Assert.Equal(OverlayStep.WaitingForKey, session.Step);
        Assert.True(session.HandleKey('F'));                 // bind F

        Assert.Equal('F', profile.Inventory[2].FromVk);
        Assert.True(profile.Inventory[2].Enabled);
        Assert.Equal(OverlayStep.ChoosingSlot, session.Step); // ready for the next slot
        Assert.Equal(1, saves());
    }

    [Fact]
    public void Numpad_digits_select_slots_too()
    {
        var (session, profile, _) = Create();

        session.HandleKey(VirtualKeys.NumPad4);
        session.HandleKey('G');

        Assert.Equal('G', profile.Inventory[3].FromVk);
    }

    [Fact]
    public void Backspace_clears_a_slot()
    {
        var (session, profile, _) = Create();
        Assert.Equal(VirtualKeys.Space, profile.Inventory[0].FromVk); // default

        session.HandleKey('1');
        session.HandleKey(VirtualKeys.Back);

        Assert.Equal(0, profile.Inventory[0].FromVk);
        Assert.False(profile.Inventory[0].Enabled);
    }

    [Fact]
    public void Escape_closes_the_overlay_from_either_step()
    {
        var (session, _, _) = Create();
        Assert.False(session.HandleKey(VirtualKeys.Escape));

        session.Reset();
        session.HandleKey('2');
        Assert.False(session.HandleKey(VirtualKeys.Escape));
    }

    [Fact]
    public void Keys_that_are_not_slot_numbers_are_ignored_while_choosing()
    {
        var (session, profile, saves) = Create();

        Assert.True(session.HandleKey('Z'));
        Assert.True(session.HandleKey('9'));

        Assert.Equal(OverlayStep.ChoosingSlot, session.Step);
        Assert.Equal(0, saves());
        Assert.Equal(VirtualKeys.Space, profile.Inventory[0].FromVk); // untouched
    }

    [Fact]
    public void Prompt_tells_the_user_what_to_do_at_each_step()
    {
        var (session, _, _) = Create();
        Assert.Contains("1–6", session.Prompt);

        session.HandleKey('5');
        Assert.Contains("5", session.Prompt);
        Assert.Contains("Backspace", session.Prompt);
    }

    [Theory]
    [InlineData('1', 0)]
    [InlineData('6', 5)]
    [InlineData(VirtualKeys.NumPad1, 0)]
    [InlineData(VirtualKeys.NumPad6, 5)]
    [InlineData('7', -1)]
    [InlineData('0', -1)]
    [InlineData('A', -1)]
    public void Slot_index_mapping_covers_both_keyboards(int vk, int expected)
    {
        Assert.Equal(expected, OverlayConfigSession.SlotIndexFor(vk));
    }
}
