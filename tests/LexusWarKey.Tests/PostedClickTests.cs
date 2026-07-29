using LexusWarKey.Windows;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Casting a slot posts mouse messages to the game's own window instead of dragging the
/// real cursor there and back. The cursor never moves, so nothing is taken from the player's aim
/// and there is no 35ms excursion for the next cast to queue behind.
///
/// This path existed once, was judged not to work, and was deleted — a conclusion drawn from an
/// experiment that never posted a single message, because every measurement behind it was taken
/// while Warcraft sat minimised reporting a 237x39 client area at (-32000,-32000). Re-tested with
/// the game in front, the game acts on these immediately. What is pinned down here is the piece
/// that can silently go wrong without anyone noticing: the coordinate packing.
///
/// Pure arithmetic — nothing is posted and no window is touched.</summary>
public class PostedClickTests
{
    private static int LowWord(int packed) => packed & 0xFFFF;
    private static int HighWord(int packed) => (packed >> 16) & 0xFFFF;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2456, 1538)]     // the user's Q slot, verified firing in game
    [InlineData(2037, 1421)]
    [InlineData(1, 1)]
    [InlineData(2559, 1599)]     // the far corner of a 2560x1600 client area
    public void A_client_point_survives_the_trip_into_a_mouse_message(int x, int y)
    {
        var packed = InputSender.PackClientPoint(x, y);

        Assert.Equal(x, LowWord(packed));
        Assert.Equal(y, HighWord(packed));
    }

    /// <summary>x lives in the low half and y in the high half. If an x wide enough to overflow
    /// its half were allowed to bleed upward it would arrive as a different ROW of the command
    /// card — a different ability, not a near miss — so the halves are masked apart explicitly.</summary>
    [Fact]
    public void A_wide_x_cannot_bleed_into_the_row()
    {
        var packed = InputSender.PackClientPoint(0x1_2345, 7);

        Assert.Equal(0x2345, LowWord(packed));
        Assert.Equal(7, HighWord(packed));
    }

    /// <summary>Every slot of a real card packs to a distinct message. Two slots colliding here
    /// would mean two different keys casting the same ability, with nothing on screen to show
    /// why the other one had stopped working.</summary>
    [Fact]
    public void Every_slot_of_a_real_card_packs_to_something_different()
    {
        var xs = new[] { 2037, 2176, 2316, 2456 };
        var ys = new[] { 1421, 1538 };

        var packed = (from x in xs from y in ys select InputSender.PackClientPoint(x, y)).ToList();

        Assert.Equal(packed.Count, packed.Distinct().Count());
    }
}
