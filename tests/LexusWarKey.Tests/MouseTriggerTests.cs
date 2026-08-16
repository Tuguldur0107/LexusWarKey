using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Mouse wheel and side buttons are just more virtual-key codes, so they flow through the
/// same remap decisions as keyboard triggers.</summary>
public class MouseTriggerTests
{
    [Fact]
    public void Wheel_and_side_buttons_have_names()
    {
        Assert.Equal("Wheel ↑", VirtualKeys.NameOf(VirtualKeys.WheelUp));
        Assert.Equal("Wheel ↓", VirtualKeys.NameOf(VirtualKeys.WheelDown));
        Assert.Equal("Mouse4", VirtualKeys.NameOf(VirtualKeys.MouseX1));
        Assert.Equal("Mouse5", VirtualKeys.NameOf(VirtualKeys.MouseX2));
    }

    [Fact]
    public void Mouse_and_wheel_classification()
    {
        Assert.True(VirtualKeys.IsMouse(VirtualKeys.WheelUp));
        Assert.True(VirtualKeys.IsMouse(VirtualKeys.MouseX2));
        Assert.False(VirtualKeys.IsMouse('A'));
        Assert.True(VirtualKeys.IsWheel(VirtualKeys.WheelDown));
        Assert.False(VirtualKeys.IsWheel(VirtualKeys.MouseX1));
    }

    [Fact]
    public void An_inventory_slot_can_be_triggered_by_the_wheel()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.Inventory[0].FromVk = VirtualKeys.WheelUp;
        profile.Inventory[0].ToVk = VirtualKeys.NumPad7;
        profile.Inventory[0].Enabled = true;

        var d = new RemapEngine(() => profile, () => true)
            .Decide(VirtualKeys.WheelUp, isKeyDown: true, ctrlHeld: false, altHeld: false);

        Assert.Equal(RemapAction.SendKey, d.Action);
        Assert.Equal(VirtualKeys.NumPad7, d.SendVk);
    }

    [Fact]
    public void A_side_button_can_fire_quickchat()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.ChatMacros[0].HotkeyVk = VirtualKeys.MouseX1;
        profile.ChatMacros[0].Messages = new() { "-clear" };

        var d = new RemapEngine(() => profile, () => true)
            .Decide(VirtualKeys.MouseX1, isKeyDown: true, ctrlHeld: false, altHeld: false);

        Assert.Equal(RemapAction.SendChat, d.Action);
        Assert.Equal(new[] { "-clear" }, d.ChatLines);
    }
}
