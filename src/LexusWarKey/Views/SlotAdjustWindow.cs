using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LexusWarKey.Core;
using LexusWarKey.Windows;

namespace LexusWarKey.Views;

/// <summary>Shows a numbered ring exactly where each command-card slot will click, and lets
/// the user DRAG any ring onto the real button and save. This is the escape hatch for every
/// resolution, UI mod and stretched-console quirk at once: when the interpolated grid is even
/// slightly off, the user corrects it by eye in seconds instead of fighting the calibration.
///
/// The window covers the screen but is layered, so only the drawn rings and buttons catch the
/// mouse — everywhere else clicks fall straight through to the game. It never takes focus.</summary>
public sealed class SlotAdjustWindow : Window
{
    private static SlotAdjustWindow? _current;

    private readonly CommandCard _card;
    private readonly Action _onSaved;
    private readonly Canvas _canvas = new();
    private readonly Grid?[] _rings = new Grid?[CommandCard.Slots];

    private Grid? _dragging;
    private Point _dragOffset;

    /// <summary>Physical top-left of the virtual screen — (0,0) only when the primary monitor
    /// is the leftmost/topmost one. Every stored point is offset by this before drawing, and
    /// added back on save, so rings land correctly on any monitor.</summary>
    private int _deviceLeft;
    private int _deviceTop;

    private const double Ring = 38;

    private SlotAdjustWindow(CommandCard card, Action onSaved)
    {
        _card = card;
        _onSaved = onSaved;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null; // a null background is not hit-testable: empty space belongs to the game
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Content = _canvas;

        SourceInitialized += (_, _) =>
        {
            WindowChrome.MakeNonActivating(this);

            // Cover the whole VIRTUAL screen, not just the primary monitor. The card can be
            // calibrated on any monitor, and a window pinned to the primary would silently
            // clip every ring on the others — the diagnostic failing exactly where a
            // multi-monitor setup needs it most.
            _deviceLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            _deviceTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            var deviceWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            var deviceHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.MoveWindow(hwnd, _deviceLeft, _deviceTop, deviceWidth, deviceHeight, false);

            BuildVisuals();
        };
    }

    /// <summary>Opens the adjuster (closing any previous one). <paramref name="onSaved"/> runs
    /// only when the user presses save.</summary>
    public static void Open(CommandCard card, Action onSaved)
    {
        _current?.Close();
        var opened = new SlotAdjustWindow(card, onSaved);
        opened.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, opened))
                _current = null;
        };
        _current = opened;
        opened.Show();
    }

    private void BuildVisuals()
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                         ?? Matrix.Identity;

        // Only the bindable slots get a ring: the top row is Move/Stop/Hold/Attack and is not
        // shown anywhere else either.
        for (var slot = CommandCard.FirstBindableSlot; slot < CommandCard.Slots; slot++)
        {
            if (_card.PointFor(slot) is not { } p)
                continue;

            var dip = fromDevice.Transform(new Point(p.X - _deviceLeft, p.Y - _deviceTop));
            var ring = BuildRing(slot);
            _rings[slot] = ring;
            Canvas.SetLeft(ring, dip.X - Ring / 2);
            Canvas.SetTop(ring, dip.Y - Ring / 2);
            _canvas.Children.Add(ring);
        }

    }

    private Grid BuildRing(int slot)
    {
        var ring = new Grid { Width = Ring, Height = Ring, Cursor = Cursors.SizeAll, Tag = slot };
        ring.Children.Add(new Ellipse
        {
            Stroke = Brushes.White,
            StrokeThickness = 2.5,
            // A faint fill so the whole disc is grabbable, not just the 2.5px outline.
            Fill = new SolidColorBrush(Color.FromArgb(0x58, 0x00, 0x00, 0x00)),
        });
        ring.Children.Add(new TextBlock
        {
            Text = (slot + 1).ToString(),   // the card's own number, matching the panel
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        ring.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = ring;
            _dragOffset = e.GetPosition(ring);
            ring.CaptureMouse();
            e.Handled = true;
        };
        ring.MouseMove += (_, e) =>
        {
            if (_dragging != ring)
                return;
            var at = e.GetPosition(_canvas);
            Canvas.SetLeft(ring, at.X - _dragOffset.X);
            Canvas.SetTop(ring, at.Y - _dragOffset.Y);
        };
        ring.MouseLeftButtonUp += (_, _) =>
        {
            _dragging = null;
            ring.ReleaseMouseCapture();
        };

        return ring;
    }

    /// <summary>Current ring centres as physical screen points, or null if any ring is missing.</summary>
    private List<ScreenPoint>? ReadRingPositions()
    {
        var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
                       ?? Matrix.Identity;

        var points = new List<ScreenPoint>(CommandCard.BindableSlots);
        for (var slot = CommandCard.FirstBindableSlot; slot < CommandCard.Slots; slot++)
        {
            if (_rings[slot] is not { } ring)
                return null; // the card lost calibration mid-edit

            var centreDip = new Point(Canvas.GetLeft(ring) + Ring / 2, Canvas.GetTop(ring) + Ring / 2);
            var device = toDevice.Transform(centreDip);
            points.Add(new ScreenPoint(
                (int)Math.Round(device.X) + _deviceLeft,
                (int)Math.Round(device.Y) + _deviceTop));
        }
        return points;
    }

    /// <summary>The adjuster currently on screen, or null. Its controls live on the overlay
    /// panel, so something has to hold the reference between them.</summary>
    public static SlotAdjustWindow? Current => _current;

    /// <summary>Snaps every ring onto the even grid the hand placement was aiming at, live —
    /// so the user sees the tidy result before deciding to save it.</summary>
    public void TidyRings()
    {
        if (ReadRingPositions() is not { } points)
            return;

        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                         ?? Matrix.Identity;
        var snapped = CommandCard.FitRegularGrid(points, CommandCard.BindableRows);
        for (var slot = CommandCard.FirstBindableSlot; slot < CommandCard.Slots; slot++)
        {
            if (_rings[slot] is not { } ring)
                continue;
            var point = snapped[slot - CommandCard.FirstBindableSlot];
            var dip = fromDevice.Transform(new Point(point.X - _deviceLeft, point.Y - _deviceTop));
            Canvas.SetLeft(ring, dip.X - Ring / 2);
            Canvas.SetTop(ring, dip.Y - Ring / 2);
        }
    }

    public void SavePositions()
    {
        if (ReadRingPositions() is not { } points)
        {
            Close();
            return;
        }

        // Saved positions are always the fitted grid, never the raw hand placement: the card
        // is uniform, so the jitter was never intentional and only makes the rings look wrong.
        _card.SetBindableOverrides(CommandCard.FitRegularGrid(points, CommandCard.BindableRows));
        _onSaved();
        Close();
    }
}
