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

    /// <summary>Slot centres must be far enough apart to describe a real grid — clicking the
    /// same button twice used to leave a "calibrated" card that silently did nothing.</summary>
    private const int MinimumSpan = 20;

    public bool IsCalibrated =>
        BottomRightX - TopLeftX >= MinimumSpan && BottomRightY - TopLeftY >= MinimumSpan;

    /// <summary>Why a calibration attempt was rejected, or null when it is good.</summary>
    public static string? Validate(int x1, int y1, int x2, int y2)
    {
        if (Math.Abs(x2 - x1) < MinimumSpan)
            return "Хоёр товшилт хэвтээ чиглэлд хэт ойрхон байна — 1-р нүд, дараа нь 4 нүдээр баруун тийш байрлах 8-р нүдийг дарна уу.";
        if (Math.Abs(y2 - y1) < MinimumSpan)
            return "Хоёр товшилт босоо чиглэлд хэт ойрхон байна — 8-р нүд нь 1-р нүднээс НЭГ ЭГНЭЭ доор байна.";
        return null;
    }

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
