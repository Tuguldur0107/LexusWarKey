using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>The game's card is a perfectly even grid; hands are not. FitRegularGrid removes
/// the tremor from dragged ring positions — these tests pin it down with the user's real
/// saved points, which wobbled by up to 10px and looked ragged on screen.</summary>
public class GridFitTests
{
    // Verbatim from the user's profile.json: twelve hand-dragged ring positions.
    private static readonly ScreenPoint[] HandPlaced =
    {
        new(2038, 1306), new(2178, 1297), new(2310, 1298), new(2439, 1299),
        new(2035, 1413), new(2181, 1414), new(2314, 1410), new(2458, 1418),
        new(2040, 1528), new(2180, 1533), new(2320, 1534), new(2462, 1539),
    };

    [Fact]
    public void The_fitted_grid_is_perfectly_even()
    {
        var snapped = CommandCard.FitRegularGrid(HandPlaced);

        // Same column -> same X; same row -> same Y (within rounding).
        for (var c = 0; c < 4; c++)
        {
            Assert.InRange(Math.Abs(snapped[c].X - snapped[c + 4].X), 0, 1);
            Assert.InRange(Math.Abs(snapped[c].X - snapped[c + 8].X), 0, 1);
        }
        for (var r = 0; r < 3; r++)
        {
            var row = snapped.Skip(r * 4).Take(4).ToList();
            Assert.InRange(Math.Abs(row[0].Y - row[3].Y), 0, 1);
        }

        // Even pitch between neighbouring columns and rows.
        var step01 = snapped[1].X - snapped[0].X;
        var step12 = snapped[2].X - snapped[1].X;
        var step23 = snapped[3].X - snapped[2].X;
        Assert.InRange(Math.Abs(step01 - step12), 0, 1);
        Assert.InRange(Math.Abs(step12 - step23), 0, 1);

        var rowStep1 = snapped[4].Y - snapped[0].Y;
        var rowStep2 = snapped[8].Y - snapped[4].Y;
        Assert.InRange(Math.Abs(rowStep1 - rowStep2), 0, 1);
    }

    [Fact]
    public void The_fitted_grid_stays_close_to_where_the_hand_put_each_ring()
    {
        var snapped = CommandCard.FitRegularGrid(HandPlaced);

        // The worst wobble in the real data is ~14px (ring 4 at x=2439 against a column
        // whose true centre the fit puts at ~2453) — that ring WAS the outlier being fixed.
        for (var i = 0; i < HandPlaced.Length; i++)
        {
            Assert.InRange(Math.Abs(snapped[i].X - HandPlaced[i].X), 0, 20);
            Assert.InRange(Math.Abs(snapped[i].Y - HandPlaced[i].Y), 0, 20);
        }
    }

    [Fact]
    public void Fitting_twice_changes_nothing()
    {
        var once = CommandCard.FitRegularGrid(HandPlaced);
        var twice = CommandCard.FitRegularGrid(once);

        for (var i = 0; i < once.Count; i++)
        {
            Assert.InRange(Math.Abs(once[i].X - twice[i].X), 0, 1);
            Assert.InRange(Math.Abs(once[i].Y - twice[i].Y), 0, 1);
        }
    }

    [Fact]
    public void Loading_a_profile_tidies_jittery_saved_overrides()
    {
        var profile = WarKeyProfile.CreateDefault();
        profile.CommandCard = new CommandCard
        {
            TopLeftX = 2022, TopLeftY = 835, BottomRightX = 2301, BottomRightY = 974,
        };
        profile.CommandCard.SetOverrides(HandPlaced);

        profile.NormaliseSlots();

        var p0 = profile.CommandCard.PointFor(0)!;
        var p4 = profile.CommandCard.PointFor(4)!;
        Assert.InRange(Math.Abs(p0.X - p4.X), 0, 1); // same column now truly aligned
    }

    [Fact]
    public void A_partial_or_missing_override_set_is_left_alone()
    {
        var card = new CommandCard
        {
            TopLeftX = 1000, TopLeftY = 800, BottomRightX = 1150, BottomRightY = 900,
            Overrides = new List<ScreenPoint?> { new ScreenPoint(1, 1), null },
        };

        card.TidyOverrides();

        Assert.Equal(2, card.Overrides!.Count); // untouched — nothing to fit a grid to
        Assert.Equal(new ScreenPoint(1, 1), card.Overrides[0]);
    }

    [Fact]
    public void The_wrong_number_of_points_is_rejected_loudly()
    {
        Assert.Throws<ArgumentException>(() => CommandCard.FitRegularGrid(new[] { new ScreenPoint(1, 1) }));
    }
}
