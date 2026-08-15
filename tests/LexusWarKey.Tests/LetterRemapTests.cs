using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

public class LetterRemapTests
{
    // Skills are written straight to memory now, so the engine only handles inventory (key->key)
    // and QuickChat. These tests exercise that key->key path via inventory.
    private static (RemapEngine Engine, WarKeyProfile Profile) Create()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Inventory[0].FromVk = 'Q';
        profile.Inventory[0].ToVk = 'T';
        profile.Inventory[0].Enabled = true;
        return (new RemapEngine(() => profile, () => true), profile);
    }

    [Fact]
    public void An_inventory_cell_sends_its_game_key_on_both_down_and_up()
    {
        var (engine, _) = Create();

        Assert.Equal('T', engine.Decide('Q', true, false, false).SendVk);
        Assert.Equal('T', engine.Decide('Q', false, false, false).SendVk);
    }

    [Fact]
    public void Nothing_the_engine_returns_moves_the_cursor()
    {
        var (engine, profile) = Create();
        profile.Inventory[1].FromVk = 'W';
        profile.Inventory[1].Enabled = true;

        foreach (var vk in new[] { 'Q', 'W', 'Z' })
        {
            var action = engine.Decide(vk, true, false, false).Action;
            Assert.True(action is RemapAction.SendKey or RemapAction.PassThrough or RemapAction.SendChat);
        }
    }

    [Fact]
    public void An_inventory_cell_sends_its_prefilled_numpad_game_key()
    {
        var (engine, profile) = Create();   // Inventory[1] keeps its default game key (NumPad8)
        profile.Inventory[1].FromVk = 'W';
        profile.Inventory[1].Enabled = true;

        Assert.Equal(VirtualKeys.NumPad8, engine.Decide('W', true, false, false).SendVk);
    }

}
