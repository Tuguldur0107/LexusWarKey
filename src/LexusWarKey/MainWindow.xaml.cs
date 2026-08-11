using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LexusWarKey.ViewModels;

namespace LexusWarKey;

public partial class MainWindow : Window
{
    private TrayIcon? _tray;
    private bool _reallyExiting;
    private bool _hintShown;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => Windows.WindowChrome.UseDarkTitleBar(this);
        Loaded += (_, _) => _tray = new TrayIcon(this, ExitForReal);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vm.HandleCaptureKey(vk))
            e.Handled = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyExiting)
        {
            e.Cancel = true;
            _tray?.HideWindow();
            if (!_hintShown)
            {
                _hintShown = true;
                _tray?.ShowFirstHideHint();
            }
            return;
        }

        base.OnClosing(e);
    }

    private void ExitForReal()
    {
        _reallyExiting = true;
        Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        Vm?.Shutdown();
        _tray?.Dispose();
        base.OnClosed(e);
    }
}
