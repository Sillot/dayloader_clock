using System.Windows;
using DayloaderClock.Services;
using Microsoft.Win32;

namespace DayloaderClock;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "DayloaderClock_SingleInstance_Mutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "Dayloader Clock est déjà en cours d'exécution.\nVérifiez la barre des tâches (system tray).",
                "Dayloader Clock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Ensure auto-start registry key is in sync with settings on every launch
        EnsureAutoStartRegistered();

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// On every launch, ensure the Windows Registry auto-start key
    /// matches the persisted setting (registers on first run since default is true).
    /// </summary>
    private static void EnsureAutoStartRegistered()
    {
        try
        {
            var settings = StorageService.LoadSettings();
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (settings.AutoStartWithWindows)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue("DayloaderClock", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("DayloaderClock", false);
            }
        }
        catch
        {
            // Registry access may be restricted in corporate environments
        }
    }
}
