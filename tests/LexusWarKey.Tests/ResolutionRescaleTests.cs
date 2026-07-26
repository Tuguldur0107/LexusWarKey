using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The field incident behind this: the user switched their display from 1920x1200 to
/// 2560x1600 and every stored pixel — corners and hand-placed rings alike — kept pointing at
/// where the card USED to be, which read as "my settings are gone". The calibration now knows
/// which screen it was measured on and moves itself when that changes.</summary>
public class ResolutionRescaleTests
{
    private static CommandCard CardAt1920x1200() => new()
    {
        // A plausible card on a 16:10 1920x1200 screen (bottom-right quadrant).
        TopLeftX = 1528, TopLeftY = 975,
        BottomRightX = 1841, BottomRightY = 1150,
        CapturedWidth = 1920, CapturedHeight = 1200,
    };

    [Fact]
    public void The_users_exact_switch_moves_every_point_onto_the_new_screen()
    {
        var card = CardAt1920x1200();
        card.SetOverrides(Enumerable.Range(0, CommandCard.Slots)
            .Select(i => new ScreenPoint(1528 + i % 4 * 104, 975 + i / 4 * 87)).ToList());

        var changed = card.RescaleTo(2560, 1600); // 1920x1200 -> 2560x1600 is exactly 4/3

        Assert.True(changed);
        Assert.Equal(1528 * 4 / 3, card.TopLeftX);
        Assert.Equal(975 * 4 / 3, card.TopLeftY);
        Assert.Equal((int)Math.Round(1841 * 4.0 / 3), card.BottomRightX);
        Assert.Equal(2560, card.CapturedWidth);

        var firstRing = card.PointFor(0)!;
        Assert.Equal(1528 * 4 / 3, firstRing.X); // overrides moved with it
        Assert.True(card.IsCalibrated);
    }

    [Fact]
    public void Switching_back_lands_within_a_pixel_of_where_it_started()
    {
        var card = CardAt1920x1200();
        var originalX = card.TopLeftX;
        var originalY = card.BottomRightY;

        card.RescaleTo(2560, 1600);
        card.RescaleTo(1920, 1200);

        Assert.InRange(Math.Abs(card.TopLeftX - originalX), 0, 1);
        Assert.InRange(Math.Abs(card.BottomRightY - originalY), 0, 1);
    }

    [Fact]
    public void The_same_screen_changes_nothing()
    {
        var card = CardAt1920x1200();

        Assert.False(card.RescaleTo(1920, 1200));
        Assert.Equal(1528, card.TopLeftX);
    }

    [Fact]
    public void A_legacy_card_adopts_the_current_screen_without_moving()
    {
        // Profiles from before CapturedWidth existed: their pixels belong to whatever screen
        // the user is on right now — moving them would be inventing an origin.
        var card = CardAt1920x1200();
        card.CapturedWidth = 0;
        card.CapturedHeight = 0;

        var changed = card.RescaleTo(2560, 1600);

        Assert.False(changed);
        Assert.Equal(1528, card.TopLeftX);       // untouched
        Assert.Equal(2560, card.CapturedWidth);  // origin recorded from here on
    }

    [Fact]
    public void An_uncalibrated_card_only_records_the_screen()
    {
        var card = new CommandCard();

        Assert.False(card.RescaleTo(2560, 1600));
        Assert.Equal(2560, card.CapturedWidth);
        Assert.False(card.IsCalibrated);
    }

    [Fact]
    public void Nonsense_screen_sizes_are_ignored()
    {
        var card = CardAt1920x1200();

        Assert.False(card.RescaleTo(0, 0));
        Assert.Equal(1920, card.CapturedWidth);
    }

    [Fact]
    public void The_captured_screen_survives_a_profile_round_trip()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = CardAt1920x1200();

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        };
        var restored = System.Text.Json.JsonSerializer.Deserialize<WarKeyProfile>(
            System.Text.Json.JsonSerializer.Serialize(profile, options), options)!;

        Assert.Equal(1920, restored.CommandCard.CapturedWidth);
        Assert.Equal(1200, restored.CommandCard.CapturedHeight);
    }
}
