using System.Windows;
using LexusWarKey.Windows;

namespace LexusWarKey.Views;

public sealed record OverlaySlot(string Number, string Name, string KeyName, string RowBackground);

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowChrome.MakeNonActivating(this);
    }

    public void Show(IReadOnlyList<OverlaySlot> slots, string prompt)
    {
        SlotList.ItemsSource = slots;
        PromptText.Text = prompt;
        if (!IsVisible)
        {
            PositionTopRight();
            base.Show();
        }
    }

    private void PositionTopRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 24;
        Top = area.Top + 24;
    }
}
