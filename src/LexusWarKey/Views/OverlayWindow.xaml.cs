using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LexusWarKey.Core;
using LexusWarKey.Windows;

namespace LexusWarKey.Views;

public sealed record OverlaySlot(SlotGroup Group, int Index, string KeyName, string Background, string Border);

public partial class OverlayWindow : Window
{
    private bool _dragging;
    private Point _dragStart;
    private double _startLeft, _startTop;

    // Cell size is user-adjustable so the command-card grid can be laid exactly over the
    // game's own buttons — that alignment IS the calibration (see Link_Click).
    private double _cellWidth = 62;
    private double _cellHeight = 46;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowChrome.MakeNonActivating(this);

        DragHandle.MouseLeftButtonDown += OnDragStart;
        DragHandle.MouseMove += OnDragMove;
        DragHandle.MouseLeftButtonUp += OnDragEnd;

        ResizeGrip.DragDelta += OnResizeDelta;
        ResizeGrip.DragCompleted += (_, _) => Resized?.Invoke(_cellWidth, _cellHeight);
        ApplyCellSize();
    }

    /// <summary>Raised when a slot is clicked. The overlay never takes focus, so clicking it
    /// does not pull the player out of the game.</summary>
    public event Action<SlotGroup, int>? SlotClicked;

    /// <summary>Raised after the user drags the panel, so the position can be remembered.</summary>
    public event Action<double, double>? Moved;

    /// <summary>Raised after the user resizes the cells, so the size can be remembered.</summary>
    public event Action<double, double>? Resized;

    /// <summary>Raised when the user presses "Холбох" with the panel laid over the game's
    /// command card: carries the screen centres of the first and last card cells, which is
    /// exactly the two-corner calibration — captured from where the user aligned the grid,
    /// with no corner-clicking ceremony.</summary>
    public event Action<ScreenPoint, ScreenPoint>? LinkRequested;

    /// <summary>Raised when the user asks to SEE where the twelve slots will click.</summary>
    public event Action? MarkersRequested;

    public void ShowSlots(IReadOnlyList<OverlaySlot> inventory, IReadOnlyList<OverlaySlot> skills, string prompt)
    {
        InventoryList.ItemsSource = inventory;
        SkillList.ItemsSource = skills;
        PromptText.Text = prompt;
        if (!IsVisible)
            Show();
    }

    public void PlaceAt(double? left, double? top)
    {
        var area = SystemParameters.WorkArea;
        if (left is { } l && top is { } t && IsOnScreen(l, t, area))
        {
            Left = l;
            Top = t;
        }
        else
        {
            // First run (or a screen that no longer exists): sit out of the way, top-right.
            Left = area.Right - Math.Max(ActualWidth, 520) - 24;
            Top = area.Top + 24;
        }
    }

    public void SetCellSize(double? width, double? height)
    {
        if (width is { } w && w is >= 30 and <= 240)
            _cellWidth = w;
        if (height is { } h && h is >= 24 and <= 200)
            _cellHeight = h;
        ApplyCellSize();
    }

    private void ApplyCellSize()
    {
        InventoryList.Width = _cellWidth * 2;
        InventoryList.Height = _cellHeight * 3;
        SkillList.Width = _cellWidth * CommandCard.Columns;
        SkillList.Height = _cellHeight * CommandCard.Rows;
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        _cellWidth = Math.Clamp(_cellWidth + e.HorizontalChange / CommandCard.Columns, 30, 240);
        _cellHeight = Math.Clamp(_cellHeight + e.VerticalChange / CommandCard.Rows, 24, 200);
        ApplyCellSize();
    }

    private static bool IsOnScreen(double left, double top, Rect area) =>
        left > area.Left - 200 && left < area.Right - 80 &&
        top > area.Top - 40 && top < area.Bottom - 60;

    private void Slot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OverlaySlot slot })
        {
            SlotClicked?.Invoke(slot.Group, slot.Index);
            e.Handled = true;
        }
    }

    private void Link_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var first = CellCentreOnScreen(0);
        var last = CellCentreOnScreen(CommandCard.Slots - 1);
        if (first is not null && last is not null)
            LinkRequested?.Invoke(first, last);
    }

    private void Markers_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        MarkersRequested?.Invoke();
    }

    /// <summary>Physical screen centre of a command-card cell. WPF under PerMonitorV2 returns
    /// device pixels from PointToScreen, the same space the click sender works in.</summary>
    private ScreenPoint? CellCentreOnScreen(int index)
    {
        if (SkillList.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement cell
            || cell.ActualWidth <= 0)
            return null;

        var centre = cell.PointToScreen(new Point(cell.ActualWidth / 2, cell.ActualHeight / 2));
        return new ScreenPoint((int)Math.Round(centre.X), (int)Math.Round(centre.Y));
    }

    // Manual drag: DragMove() wants an activatable window, and this one deliberately is not.
    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = PointToScreen(e.GetPosition(this));
        _startLeft = Left;
        _startTop = Top;
        DragHandle.CaptureMouse();
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;
        var now = PointToScreen(e.GetPosition(this));
        Left = _startLeft + (now.X - _dragStart.X);
        Top = _startTop + (now.Y - _dragStart.Y);
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        DragHandle.ReleaseMouseCapture();
        Moved?.Invoke(Left, Top);
    }
}
