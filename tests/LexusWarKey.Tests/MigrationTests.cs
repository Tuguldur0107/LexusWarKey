using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>v1.2 changed the skill grid from a mistaken 4x2 to the game's real 4x3 card, and
/// started folding several same-key chat macros into one. Both changes rewrite the user's
/// stored profile, so both are pinned down here with the exact data seen in a live profile.</summary>
public class MigrationTests
{
    [Fact]
    public void Old_8_slot_profiles_keep_each_binding_on_the_button_it_was_actually_clicking()
    {
        // Old calibrations spanned the full card, so old row 0 = the real top row and
        // old row 1 = the real bottom row. The user's Q lived at old index 7.
        var profile = new WarKeyProfile();
        for (var i = 0; i < 8; i++)
            profile.Skills.Add(new KeyMap());
        profile.Skills[7].FromVk = 'Q';
        profile.Skills[7].Enabled = true;
        profile.Skills[0].FromVk = 'A';
        profile.Skills[0].Enabled = true;

        profile.NormaliseSlots();

        Assert.Equal(12, profile.Skills.Count);
        Assert.Equal('A', profile.Skills[0].FromVk);   // old top row stays the top row
        Assert.Equal('Q', profile.Skills[11].FromVk);  // old bottom-right is the real slot 12
        Assert.Equal(0, profile.Skills[7].FromVk);     // the middle row starts empty
    }

    [Fact]
    public void The_users_real_calibration_now_produces_a_sane_grid()
    {
        // Live numbers from the user's profile.json: corners of the full card on a 16:10
        // screen. Under the old 4x2 model the row step came out at 338px — half the card.
        var card = new CommandCard
        {
            TopLeftX = 1913, TopLeftY = 1219,
            BottomRightX = 2529, BottomRightY = 1557,
        };

        Assert.True(card.IsCalibrated);
        Assert.Null(CommandCard.Validate(1913, 1219, 2529, 1557));

        var firstAbility = card.PointFor(8)!;          // bottom row, first cell
        Assert.Equal(1913, firstAbility.X);
        Assert.Equal(1557, firstAbility.Y);

        var middle = card.PointFor(5)!;                // middle row lands mid-card, not below it
        Assert.Equal(1219 + 169, middle.Y);
    }

    [Fact]
    public void A_54_by_25_pixel_card_is_junk_and_no_longer_counts_as_calibrated()
    {
        // Also live data: a later re-calibration stored corners 54x25 apart — two clicks that
        // landed almost on top of each other, silently accepted by the old 20px threshold.
        var card = new CommandCard
        {
            TopLeftX = 1987, TopLeftY = 1210,
            BottomRightX = 2041, BottomRightY = 1235,
        };

        Assert.False(card.IsCalibrated);
        Assert.NotNull(CommandCard.Validate(1987, 1210, 2041, 1235));
    }

    [Fact]
    public void Six_macros_on_one_key_become_one_macro_that_sends_all_six()
    {
        // Exactly what the user's profile contained: six separate F5 macros, of which only
        // the first had ever fired.
        var profile = new WarKeyProfile();
        foreach (var line in new[] { "-sds6fnboulso", "-hhn", "-sddon", "-water 0 0 0", "/distance 2300", "/fps" })
            profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { line } });

        profile.NormaliseSlots();

        var macro = Assert.Single(profile.ChatMacros);
        Assert.Equal(
            new[] { "-sds6fnboulso", "-hhn", "-sddon", "-water 0 0 0", "/distance 2300", "/fps" },
            macro.Messages);

        var decision = new RemapEngine(() => profile, () => true)
            .Decide(VirtualKeys.F5, true, false, false);
        Assert.Equal(6, decision.ChatLines!.Count);
    }

    [Fact]
    public void Macros_on_different_keys_are_left_alone()
    {
        var profile = new WarKeyProfile();
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F2, Messages = { "-clear" } });
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F3, Messages = { "-ii" } });

        profile.NormaliseSlots();

        Assert.Equal(2, profile.ChatMacros.Count);
    }

    [Fact]
    public void A_disabled_duplicate_is_not_merged_into_the_live_macro()
    {
        var profile = new WarKeyProfile();
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "-hhn" } });
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F5, Messages = { "/fps" }, Enabled = false });

        profile.NormaliseSlots();

        Assert.Equal(2, profile.ChatMacros.Count);
        Assert.Equal(new[] { "-hhn" }, profile.ChatMacros[0].Messages);
    }

    [Theory]
    [InlineData(2560, 1600)] // the machine the fractions were measured on
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(1280, 1024)] // 5:4 — worst stretch 1.26a is played at
    public void The_default_card_is_valid_at_common_resolutions(int width, int height)
    {
        var card = CommandCard.DefaultFor(width, height);

        Assert.True(card.IsCalibrated);
        Assert.Null(CommandCard.Validate(card.TopLeftX, card.TopLeftY, card.BottomRightX, card.BottomRightY));
    }

    [Fact]
    public void The_default_card_reproduces_the_hand_verified_positions_it_was_measured_from()
    {
        // The user dragged all 12 rings onto their real buttons (2560x1600 fullscreen) and
        // saved; these fractions came from those points. Allow a couple of pixels of rounding.
        var card = CommandCard.DefaultFor(2560, 1600);

        Assert.InRange(card.TopLeftX, 2036, 2040);       // measured 2038
        Assert.InRange(card.TopLeftY, 1298, 1302);       // measured 1300
        Assert.InRange(card.BottomRightX, 2453, 2457);   // measured 2455
        Assert.InRange(card.BottomRightY, 1531, 1535);   // measured 1533
    }

    [Fact]
    public void A_new_profile_is_born_with_12_slots_and_needs_no_migration()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.NormaliseSlots();

        Assert.Equal(12, profile.Skills.Count);
    }
}
