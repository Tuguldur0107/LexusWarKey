using System.Windows;

namespace LexusWarKey;

public partial class App : Application
{
    private Mutex? _singleInstance;

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
