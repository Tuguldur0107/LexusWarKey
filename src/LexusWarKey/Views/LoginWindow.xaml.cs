using System.Windows;
using LexusWarKey.Core;

namespace LexusWarKey.Views;

public partial class LoginWindow : Window
{
    private readonly AuthService _auth;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public LoginWindow(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        _busy = true;
        LoginButton.IsEnabled = false;
        StatusText.Text = "Browser нээгдэж байна — Discord дээр зөвшөөрөл өгсний дараа эндээ автоматаар орно…";

        _cts = new CancellationTokenSource();
        bool ok;
        try
        {
            ok = await _auth.LoginAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;   // cancelled: the window is already closing
        }
        catch
        {
            ok = false;
        }

        if (ok)
        {
            DialogResult = true;
            return;
        }

        StatusText.Text = "Нэвтрэлт амжилтгүй боллоо. Дахин оролдоно уу.";
        LoginButton.IsEnabled = true;
        _busy = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
