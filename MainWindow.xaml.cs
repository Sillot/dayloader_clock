using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DayloaderClock.Models;
using DayloaderClock.Services;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace DayloaderClock;

public partial class MainWindow : Window
{
    private SessionService _session = null!;
    private AppSettings _settings = null!;
    private DispatcherTimer _timer = null!;
    private System.Windows.Forms.NotifyIcon _trayIcon = null!;
    private System.Windows.Forms.ToolStripMenuItem _trayPauseItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _trayPomodoroItem = null!;
    private System.Windows.Forms.ToolStripMenuItem _trayStopItem = null!;
    private bool _isExiting;
    private bool _isStopped;

    // ── Pomodoro ──────────────────────────────────────────────
    private bool _pomodoroActive;
    private DateTime _pomodoroEndTime;
    private DateTime _pomodoroStartTime;
    private DispatcherTimer? _pomodoroTimer;

    // Number of pixel segments in the horizontal bar
    private const int SEGMENT_COUNT = 80;

    // ── Gradient stops (Green → Yellow → Orange → Red) ───────
    private static readonly Color ColorGreen  = Color.FromRgb(76, 217, 100);
    private static readonly Color ColorYellow = Color.FromRgb(255, 230, 50);
    private static readonly Color ColorOrange = Color.FromRgb(255, 149, 0);
    private static readonly Color ColorRed    = Color.FromRgb(235, 64, 52);
    private static readonly Color ColorEmpty  = Color.FromRgb(30, 26, 18);
    private static readonly Color ColorOvertime = Color.FromRgb(204, 51, 51);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public MainWindow()
    {
        InitializeComponent();

        // Version
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v?";
        txtTitle.ToolTip = $"Dayloader Clock {versionText}";
        txtVersion.Text = versionText;

        _settings = StorageService.LoadSettings();
        _session = new SessionService(_settings);
        _session.OvertimeStarted += OnOvertimeStarted;
        _session.PauseStateChanged += OnPauseStateChanged;

        InitializeTrayIcon();
        InitializeTimer();

        UpdateDisplay();
        RestoreWindowPosition();

        Closing += MainWindow_Closing;
        LocationChanged += MainWindow_LocationChanged;
        SizeChanged += MainWindow_SizeChanged;
    }

    // ── Initialization ────────────────────────────────────────

    private void InitializeTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Dayloader Clock",
            Visible = true,
            Icon = CreateTrayIcon(0)
        };

        _trayIcon.DoubleClick += (_, _) => ShowWindow();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Afficher", null, (_, _) => Dispatcher.Invoke(ShowWindow));        _trayStopItem = new System.Windows.Forms.ToolStripMenuItem("\u23F9 Fin de journ\u00e9e", null, (_, _) => Dispatcher.Invoke(ToggleStop));
        menu.Items.Add(_trayStopItem);        _trayPauseItem = new System.Windows.Forms.ToolStripMenuItem("⏸ Pause", null, (_, _) => Dispatcher.Invoke(() => _session.TogglePause()));
        menu.Items.Add(_trayPauseItem);
        _trayPomodoroItem = new System.Windows.Forms.ToolStripMenuItem("🍅 Pomodoro", null, (_, _) => Dispatcher.Invoke(TogglePomodoro));
        menu.Items.Add(_trayPomodoroItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("📅 Historique", null, (_, _) => Dispatcher.Invoke(OpenHistory));
        menu.Items.Add("⚙ Paramètres", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _trayIcon.ContextMenuStrip = menu;
    }

    private void InitializeTimer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) =>
        {
            _session.CheckNewDay();
            UpdateDisplay();
            _session.CheckAndNotifyOvertime();
            _session.SaveState();
        };
        _timer.Start();
    }

    // ── Display update ────────────────────────────────────────

    private void UpdateDisplay()
    {
        var effectiveWork = _session.GetEffectiveWorkTime();
        var remaining = _session.GetRemainingTime();
        var progress = _session.GetProgressPercent();
        var isOvertime = _session.IsOvertime;
        var isLunch = _session.IsLunchTime;
        var isPaused = _session.IsPaused;

        // ── Text labels ──
        txtStartTime.Text = $"Début: {_session.LoginTime:HH:mm}";
        txtElapsed.Text = FormatTime(effectiveWork);

        // Pause indicator
        txtPauseIndicator.Visibility = isPaused ? Visibility.Visible : Visibility.Collapsed;

        if (isOvertime)
        {
            var overtime = _session.GetOvertimeTime();
            txtRemaining.Text = "Terminé !";
            txtRemaining.Foreground = new SolidColorBrush(ColorOvertime);
            txtOvertime.Text = $"\u26A0 HEURES SUP: +{FormatTime(overtime)}";
            txtOvertime.Visibility = Visibility.Visible;
        }
        else
        {
            txtRemaining.Text = $"Reste: {FormatTime(remaining)}";
            txtRemaining.Foreground = new SolidColorBrush(Color.FromRgb(107, 80, 53));
            txtOvertime.Visibility = Visibility.Collapsed;
        }

        // Lunch indicator
        txtLunch.Visibility = isLunch ? Visibility.Visible : Visibility.Collapsed;

        // Progress
        var displayPercent = Math.Min(progress, 100);

        // Bar
        DrawBar(progress);

        // Tray icon
        UpdateTrayIcon(progress, effectiveWork, displayPercent);
    }

    // ── Bar drawing ───────────────────────────────────────────

    private void DrawBar(double progress)
    {
        barCanvas.Children.Clear();

        double canvasWidth = barCanvas.ActualWidth;
        double canvasHeight = barCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        // Dynamically compute segment count to fit the available width
        const double targetSegWidth = 4.5;
        const double gap = 2;
        int segCount = Math.Max(10, (int)((canvasWidth + gap) / (targetSegWidth + gap)));
        double segWidth = (canvasWidth - gap * (segCount - 1)) / segCount;
        if (segWidth < 2) segWidth = 2;

        int filledSegments = (int)Math.Min(
            Math.Floor(progress / 100.0 * segCount), segCount);
        bool isOvertime = progress > 100;

        for (int i = 0; i < segCount; i++)
        {
            var rect = new Rectangle
            {
                Width = segWidth,
                Height = canvasHeight,
                RadiusX = 2,
                RadiusY = 2
            };

            if (i < filledSegments)
            {
                var color = GetBarGradientColor(i, segCount);
                if (isOvertime)
                {
                    // Pulsing red tint in overtime
                    color = InterpolateColor(color, ColorOvertime, 0.4);
                }
                rect.Fill = new SolidColorBrush(color);
            }
            else
            {
                rect.Fill = new SolidColorBrush(ColorEmpty);
            }

            Canvas.SetLeft(rect, i * (segWidth + gap));
            Canvas.SetTop(rect, 0);
            barCanvas.Children.Add(rect);
        }
    }

    private void BarCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawBar(_session.GetProgressPercent());
    }

    private void UpdateTrayIcon(double progress, TimeSpan effectiveWork, double displayPercent)
    {
        var oldIcon = _trayIcon.Icon;
        _trayIcon.Icon = CreateTrayIcon(progress);
        _trayIcon.Text = $"Dayloader \u2013 {displayPercent:F0}% ({FormatTime(effectiveWork)})";

        if (oldIcon != null)
        {
            DestroyIcon(oldIcon.Handle);
            oldIcon.Dispose();
        }
    }

    // ── Color helpers ─────────────────────────────────────────

    /// <summary>
    /// Bar gradient: Green → Yellow → Orange → Red (matching physical device).
    /// </summary>
    private static Color GetBarGradientColor(int index, int total)
    {
        double t = (double)index / Math.Max(total - 1, 1);

        if (t < 0.33)
        {
            return InterpolateColor(ColorGreen, ColorYellow, t / 0.33);
        }
        else if (t < 0.66)
        {
            return InterpolateColor(ColorYellow, ColorOrange, (t - 0.33) / 0.33);
        }
        else
        {
            return InterpolateColor(ColorOrange, ColorRed, (t - 0.66) / 0.34);
        }
    }

    private static Color InterpolateColor(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    private static string FormatTime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
        return $"{ts.Minutes}m";
    }

    // ── Tray icon drawing ─────────────────────────────────────

    private static System.Drawing.Icon CreateTrayIcon(double progress)
    {
        using var bitmap = new System.Drawing.Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(System.Drawing.Color.FromArgb(44, 36, 25));

            // Horizontal bar in the tray icon (16px wide, centered)
            int barHeight = 6;
            int yOffset = 5;
            int filled = (int)(Math.Min(progress, 100) / 100.0 * 14);
            bool isOvertime = progress > 100;

            // Bar background
            using (var bgBrush = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(30, 26, 18)))
            {
                g.FillRectangle(bgBrush, 1, yOffset, 14, barHeight);
            }

            // Filled portion with gradient
            for (int x = 0; x < filled; x++)
            {
                double t = (double)x / 14.0;
                System.Drawing.Color c;
                if (isOvertime)
                {
                    c = System.Drawing.Color.FromArgb(204, 51, 51);
                }
                else if (t < 0.33)
                {
                    c = LerpDrawingColor(
                        System.Drawing.Color.FromArgb(76, 217, 100),
                        System.Drawing.Color.FromArgb(255, 230, 50), t / 0.33);
                }
                else if (t < 0.66)
                {
                    c = LerpDrawingColor(
                        System.Drawing.Color.FromArgb(255, 230, 50),
                        System.Drawing.Color.FromArgb(255, 149, 0), (t - 0.33) / 0.33);
                }
                else
                {
                    c = LerpDrawingColor(
                        System.Drawing.Color.FromArgb(255, 149, 0),
                        System.Drawing.Color.FromArgb(235, 64, 52), (t - 0.66) / 0.34);
                }

                using var brush = new System.Drawing.SolidBrush(c);
                g.FillRectangle(brush, 1 + x, yOffset, 1, barHeight);
            }
        }

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static System.Drawing.Color LerpDrawingColor(
        System.Drawing.Color from, System.Drawing.Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return System.Drawing.Color.FromArgb(
            (int)(from.R + (to.R - from.R) * t),
            (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
    }

    // ── Window management ─────────────────────────────────────

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _settings = win.Settings;
            StorageService.SaveSettings(_settings);
            _session.UpdateSettings(_settings);
            UpdateDisplay();
        }
    }

    private void OpenHistory()
    {
        ShowWindow();
        var win = new HistoryWindow { Owner = this };
        win.ShowDialog();
    }

    private void OnPauseStateChanged(bool isPaused)
    {
        Dispatcher.Invoke(() =>
        {
            btnPause.Content = isPaused ? "\u25B6" : "\u23F8";
            btnPause.ToolTip = isPaused ? "Reprendre" : "Pause";
            txtPauseIndicator.Visibility = (isPaused && !_isStopped) ? Visibility.Visible : Visibility.Collapsed;
            _trayPauseItem.Text = isPaused ? "\u25B6 Reprendre" : "\u23F8 Pause";
            _trayPauseItem.Visible = !_isStopped;
            UpdateDisplay();
        });
    }

    private void ExitApp()
    {
        // Stop Pomodoro and restore DND
        if (_pomodoroActive)
            StopPomodoro(cancelled: true);

        _isExiting = true;
        _timer.Stop();
        _session.SaveState();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    private void OnOvertimeStarted()
    {
        _trayIcon.ShowBalloonTip(
            5000,
            "Dayloader Clock",
            "\u26A0 Journée de travail terminée ! Les heures supplémentaires commencent.",
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void RestoreWindowPosition()
    {
        if (_settings.WindowWidth >= MinWidth && _settings.WindowHeight >= MinHeight
            && _settings.WindowWidth <= MaxWidth && _settings.WindowHeight <= MaxHeight)
        {
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }

        if (_settings.WindowLeft >= 0 && _settings.WindowTop >= 0)
        {
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 20;
            Top = area.Bottom - Height - 20;
        }
    }

    // ── Event handlers ────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => Hide();

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void History_Click(object sender, RoutedEventArgs e) => OpenHistory();

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_isStopped) return; // Can't pause when stopped
        _session.TogglePause();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => ToggleStop();

    private void ToggleStop()
    {
        if (_isStopped)
            ResumeDay();
        else
            StopDay();
    }

    private void StopDay()
    {
        _isStopped = true;

        // If paused, keep paused. If not paused, pause now.
        if (!_session.IsPaused)
            _session.Pause();

        _session.SaveState();

        // UI updates
        btnStop.Content = "\u25B6";  // ▶
        btnStop.ToolTip = "Reprendre la journ\u00e9e";
        btnPause.IsEnabled = false;
        _trayPauseItem.Visible = false;
        txtPauseIndicator.Visibility = Visibility.Collapsed;
        txtStopIndicator.Visibility = Visibility.Visible;
        _trayStopItem.Text = "\u25B6 Reprendre";

        // Minimize to tray
        Hide();
    }

    private void ResumeDay()
    {
        _isStopped = false;

        // Resume tracking
        if (_session.IsPaused)
            _session.Resume();

        // UI updates
        btnStop.Content = "\u23F9";  // ⏹
        btnStop.ToolTip = "Fin de journ\u00e9e";
        btnPause.IsEnabled = true;
        _trayPauseItem.Visible = true;
        txtStopIndicator.Visibility = Visibility.Collapsed;
        _trayStopItem.Text = "\u23F9 Fin de journ\u00e9e";

        ShowWindow();
    }

    private void Pomodoro_Click(object sender, RoutedEventArgs e) => TogglePomodoro();

    // ── Pomodoro ──────────────────────────────────────────────

    private void TogglePomodoro()
    {
        if (_pomodoroActive)
            StopPomodoro(cancelled: true);
        else
            StartPomodoro();
    }

    private void StartPomodoro()
    {
        _pomodoroActive = true;
        _pomodoroStartTime = DateTime.Now;
        _pomodoroEndTime = DateTime.Now.AddMinutes(_settings.PomodoroMinutes);

        // Enable DND
        if (_settings.PomodoroDndEnabled)
            FocusService.EnableDnd();

        // Visual feedback
        btnPomodoro.Content = "\u23F9";  // ⏹
        btnPomodoro.ToolTip = "Arrêter le Pomodoro";
        _trayPomodoroItem.Text = "\u23F9 Arrêter Pomodoro";

        // Dedicated 1-second timer for countdown
        _pomodoroTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pomodoroTimer.Tick += PomodoroTick;
        _pomodoroTimer.Start();

        UpdatePomodoroDisplay();

        _trayIcon.ShowBalloonTip(
            3000, "Pomodoro",
            $"\uD83C\uDF45 Focus {_settings.PomodoroMinutes} min — Ne Pas Déranger activé",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void StopPomodoro(bool cancelled)
    {
        _pomodoroActive = false;
        _pomodoroTimer?.Stop();
        _pomodoroTimer = null;

        // Restore notifications
        if (_settings.PomodoroDndEnabled)
            FocusService.DisableDnd();

        btnPomodoro.Content = "\uD83C\uDF45";
        btnPomodoro.ToolTip = $"Pomodoro ({_settings.PomodoroMinutes} min)";
        _trayPomodoroItem.Text = "\uD83C\uDF45 Pomodoro";
        pomodoroBar.Visibility = Visibility.Collapsed;
        txtPomodoro.Text = "";

        if (!cancelled)
        {
            _trayIcon.ShowBalloonTip(
                5000, "Pomodoro terminé !",
                "\u2705 Bravo ! Prends une petite pause.",
                System.Windows.Forms.ToolTipIcon.Info);

            // Flash the window
            ShowWindow();
        }
    }

    private void PomodoroTick(object? sender, EventArgs e)
    {
        if (!_pomodoroActive) return;

        var remaining = _pomodoroEndTime - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            StopPomodoro(cancelled: false);
            return;
        }

        UpdatePomodoroDisplay();
    }

    private void UpdatePomodoroDisplay()
    {
        if (!_pomodoroActive) return;

        var remaining = _pomodoroEndTime - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        var totalDuration = (_pomodoroEndTime - _pomodoroStartTime).TotalSeconds;
        var elapsed = totalDuration - remaining.TotalSeconds;
        var progress = totalDuration > 0 ? elapsed / totalDuration : 0;
        if (progress > 1) progress = 1;

        txtPomodoro.Text = $"\uD83C\uDF45 {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
        pomodoroBar.Visibility = Visibility.Visible;
        pomodoroFill.Width = pomodoroBar.ActualWidth * progress;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal && Left > -10000)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
    }
}
