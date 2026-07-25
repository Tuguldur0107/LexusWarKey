namespace LexusWarKey.Core;

public sealed record ScreenPoint(int X, int Y);

/// <summary>Where Warcraft's 4x3 command card sits on screen.
///
/// This exists because of game modes like LoD, where the abilities you get are random every
/// match: their hotkey letters are unknown and can collide, so the only stable way to reach
/// "the ability in slot N" is to click slot N. The user calibrates once by clicking the
/// top-left and bottom-right buttons, and everything else is interpolated — which keeps this
/// correct at any resolution or UI scale without guessing Blizzard's layout constants.</summary>
public sealed class CommandCard
{
    /// <summary>Four across, two down — the ability area of the command card, which is the
    /// part hero skills actually occupy (the bottom row is move/stop/hold).</summary>
    public const int Columns = 4;
    public const int Rows = 2;
    public const int Slots = Columns * Rows;

    /// <summary>Centre of slot 1 (top-left button).</summary>
    public int TopLeftX { get; set; }
    public int TopLeftY { get; set; }

    /// <summary>Centre of slot 8 (bottom-right button of the ability area).</summary>
    public int BottomRightX { get; set; }
    public int BottomRightY { get; set; }

    public bool IsCalibrated => BottomRightX > TopLeftX && BottomRightY > TopLeftY;

    /// <summary>Screen position of a slot, 0-based, left-to-right then top-to-bottom —
    /// the same order the game draws them.</summary>
    public ScreenPoint? PointFor(int slotIndex)
    {
        if (!IsCalibrated || slotIndex < 0 || slotIndex >= Slots)
            return null;

        var col = slotIndex % Columns;
        var row = slotIndex / Columns;

        // Two corners define the grid; the rest is linear interpolation between them.
        var stepX = (BottomRightX - TopLeftX) / (double)(Columns - 1);
        var stepY = (BottomRightY - TopLeftY) / (double)(Rows - 1);

        return new ScreenPoint(
            (int)Math.Round(TopLeftX + stepX * col),
            (int)Math.Round(TopLeftY + stepY * row));
    }

    public void Clear()
    {
        TopLeftX = TopLeftY = BottomRightX = BottomRightY = 0;
    }
}
