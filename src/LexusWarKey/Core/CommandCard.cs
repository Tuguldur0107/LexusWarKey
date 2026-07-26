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

    /// <summary>The physical screen size every stored pixel above was measured on. Absolute
    /// pixels silently break the moment the user changes resolution — a card calibrated at
    /// 1920x1200 pointed at the middle of a 2560x1600 screen, which read as "my settings are
    /// gone". With the origin recorded, <see cref="RescaleTo"/> moves everything to the new
    /// screen instead. Zero = a profile from before this existed.</summary>
    public int CapturedWidth { get; set; }
    public int CapturedHeight { get; set; }

    /// <summary>Converts every stored position onto a different screen size. Returns true when
    /// anything actually moved. A card with no recorded origin adopts the given size as its
    /// origin without moving — its pixels are presumed to belong to the screen it is on now.</summary>
    public bool RescaleTo(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
            return false;

        if (CapturedWidth <= 0 || CapturedHeight <= 0 || !IsCalibrated)
        {
            CapturedWidth = screenWidth;
            CapturedHeight = screenHeight;
            return false;
        }

        if (CapturedWidth == screenWidth && CapturedHeight == screenHeight)
            return false;

        var scaleX = (double)screenWidth / CapturedWidth;
        var scaleY = (double)screenHeight / CapturedHeight;

        TopLeftX = (int)Math.Round(TopLeftX * scaleX);
        TopLeftY = (int)Math.Round(TopLeftY * scaleY);
        BottomRightX = (int)Math.Round(BottomRightX * scaleX);
        BottomRightY = (int)Math.Round(BottomRightY * scaleY);

        if (Overrides is not null)
        {
            for (var i = 0; i < Overrides.Count; i++)
            {
                if (Overrides[i] is { } p)
                    Overrides[i] = new ScreenPoint(
                        (int)Math.Round(p.X * scaleX),
                        (int)Math.Round(p.Y * scaleY));
            }
        }

        CapturedWidth = screenWidth;
        CapturedHeight = screenHeight;
        return true;
    }

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

    /// <summary>Where the card sits on a standard fullscreen setup, as fractions of the
    /// physical screen. The fractions come from a real hand-verified layout (rings dragged
    /// onto the buttons and saved), and agree with the relative coordinates AucT's tool has
    /// shipped for two decades — so a fresh install starts with clicks already landing on
    /// the card, and the rings exist for whatever small correction remains.</summary>
    public static CommandCard DefaultFor(int screenWidth, int screenHeight) =>
        DefaultForArea(0, 0, screenWidth, screenHeight);

    /// <summary>The same fractions applied to an arbitrary rectangle — the game's client area
    /// when it is running, the whole screen otherwise. Windowed Warcraft draws its card at the
    /// WINDOW's bottom-right, so a screen-fraction default lands on empty desktop; this is why
    /// the app looks at where the game actually is before guessing.</summary>
    public static CommandCard DefaultForArea(int left, int top, int width, int height) => new()
    {
        TopLeftX = left + (int)Math.Round(width * 0.7961),
        TopLeftY = top + (int)Math.Round(height * 0.8125),
        BottomRightX = left + (int)Math.Round(width * 0.9590),
        BottomRightY = top + (int)Math.Round(height * 0.9581),
        CapturedWidth = width,
        CapturedHeight = height,
    };

    /// <summary>True when every slot lands inside the given rectangle. A calibration made at a
    /// different resolution, or before the game was windowed, points somewhere it cannot work —
    /// and that is invisible until the player casts and nothing happens.</summary>
    public bool FitsInside(int left, int top, int width, int height)
    {
        if (!IsCalibrated || width <= 0 || height <= 0)
            return true; // nothing to contradict

        return TopLeftX >= left && TopLeftY >= top
               && BottomRightX <= left + width && BottomRightY <= top + height
               && (Overrides is null
                   || Overrides.Where(p => p is not null).All(p =>
                       p!.X >= left && p.X <= left + width && p.Y >= top && p.Y <= top + height));
    }

    /// <summary>Replaces every slot position with an explicit point. Used when the user has
    /// dragged the rings into place — from then on the grid is exactly what they see.</summary>
    public void SetOverrides(IReadOnlyList<ScreenPoint> points)
    {
        Overrides = points.Select(p => (ScreenPoint?)p).ToList();
    }

    /// <summary>Snaps hand-dragged positions onto the perfectly even grid they were aiming at.
    ///
    /// The game's card is uniform — equal column pitch, equal row pitch — but a hand can't
    /// place twelve rings to the pixel, so saved positions carry a few pixels of jitter each
    /// and the whole thing looks ragged. This finds the evenly-spaced grid closest to the
    /// points (least squares on the column and row means) and returns every slot on it. The
    /// result is what the user meant, minus the hand tremor.</summary>
    public static List<ScreenPoint> FitRegularGrid(IReadOnlyList<ScreenPoint> points)
    {
        if (points.Count != Slots)
            throw new ArgumentException($"Expected {Slots} points, got {points.Count}.", nameof(points));

        var columnMeans = new double[Columns];
        for (var c = 0; c < Columns; c++)
            columnMeans[c] = Enumerable.Range(0, Rows).Average(r => (double)points[r * Columns + c].X);

        var rowMeans = new double[Rows];
        for (var r = 0; r < Rows; r++)
            rowMeans[r] = Enumerable.Range(0, Columns).Average(c => (double)points[r * Columns + c].Y);

        var (x0, stepX) = FitLine(columnMeans);
        var (y0, stepY) = FitLine(rowMeans);

        var snapped = new List<ScreenPoint>(Slots);
        for (var slot = 0; slot < Slots; slot++)
        {
            var col = slot % Columns;
            var row = slot / Columns;
            snapped.Add(new ScreenPoint(
                (int)Math.Round(x0 + stepX * col),
                (int)Math.Round(y0 + stepY * row)));
        }
        return snapped;
    }

    /// <summary>Least-squares line through equally spaced samples: intercept and step.</summary>
    private static (double Intercept, double Step) FitLine(double[] means)
    {
        var n = means.Length;
        var centre = (n - 1) / 2.0;
        double numerator = 0, denominator = 0;
        for (var i = 0; i < n; i++)
        {
            numerator += (i - centre) * means[i];
            denominator += (i - centre) * (i - centre);
        }
        var step = numerator / denominator;
        var intercept = means.Average() - step * centre;
        return (intercept, step);
    }

    /// <summary>Applies <see cref="FitRegularGrid"/> to stored overrides, when they are a full
    /// set. Runs on every profile load, so jitter saved by an older build tidies itself.</summary>
    public void TidyOverrides()
    {
        if (Overrides is null || Overrides.Count != Slots || Overrides.Any(p => p is null))
            return;
        SetOverrides(FitRegularGrid(Overrides.Select(p => p!).ToList()));
    }
}
