using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Wheel and side buttons are bindable like keys. The rules that matter: an unbound
/// mouse control must never be swallowed (the wheel still zooms the camera), and the mouse hook
/// must only exist while something is actually on the mouse — it sees every movement report.</summary>
public class MouseBindingTests
{
    private static (RemapEngine engine, WarKeyProfile profile) Setup()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = new CommandCard
        {
            TopLeftX = 1000, TopLeftY = 800, BottomRightX = 1150, BottomRightY = 900,
        };
        return (new RemapEngine(() => profile, () => true), profile);
    }

    [Fact]
    public void Nothing_on_the_mouse_means_no_hook_and_nothing_claimed()
    {
        var (engine, _) = Setup();

        Assert.False(engine.AnyMouseControlBound());
        Assert.False(engine.ClaimsMouseControl(VirtualKeys.WheelUp));
        Assert.False(engine.ClaimsMouseControl(VirtualKeys.MButton));
    }

    [Fact]
    public void A_skill_on_the_wheel_is_claimed_and_fires_its_slot()
    {
        var (engine, profile) = Setup();
        profile.Skills[8].FromVk = VirtualKeys.WheelDown;
        profile.Skills[8].Enabled = true;

        Assert.True(engine.AnyMouseControlBound());
        Assert.True(engine.ClaimsMouseControl(VirtualKeys.WheelDown));
        Assert.False(engine.ClaimsMouseControl(VirtualKeys.WheelUp)); // the other way still zooms

        var decision = engine.Decide(VirtualKeys.WheelDown, true, false, false);
        Assert.Equal(RemapAction.ClickSlot, decision.Action);
        Assert.Equal(1000, decision.ClickAt!.X);
        Assert.Equal(900, decision.ClickAt.Y); // slot 9 = bottom-left
    }

    [Fact]
    public void An_inventory_key_on_a_side_button_remaps_like_any_key()
    {
        var (engine, profile) = Setup();
        profile.Inventory[0].FromVk = VirtualKeys.XButton1;
        profile.Inventory[0].Enabled = true;

        Assert.True(engine.ClaimsMouseControl(VirtualKeys.XButton1));

        var decision = engine.Decide(VirtualKeys.XButton1, true, false, false);
        Assert.Equal(RemapAction.SendKey, decision.Action);
        Assert.Equal(VirtualKeys.NumPad7, decision.SendVk);
    }

    [Fact]
    public void A_chat_macro_on_the_middle_button_is_claimed()
    {
        var (engine, profile) = Setup();
        profile.ChatMacros[0].HotkeyVk = VirtualKeys.MButton;

        Assert.True(engine.AnyMouseControlBound());
        Assert.True(engine.ClaimsMouseControl(VirtualKeys.MButton));
        Assert.Equal(RemapAction.SendChat, engine.Decide(VirtualKeys.MButton, true, false, false).Action);
    }

    [Fact]
    public void A_disabled_or_empty_binding_does_not_claim_the_mouse()
    {
        var (engine, profile) = Setup();
        profile.Skills[0].FromVk = VirtualKeys.WheelUp;
        profile.Skills[0].Enabled = false;

        Assert.False(engine.AnyMouseControlBound());
        Assert.False(engine.ClaimsMouseControl(VirtualKeys.WheelUp));
    }

    [Fact]
    public void Claiming_ignores_focus_and_the_master_switch()
    {
        // Those decide what an event DOES. If they gated claiming too, a bound wheel would
        // reach the game as a raw zoom the moment the app was paused — worse than either
        // outcome the user chose.
        var profile = WarKeyProfile.CreateDefault();
        profile.Enabled = false;
        profile.Skills[8].FromVk = VirtualKeys.WheelDown;
        profile.Skills[8].Enabled = true;
        var engine = new RemapEngine(() => profile, () => false);

        Assert.True(engine.ClaimsMouseControl(VirtualKeys.WheelDown));
        Assert.Equal(RemapAction.PassThrough,
            engine.Decide(VirtualKeys.WheelDown, true, false, false).Action);
    }

    [Fact]
    public void Mouse_controls_are_offered_in_the_picker_and_have_readable_names()
    {
        var offered = VirtualKeys.SelectableKeys.Select(k => k.Vk).ToList();

        foreach (var vk in new[] { VirtualKeys.WheelUp, VirtualKeys.WheelDown,
                                   VirtualKeys.MButton, VirtualKeys.XButton1, VirtualKeys.XButton2 })
        {
            Assert.Contains(vk, offered);
            Assert.DoesNotContain("0x", VirtualKeys.NameOf(vk)); // a name, not a raw code
            Assert.True(VirtualKeys.IsMouse(vk));
        }
    }

    [Fact]
    public void Mouse_codes_can_never_collide_with_a_keyboard_key()
    {
        // The wheel codes sit above the byte range a keyboard hook can produce; the button
        // codes are real VKs that a keyboard hook never reports.
        Assert.True(VirtualKeys.WheelUp > 0xFF);
        Assert.True(VirtualKeys.WheelDown > 0xFF);
        Assert.False(VirtualKeys.IsMouse('Q'));
        Assert.False(VirtualKeys.IsMouse(VirtualKeys.Space));
        Assert.False(VirtualKeys.IsMouse(VirtualKeys.LButton));  // never bindable
        Assert.False(VirtualKeys.IsMouse(VirtualKeys.RButton));
    }

    [Fact]
    public void Two_slots_on_one_mouse_control_are_reported_as_a_conflict()
    {
        var (_, profile) = Setup();
        profile.Skills[8].FromVk = VirtualKeys.WheelUp;
        profile.Skills[8].Enabled = true;
        profile.Skills[9].FromVk = VirtualKeys.WheelUp;
        profile.Skills[9].Enabled = true;

        Assert.Contains(VirtualKeys.WheelUp, RemapEngine.FindConflicts(profile));
    }
}
