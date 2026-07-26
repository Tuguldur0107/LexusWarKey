using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Slots 1-4 are Move/Stop/Hold/Attack and are no longer shown — Garena's WarKey has
/// never shown them either. The requirement that matters is that hiding a row moves NOTHING:
/// the stored card stays a full 4x3 and every visible slot keeps the exact position it had.</summary>
public class HiddenTopRowTests
{
    private static CommandCard Linked() => new()
    {
        TopLeftX = 1000, TopLeftY = 800,
        BottomRightX = 1300, BottomRightY = 1000,
    };

    [Fact]
    public void Hiding_the_row_leaves_the_visible_slots_exactly_where_they_were()
    {
        var card = Linked();

        // Slot 5 is (col 0, row 1); slot 12 is (col 3, row 2).
        Assert.Equal(new ScreenPoint(1000, 900), card.PointFor(4));
        Assert.Equal(new ScreenPoint(1300, 1000), card.PointFor(11));
        Assert.Equal(new ScreenPoint(1100, 1000), card.PointFor(9));
    }

    [Fact]
    public void Linking_from_the_visible_corners_reproduces_the_same_card()
    {
        // The panel now reports slot 5 and slot 12; the app extrapolates the hidden top row
        // from the row pitch. Round-tripping must land back on the original card.
        var original = Linked();
        var visibleTopLeft = original.PointFor(CommandCard.FirstBindableSlot)!;
        var bottomRight = original.PointFor(CommandCard.Slots - 1)!;

        var rowPitch = bottomRight.Y - visibleTopLeft.Y;
        var rebuilt = new CommandCard
        {
            TopLeftX = visibleTopLeft.X,
            TopLeftY = visibleTopLeft.Y - rowPitch,
            BottomRightX = bottomRight.X,
            BottomRightY = bottomRight.Y,
        };

        Assert.Equal(original.TopLeftX, rebuilt.TopLeftX);
        Assert.Equal(original.TopLeftY, rebuilt.TopLeftY);
        Assert.Equal(original.BottomRightY, rebuilt.BottomRightY);
        for (var slot = CommandCard.FirstBindableSlot; slot < CommandCard.Slots; slot++)
            Assert.Equal(original.PointFor(slot), rebuilt.PointFor(slot));
    }

    [Fact]
    public void Hand_placed_rings_cover_the_visible_slots_and_leave_the_rest_interpolated()
    {
        var card = Linked();
        var placed = Enumerable.Range(0, CommandCard.BindableSlots)
            .Select(i => new ScreenPoint(2000 + i, 3000 + i)).ToList();

        card.SetBindableOverrides(placed);

        Assert.Equal(CommandCard.Slots, card.Overrides!.Count);
        Assert.Null(card.Overrides[0]);                                     // hidden row: formula
        Assert.Equal(new ScreenPoint(1000, 800), card.PointFor(0));         // still interpolated
        Assert.Equal(placed[0], card.PointFor(CommandCard.FirstBindableSlot));
        Assert.Equal(placed[^1], card.PointFor(CommandCard.Slots - 1));
    }

    [Fact]
    public void The_visible_rings_fit_their_own_two_row_grid()
    {
        var jittery = new List<ScreenPoint>
        {
            new(1000, 900), new(1103, 897), new(1198, 901), new(1301, 903),
            new(998, 1002), new(1101, 999), new(1202, 1001), new(1297, 998),
        };

        var snapped = CommandCard.FitRegularGrid(jittery, CommandCard.BindableRows);

        Assert.Equal(CommandCard.BindableSlots, snapped.Count);
        // Same column -> same X, same row -> same Y.
        Assert.InRange(Math.Abs(snapped[0].X - snapped[4].X), 0, 1);
        Assert.InRange(Math.Abs(snapped[0].Y - snapped[3].Y), 0, 1);
        // Even column pitch.
        Assert.InRange(Math.Abs((snapped[1].X - snapped[0].X) - (snapped[2].X - snapped[1].X)), 0, 1);
    }

    [Fact]
    public void Tidying_a_profile_that_only_pinned_the_visible_rings_keeps_the_hidden_row_free()
    {
        var card = Linked();
        card.SetBindableOverrides(Enumerable.Range(0, CommandCard.BindableSlots)
            .Select(i => new ScreenPoint(1000 + i % 4 * 100, 900 + i / 4 * 100)).ToList());

        card.TidyOverrides();

        Assert.Null(card.Overrides![0]);
        Assert.NotNull(card.Overrides[CommandCard.FirstBindableSlot]);
        Assert.Equal(new ScreenPoint(1000, 800), card.PointFor(0)); // hidden row untouched
    }

    [Fact]
    public void An_older_profile_that_pinned_all_twelve_still_tidies()
    {
        var card = Linked();
        card.SetOverrides(Enumerable.Range(0, CommandCard.Slots)
            .Select(i => new ScreenPoint(1000 + i % 4 * 100, 800 + i / 4 * 100)).ToList());

        card.TidyOverrides();

        Assert.All(card.Overrides!, p => Assert.NotNull(p));
        Assert.Equal(new ScreenPoint(1300, 1000), card.PointFor(11));
    }

    [Fact]
    public void The_bindable_range_is_the_bottom_two_rows()
    {
        Assert.Equal(4, CommandCard.FirstBindableSlot);
        Assert.Equal(8, CommandCard.BindableSlots);
        Assert.Equal(2, CommandCard.BindableRows);
        Assert.Equal(12, CommandCard.Slots); // geometry unchanged
    }
}
