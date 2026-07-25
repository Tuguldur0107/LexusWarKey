namespace LexusWarKey.Core;

public sealed record ScreenPoint(int X, int Y);

/// <summary>Where Warcraft's command card sits on screen.
///
/// The card is a single 4x3 grid of 12 buttons (Blizzard's CommandFunc.txt: Move/Stop/Hold/
/// Attack across the TOP row, Patrol and friends in the middle, hero abilities along the
/// BOTTOM row — LoD's extra skills spill into the middle row). Modelling anything smaller
/// than the full card was this class's original sin: users naturally click its true corners
/// when calibrating, and a 4x2 model then puts every middle-row slot a full row off.
///
/// This exists because of game modes like LoD, where the abilities you get are random every
/// match: their hotkey letters are unknown and can collide, so the only stable way to reach
/// "the ability in slot N" is to click slot N. Two known corners define the whole grid; the
/// rest is interpolation, which keeps this correct at any resolution without hard-coding
/// Blizzard's layout constants. Note the pitches genuinely differ per axis on widescreen —
/// 1.26a stretches its 4:3 UI horizontally — so column step and row step must stay independent.</summary>
public sealed class CommandCard
{
    public const int Columns = 4;
    public const int Rows = 3;
    public const int Slots = Columns * Rows;

    /// <summary>Centre of slot 1 — the top-left button (Move, in an unmodified game).</summary>
    public int TopLeftX { get; set; }
    public int TopLeftY { get; set; }

    /// <summary>Centre of slot 12 — the bottom-right button (Cancel / the last ability).</summary>
    public int BottomRightX { get; set; }
    public int BottomRightY { get; set; }

    /// <summary>Hand-placed positions, one per slot, set by dragging the rings in the adjust
    /// window. A null entry (or a missing list) falls back to corner interpolation. This exists
    /// because no formula survives contact with every resolution and UI mod — when the maths
    /// puts a ring half a button off, the user just drags it onto the button and keeps playing.</summary>
    public List<ScreenPoint?>? Overrides { get; set; }

    /// <summary>The whole card spans hundreds of pixels at any playable resolution — even at
    /// 800x600 its three column steps cover well over 100. A live profile was found "calibrated"
    /// to a 54x25 box because the old threshold (20) only guarded against clicking the same
    /// button twice; every interpolated slot then landed within one button of the first.</summary>
    private const int MinimumSpanX = 80;
    private const int MinimumSpanY = 50;

    public bool IsCalibrated =>
        BottomRightX - TopLeftX >= MinimumSpanX && BottomRightY - TopLeftY >= MinimumSpanY;

    /// <summary>Why a calibration attempt was rejected, or null when it is good.</summary>
    public static string? Validate(int x1, int y1, int x2, int y2)
    {
        if (Math.Abs(x2 - x1) < MinimumSpanX)
            return "Хоёр цэг хэвтээ чиглэлд хэт ойрхон байна — зүүн дээд (Move) болон баруун доод булангийн нүд байх ёстой.";
        if (Math.Abs(y2 - y1) < MinimumSpanY)
            return "Хоёр цэг босоо чиглэлд хэт ойрхон байна — баруун доод нүд нь зүүн дээдээс ХОЁР эгнээ доор байна.";

        // A grid this shape cannot exist on any real monitor: the game's UI is 4:3 stretched
        // to the screen's aspect, so the step ratio stays within a narrow band (1.0 on 4:3,
        // ~1.35 on 16:9, ~1.9 on 21:9). Far outside it means the wrong buttons were marked —
        // most commonly a middle-row cell taken for the bottom corner.
        var stepX = Math.Abs(x2 - x1) / (double)(Columns - 1);
        var stepY = Math.Abs(y2 - y1) / (double)(Rows - 1);
        if (stepX / stepY is < 0.7 or > 2.4)
            return "Хоёр цэгийн хоорондох зай картын хэлбэрт тохирохгүй байна — буруу нүд тэмдэглэсэн бололтой. " +
                   "Зүүн дээд болон баруун доод БУЛАНГИЙН нүдийг сонгоно уу.";
        return null;
    }

    /// <summary>Screen position of a slot, 0-based, left-to-right then top-to-bottom —
    /// the same order the game draws them.</summary>
    public ScreenPoint? PointFor(int slotIndex)
    {
        if (!IsCalibrated || slotIndex < 0 || slotIndex >= Slots)
            return null;

        // A hand-placed position always beats the formula — it is the user telling us
        // exactly where the button really is.
        if (Overrides is not null && slotIndex < Overrides.Count && Overrides[slotIndex] is { } placed)
            return placed;

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
        Overrides = null;
    }

    /// <summary>Replaces every slot position with an explicit point. Used when the user has
    /// dragged the rings into place — from then on the grid is exactly what they see.</summary>
    public void SetOverrides(IReadOnlyList<ScreenPoint> points)
    {
        Overrides = points.Select(p => (ScreenPoint?)p).ToList();
    }
}
