using LexusWarKey.Windows;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>A click now carries its destination with it, as an absolute mouse event, so that the
/// press cannot land somewhere else when the player's own hand moves the cursor in between. That
/// only works if the pixel survives the trip through the 0-65535 grid Windows uses.
///
/// Windows truncates on the way back, so a mapping that rounds down lands one pixel short every
/// time — and a command-card slot is small enough that consistently drifting toward one edge is
/// the difference between casting and not. These tests run the round trip Windows itself runs.
///
/// Pure arithmetic: no input is injected and no cursor moves.</summary>
public class AbsoluteClickTests
{
    /// <summary>How Windows turns an absolute mouse event back into a screen pixel.</summary>
    private static int Back(int normalised, int origin, int span) =>
        origin + (int)((long)normalised * span / 65536);

    [Theory]
    // one 1920x1080 monitor
    [InlineData(0, 0, 1920, 1080)]
    // a second monitor to the left, so the virtual desktop starts negative
    [InlineData(-1920, 0, 3840, 1080)]
    // portrait secondary above the primary, and an odd width
    [InlineData(0, -1200, 2561, 2280)]
    public void Every_pixel_across_the_desktop_comes_back_as_itself(int left, int top, int w, int h)
    {
        for (var x = left; x < left + w; x++)
        {
            var (dx, _) = InputSender.ToAbsolute(x, top, left, top, w, h);
            Assert.Equal(x, Back(dx, left, w));
        }

        for (var y = top; y < top + h; y++)
        {
            var (_, dy) = InputSender.ToAbsolute(left, y, left, top, w, h);
            Assert.Equal(y, Back(dy, top, h));
        }
    }

    [Fact]
    public void A_pixel_is_aimed_at_the_middle_of_its_band_not_the_edge()
    {
        // 1920 pixels share 65536 grid steps, so each pixel owns a band about 34 steps wide.
        // Landing on the first step of the band is a pixel away from the neighbour below it;
        // landing in the middle is seventeen steps clear of both.
        var (dx, _) = InputSender.ToAbsolute(100, 0, 0, 0, 1920, 1080);

        Assert.Equal(100, Back(dx, 0, 1920));
        Assert.InRange(dx, 3430, 3446);   // pixel 100's band, and near the middle of it
    }

    [Fact]
    public void The_grid_never_runs_past_what_a_mouse_event_can_carry()
    {
        var (dx, dy) = InputSender.ToAbsolute(1919, 1079, 0, 0, 1920, 1080);

        Assert.InRange(dx, 0, 65535);
        Assert.InRange(dy, 0, 65535);
        Assert.Equal(1919, Back(dx, 0, 1920));
        Assert.Equal(1079, Back(dy, 0, 1080));
    }

    [Fact]
    public void A_point_on_a_secondary_monitor_is_relative_to_the_virtual_desktop_not_the_screen()
    {
        // Primary on the right, secondary on the left: the game's card sits at x = -960 and a
        // mapping that ignored the origin would send the click to the far side of the desktop.
        var (dx, _) = InputSender.ToAbsolute(-960, 540, -1920, 0, 3840, 1080);

        Assert.Equal(-960, Back(dx, -1920, 3840));
        Assert.InRange(dx, 16000, 16600); // a quarter of the way across
    }

    [Fact]
    public void A_point_off_the_desktop_still_produces_a_legal_event()
    {
        // The card was calibrated on a monitor that has since been unplugged, or GetSystemMetrics
        // returned zero mid-reconfigure. Either way the answer must stay inside the grid: an
        // out-of-range value is not a click in an odd place, it is a malformed mouse event.
        foreach (var (dx, dy) in new[]
                 {
                     InputSender.ToAbsolute(500, 500, 0, 0, 0, 0),        // no desktop at all
                     InputSender.ToAbsolute(500, 500, 0, 0, 1, 1),        // one pixel of desktop
                     InputSender.ToAbsolute(9000, 9000, 0, 0, 1920, 1080),// far off the right
                     InputSender.ToAbsolute(-9000, -9000, 0, 0, 1920, 1080),
                 })
        {
            Assert.InRange(dx, 0, 65535);
            Assert.InRange(dy, 0, 65535);
        }
    }
}
