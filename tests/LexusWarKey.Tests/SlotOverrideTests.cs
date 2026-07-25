using System.Text.Json;
using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Hand-dragged ring positions exist because no interpolation formula survives every
/// resolution and UI mod. Once the user has placed a ring by eye, that position is the truth —
/// it must beat the formula, survive a restart, and vanish when the card is re-linked.</summary>
public class SlotOverrideTests
{
    private static CommandCard Calibrated() => new()
    {
        TopLeftX = 1000, TopLeftY = 800,
        BottomRightX = 1150, BottomRightY = 900,
    };

    [Fact]
    public void A_hand_placed_position_beats_the_interpolated_one()
    {
        var card = Calibrated();
        card.SetOverrides(Enumerable.Range(0, CommandCard.Slots)
            .Select(i => new ScreenPoint(2000 + i, 3000 + i)).ToList());

        Assert.Equal(new ScreenPoint(2000, 3000), card.PointFor(0));
        Assert.Equal(new ScreenPoint(2011, 3011), card.PointFor(11));
    }

    [Fact]
    public void A_null_entry_falls_back_to_interpolation()
    {
        var card = Calibrated();
        card.Overrides = new List<ScreenPoint?> { null, new ScreenPoint(9999, 9999) };

        Assert.Equal(new ScreenPoint(1000, 800), card.PointFor(0));  // interpolated
        Assert.Equal(new ScreenPoint(9999, 9999), card.PointFor(1)); // hand-placed
        Assert.Equal(new ScreenPoint(1100, 800), card.PointFor(2));  // beyond the list: interpolated
    }

    [Fact]
    public void Overrides_survive_a_profile_round_trip()
    {
        // Grid-shaped positions, because loading also tidies: NormaliseSlots snaps overrides
        // onto the even grid they describe, and an already-even grid must come through intact.
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = Calibrated();
        profile.CommandCard.SetOverrides(Enumerable.Range(0, CommandCard.Slots)
            .Select(i => new ScreenPoint(1000 + i % 4 * 100, 800 + i / 4 * 80)).ToList());

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var restored = JsonSerializer.Deserialize<WarKeyProfile>(
            JsonSerializer.Serialize(profile, options), options)!;
        restored.NormaliseSlots();

        Assert.Equal(new ScreenPoint(1300, 960), restored.CommandCard.PointFor(11));
    }

    [Fact]
    public void Relinking_the_card_drops_the_old_hand_positions()
    {
        // The rings were dragged relative to where the card USED to be — after a re-link
        // (new resolution, moved window) they would point at stale pixels.
        var card = Calibrated();
        card.SetOverrides(Enumerable.Range(0, CommandCard.Slots)
            .Select(_ => new ScreenPoint(1, 1)).ToList());

        card.Overrides = null; // what both calibration flows do on success

        Assert.Equal(new ScreenPoint(1000, 800), card.PointFor(0));
    }

    [Fact]
    public void Clearing_the_card_clears_the_hand_positions_too()
    {
        var card = Calibrated();
        card.SetOverrides(Enumerable.Range(0, CommandCard.Slots)
            .Select(_ => new ScreenPoint(1, 1)).ToList());

        card.Clear();

        Assert.Null(card.Overrides);
        Assert.False(card.IsCalibrated);
    }

    [Fact]
    public void An_uncalibrated_card_returns_nothing_even_with_overrides()
    {
        // Overrides are corrections to a linked card, not a substitute for linking one.
        var card = new CommandCard();
        card.SetOverrides(Enumerable.Range(0, CommandCard.Slots)
            .Select(_ => new ScreenPoint(500, 500)).ToList());

        Assert.Null(card.PointFor(0));
    }
}
