using System.Windows;
using LexusWarKey.Core;
using LexusWarKey.Views;

namespace LexusWarKey;

public partial class App : Application
{
    private Mutex? _singleInstance;

    /// <summary>The signed-in Discord session, shared across the app.</summary>
    public static AuthService Auth { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Two instances would both hook the keyboard and double every remap.
        _singleInstance = new Mutex(initiallyOwned: false, @"Local\LexusWarKey");
        bool acquired;
        try { acquired = _singleInstance.WaitOne(TimeSpan.Zero, false); }
        catch (AbandonedMutexException) { acquired = true; }

        if (!acquired)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            MessageBox.Show("Lexus WarKey аль хэдийн ажиллаж байна.", "Lexus WarKey",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        // Discord login is required to use the app. A cached token lets the player back in
        // without re-authenticating (and keeps working offline); only a missing token forces
        // the login window. An expired token is caught later by the heartbeat.
        Auth = new AuthService();
        Auth.LoadToken();

        if (!Auth.IsLoggedIn)
        {
            var login = new LoginWindow(Auth);
            if (login.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch { }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
