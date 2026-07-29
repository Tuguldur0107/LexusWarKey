using LexusWarKey.Core;
using LexusWarKey.Windows;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>A member reported the rings sitting nowhere near the command card. The default was
/// a fraction of the SCREEN, which is only right when Warcraft fills it — windowed, the card is
/// at the window's bottom-right and a screen-fraction guess lands on bare desktop. The default
/// is now measured against wherever the game is actually drawing.</summary>
public class GameAreaSeedTests
{
    [Fact]
    public void A_fullscreen_game_seeds_exactly_as_before()
    {
        var fromScreen = CommandCard.DefaultFor(2560, 1600);
        var fromArea = CommandCard.DefaultForArea(0, 0, 2560, 1600);

        Assert.Equal(fromScreen.TopLeftX, fromArea.TopLeftX);
        Assert.Equal(fromScreen.BottomRightY, fromArea.BottomRightY);
    }

    [Fact]
    public void A_windowed_game_seeds_inside_its_window_not_the_screen()
    {
        // 1280x720 window sitting at (300, 150) on a 2560x1600 desktop.
        var card = CommandCard.DefaultForArea(300, 150, 1280, 720);

        Assert.True(card.IsCalibrated);
        Assert.InRange(card.TopLeftX, 300, 300 + 1280);
        Assert.InRange(card.BottomRightY, 150, 150 + 720);
        Assert.True(card.FitsInside(300, 150, 1280, 720));

        // And it is nowhere near where the old screen-fraction default would have put it.
        var wrong = CommandCard.DefaultFor(2560, 1600);
        Assert.True(Math.Abs(wrong.TopLeftX - card.TopLeftX) > 400);
    }

    [Theory]
    [InlineData(1024, 768)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1600)]
    public void Every_common_window_size_seeds_something_valid(int width, int height)
    {
        var card = CommandCard.DefaultForArea(0, 0, width, height);

        Assert.True(card.IsCalibrated);
        Assert.Null(CommandCard.Validate(card.TopLeftX, card.TopLeftY, card.BottomRightX, card.BottomRightY));
        Assert.True(card.FitsInside(0, 0, width, height));
    }

    [Fact]
    public void A_card_from_another_resolution_is_detected_as_outside_the_window()
    {
        // Linked at 2560x1600 fullscreen, then the player switched the game to a 1280x720 window.
        var card = CommandCard.DefaultFor(2560, 1600);

        Assert.False(card.FitsInside(0, 0, 1280, 720));
    }

    [Fact]
    public void Hand_placed_rings_outside_the_window_are_caught_too()
    {
        var card = CommandCard.DefaultForArea(0, 0, 1920, 1080);
        Assert.True(card.FitsInside(0, 0, 1920, 1080));

        var strayed = Enumerable.Range(0, CommandCard.Slots)
            .Select(_ => new ScreenPoint(1900, 1000)).ToList();
        strayed[5] = new ScreenPoint(3000, 1000); // dragged onto a second monitor
        card.SetOverrides(strayed);

        Assert.False(card.FitsInside(0, 0, 1920, 1080));
    }

    [Fact]
    public void An_unlinked_card_never_reports_a_mismatch()
    {
        // Nothing to contradict — the problem panel already says it is unlinked.
        Assert.True(new CommandCard().FitsInside(0, 0, 1280, 720));
    }

    [Fact]
    public void The_window_size_is_recorded_so_a_later_change_can_rescale()
    {
        var card = CommandCard.DefaultForArea(300, 150, 1280, 720);

        Assert.Equal(1280, card.CapturedWidth);
        Assert.Equal(720, card.CapturedHeight);
    }

    /// <summary>Measured on the real thing: a MINIMISED war3 answers every geometry call happily
    /// and reports a 237x39 client area parked at (-32000,-32000). Nothing used to reject that,
    /// so with the game in the taskbar the app took that rectangle for the game and acted on it.
    /// Both consequences were silent and both looked like the app being broken: a linked card was
    /// declared "outside the Warcraft window" with instructions to re-link it, and an unlinked one
    /// would be seeded into coordinates no click on this planet can reach.</summary>
    [Fact]
    public void A_minimised_game_is_not_a_game_area()
    {
        Assert.False(GameWindowWatcher.IsPlausibleGameArea(minimised: true, 237, 39));
        // Even at a believable size — minimised is minimised, wherever Windows parked it.
        Assert.False(GameWindowWatcher.IsPlausibleGameArea(minimised: true, 2560, 1600));
    }

    [Theory]
    [InlineData(237, 39)]     // what a minimised war3 actually reports
    [InlineData(0, 0)]
    [InlineData(1280, 200)]   // a sliver: tall enough to pass the old > 0 test, far too short
    public void A_client_area_too_small_to_be_the_game_is_refused(int width, int height)
    {
        Assert.False(GameWindowWatcher.IsPlausibleGameArea(minimised: false, width, height));
    }

    [Theory]
    [InlineData(1024, 768)]
    [InlineData(1280, 720)]
    [InlineData(2560, 1600)]
    public void A_real_window_is_accepted(int width, int height)
    {
        Assert.True(GameWindowWatcher.IsPlausibleGameArea(minimised: false, width, height));
    }

    /// <summary>The exact false alarm this caused: a perfectly good card, judged against the
    /// stub rectangle a minimised game reports, reads as "outside the Warcraft window".</summary>
    [Fact]
    public void The_minimised_rectangle_would_have_condemned_a_perfectly_good_card()
    {
        var card = CommandCard.DefaultFor(2560, 1600);

        Assert.True(card.FitsInside(0, 0, 2560, 1600));
        Assert.False(card.FitsInside(-32000, -32000, 237, 39));
    }
}
