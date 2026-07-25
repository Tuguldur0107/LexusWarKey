using System.Windows;
using System.Windows.Controls;
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

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowChrome.MakeNonActivating(this);

        DragHandle.MouseLeftButtonDown += OnDragStart;
        DragHandle.MouseMove += OnDragMove;
        DragHandle.MouseLeftButtonUp += OnDragEnd;
    }

    /// <summary>Raised when a slot is clicked. The overlay never takes focus, so clicking it
    /// does not pull the player out of the game.</summary>
    public event Action<SlotGroup, int>? SlotClicked;

    /// <summary>Raised after the user drags the panel, so the position can be remembered.</summary>
    public event Action<double, double>? Moved;

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
            Left = area.Right - Math.Max(ActualWidth, 460) - 24;
            Top = area.Top + 24;
        }
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
