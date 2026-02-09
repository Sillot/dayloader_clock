using System.Globalization;
using System.Windows;
using System.Windows.Input;
using DayloaderClock.Models;
using Microsoft.Win32;

namespace DayloaderClock;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; private set; }

    public SettingsWindow(AppSettings currentSettings)
    {
        InitializeComponent();
        Settings = currentSettings;

        // Populate fields from current settings
        txtWorkHours.Text = (currentSettings.WorkDayMinutes / 60.0).ToString("F1", CultureInfo.InvariantCulture);
        txtLunchStart.Text = currentSettings.LunchStartTime;
        txtLunchDuration.Text = currentSettings.LunchDurationMinutes.ToString();
        chkAutoStart.IsChecked = currentSettings.AutoStartWithWindows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // ── Validation ──

        if (!double.TryParse(
                txtWorkHours.Text.Replace(",", "."),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double workHours)
            || workHours <= 0 || workHours > 24)
        {
            ShowError("Heures de travail invalides (entre 1 et 24).");
            return;
        }

        if (!TimeSpan.TryParse(txtLunchStart.Text, out _))
        {
            ShowError("Heure de début de pause invalide (format HH:mm).");
            return;
        }

        if (!int.TryParse(txtLunchDuration.Text, out int lunchMinutes)
            || lunchMinutes < 0 || lunchMinutes > 180)
        {
            ShowError("Durée de pause invalide (entre 0 et 180 minutes).");
            return;
        }

        // ── Apply ──

        Settings = new AppSettings
        {
            WorkDayMinutes = (int)(workHours * 60),
            LunchStartTime = txtLunchStart.Text.Trim(),
            LunchDurationMinutes = lunchMinutes,
            AutoStartWithWindows = chkAutoStart.IsChecked == true,
            WindowLeft = Settings.WindowLeft,
            WindowTop = Settings.WindowTop,
            WindowWidth = Settings.WindowWidth,
            WindowHeight = Settings.WindowHeight
        };

        SetAutoStart(Settings.AutoStartWithWindows);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    // ── Helpers ───────────────────────────────────────────────

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "Erreur de validation",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// Register / unregister auto-start via the Windows Registry (current user).
    /// </summary>
    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
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
