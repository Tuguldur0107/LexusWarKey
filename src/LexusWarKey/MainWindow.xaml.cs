using System.Windows;
using System.Windows.Input;
using LexusWarKey.ViewModels;

namespace LexusWarKey;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // PreviewKeyDown so Tab/Space/arrows reach us before WPF turns them into focus moves.
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => Windows.WindowChrome.UseDarkTitleBar(this);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vm.HandleCaptureKey(vk))
            e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as MainViewModel)?.Shutdown();
        base.OnClosed(e);
    }
}
