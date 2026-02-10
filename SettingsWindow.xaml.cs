using System.Globalization;
using System.Windows;
using System.Windows.Input;
using DayloaderClock.Helpers;
using DayloaderClock.Models;
using DayloaderClock.Resources;
using Microsoft.Win32;

namespace DayloaderClock;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; private set; }
    public bool LanguageChanged { get; private set; }

    /// <summary>Available languages: code → display name</summary>
    private static readonly (string Code, string Name)[] Languages =
    [
        ("auto", "🌐 Auto (system)"),
        ("en", "🇬🇧 English"),
        ("fr", "🇫🇷 Français"),
        ("es", "🇪🇸 Español")
    ];

    public SettingsWindow(AppSettings currentSettings)
    {
        InitializeComponent();
        Settings = currentSettings;

        // Version
        txtVersion.Text = AppVersion.Display;

        // Populate fields from current settings
        txtWorkHours.Text = (currentSettings.WorkDayMinutes / 60.0).ToString("F1", CultureInfo.InvariantCulture);
        txtLunchStart.Text = currentSettings.LunchStartTime;
        txtLunchDuration.Text = currentSettings.LunchDurationMinutes.ToString();
        chkAutoStart.IsChecked = currentSettings.AutoStartWithWindows;
        txtPomodoro.Text = currentSettings.PomodoroMinutes.ToString();
        chkPomodoroDnd.IsChecked = currentSettings.PomodoroDndEnabled;

        // Language picker
        foreach (var (code, name) in Languages)
            cmbLanguage.Items.Add(new System.Windows.Controls.ComboBoxItem { Tag = code, Content = name });
        var selectedIndex = Array.FindIndex(Languages, l => l.Code == currentSettings.Language);
        cmbLanguage.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
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
            ShowError(Strings.Settings_Error_WorkHours);
            return;
        }

        if (!TimeSpan.TryParse(txtLunchStart.Text, CultureInfo.InvariantCulture, out _))
        {
            ShowError(Strings.Settings_Error_LunchStart);
            return;
        }

        if (!int.TryParse(txtLunchDuration.Text, out int lunchMinutes)
            || lunchMinutes < 0 || lunchMinutes > 180)
        {
            ShowError(Strings.Settings_Error_LunchDuration);
            return;
        }

        if (!int.TryParse(txtPomodoro.Text, out int pomodoroMinutes)
            || pomodoroMinutes < 1 || pomodoroMinutes > 120)
        {
            ShowError(Strings.Settings_Error_Pomodoro);
            return;
        }

        // ── Apply ──

        var selectedLang = (cmbLanguage.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "auto";
        bool languageChanged = selectedLang != Settings.Language;

        Settings = new AppSettings
        {
            WorkDayMinutes = (int)(workHours * 60),
            LunchStartTime = txtLunchStart.Text.Trim(),
            LunchDurationMinutes = lunchMinutes,
            AutoStartWithWindows = chkAutoStart.IsChecked == true,
            PomodoroMinutes = pomodoroMinutes,
            PomodoroDndEnabled = chkPomodoroDnd.IsChecked == true,
            Language = selectedLang,
            WindowLeft = Settings.WindowLeft,
            WindowTop = Settings.WindowTop,
            WindowWidth = Settings.WindowWidth,
            WindowHeight = Settings.WindowHeight
        };

        SetAutoStart(Settings.AutoStartWithWindows);

        LanguageChanged = languageChanged;

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
        MessageBox.Show(message, Strings.Settings_Error_Title,
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
