using System.Globalization;
using System.Threading;
using System.Windows;
using DayloaderClock.Resources;
using DayloaderClock.Services;
using Microsoft.Win32;

namespace DayloaderClock;

// CA1001: _mutex is disposed in OnExit — WPF Application should not implement IDisposable
#pragma warning disable CA1001
public partial class App : Application
#pragma warning restore CA1001
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Apply language setting before any UI is created
        ApplyLanguageSetting();

        const string mutexName = "DayloaderClock_SingleInstance_Mutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            // Wait briefly in case this is a language-change restart
            Thread.Sleep(1500);
            _mutex = new Mutex(true, mutexName, out createdNew);
        }

        if (!createdNew)
        {
            MessageBox.Show(
                Strings.Msg_AlreadyRunning,
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
    /// Apply the user's language preference before any UI is created.
    /// "auto" = use system default, otherwise set the specified culture.
    /// </summary>
    private static void ApplyLanguageSetting()
    {
        try
        {
            var settings = StorageService.Instance.LoadSettings();
            if (settings.Language != "auto" && !string.IsNullOrEmpty(settings.Language))
            {
                var culture = new CultureInfo(settings.Language);
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
            }
        }
        catch
        {
            // If the culture code is invalid, just use system default
        }
    }

    /// <summary>
    /// On every launch, ensure the Windows Registry auto-start key
    /// matches the persisted setting (registers on first run since default is true).
    /// </summary>
    private static void EnsureAutoStartRegistered()
    {
        try
        {
            var settings = StorageService.Instance.LoadSettings();
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
