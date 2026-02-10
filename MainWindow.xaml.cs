using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DayloaderClock.Models;
using DayloaderClock.Services;
using DayloaderClock.Resources;
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

    // ── Blink (last segment heartbeat) ───────────────────────
    private Rectangle? _lastFilledSegment;
    private Rectangle? _lastPomodoroSegment;
    private int _prevBarFilled = -1;
    private int _prevPomodoroFilled = -1;
    private int _prevTrayFilled = -1;
    private DispatcherTimer? _blinkTimer;
    private bool _blinkOn = true;

    // ── Mini mode ───────────────────────────────────────────
    private bool _isMiniMode;
    private double _savedWidth;
    private double _savedHeight;

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

        // Set localized Pomodoro tooltip (after _settings is loaded)
        btnPomodoro.ToolTip = string.Format(Strings.Tooltip_Pomodoro, _settings.PomodoroMinutes);

        InitializeTrayIcon();
        InitializeTimer();

        UpdateDisplay();
        RestoreWindowPosition();

        // Restore pause UI if session was paused on disk
        if (_session.IsPaused)
        {
            btnPause.Content = "\u25B6";
            btnPause.ToolTip = Strings.Tooltip_Resume;
            _trayPauseItem.Text = Strings.Tray_Resume;
        }

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
        menu.Items.Add(Strings.Tray_Show, null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add(Strings.Tray_MiniMode, null, (_, _) => Dispatcher.Invoke(() => { ShowWindow(); ToggleMiniMode(); }));
        _trayStopItem = new System.Windows.Forms.ToolStripMenuItem(Strings.Tray_EndDay, null, (_, _) => Dispatcher.Invoke(ToggleStop));
        menu.Items.Add(_trayStopItem);
        _trayPauseItem = new System.Windows.Forms.ToolStripMenuItem(Strings.Tray_Pause, null, (_, _) => Dispatcher.Invoke(() => _session.TogglePause()));
        menu.Items.Add(_trayPauseItem);
        _trayPomodoroItem = new System.Windows.Forms.ToolStripMenuItem(Strings.Tray_Pomodoro, null, (_, _) => Dispatcher.Invoke(TogglePomodoro));
        menu.Items.Add(_trayPomodoroItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Strings.Tray_ResetDay, null, (_, _) => Dispatcher.Invoke(ResetDay));
        menu.Items.Add(Strings.Tray_History, null, (_, _) => Dispatcher.Invoke(OpenHistory));
        menu.Items.Add(Strings.Tray_Settings, null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Strings.Tray_Quit, null, (_, _) => Dispatcher.Invoke(ExitApp));
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

        // Blink timer for last segment
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            if (_lastFilledSegment != null && !_isStopped && !_session.IsPaused)
                _lastFilledSegment.Opacity = _blinkOn ? 1.0 : 0.6;
            if (_lastPomodoroSegment != null && _pomodoroActive)
                _lastPomodoroSegment.Opacity = _blinkOn ? 1.0 : 0.5;
        };
        _blinkTimer.Start();
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
        txtStartTime.Text = string.Format(Strings.StartTime, _session.LoginTime.ToString("HH:mm"));
        txtElapsed.Text = FormatTime(effectiveWork);

        // Pause indicator
        txtPauseIndicator.Visibility = isPaused ? Visibility.Visible : Visibility.Collapsed;

        if (isOvertime)
        {
            var overtime = _session.GetOvertimeTime();
            txtRemaining.Text = Strings.Finished;
            txtRemaining.Foreground = new SolidColorBrush(ColorOvertime);
            txtOvertime.Text = string.Format(Strings.Overtime, FormatTime(overtime));
            txtOvertime.Visibility = Visibility.Visible;
        }
        else
        {
            txtRemaining.Text = string.Format(Strings.Remaining, FormatTime(remaining));
            txtRemaining.Foreground = new SolidColorBrush(Color.FromRgb(107, 80, 53));
            txtOvertime.Visibility = Visibility.Collapsed;
        }

        // Lunch indicator
        txtLunch.Visibility = isLunch ? Visibility.Visible : Visibility.Collapsed;

        // Progress
        var displayPercent = Math.Min(progress, 100);

        // Bar
        DrawBar(progress);

        // Mini mode overlay
        if (_isMiniMode)
        {
            if (isOvertime)
            {
                var ot = _session.GetOvertimeTime();
                txtMiniPercent.Text = string.Format(Strings.Mini_Done, FormatTime(ot));
            }
            else if (_isStopped)
            {
                txtMiniPercent.Text = string.Format(Strings.Mini_Stop, displayPercent.ToString("0"));
            }
            else if (isPaused)
            {
                txtMiniPercent.Text = string.Format(Strings.Mini_Paused, displayPercent.ToString("0"), FormatTime(remaining));
            }
            else
            {
                txtMiniPercent.Text = string.Format(Strings.Mini_Normal, displayPercent.ToString("0"), FormatTime(remaining));
            }
        }

        // Tray icon
        UpdateTrayIcon(progress, effectiveWork, displayPercent);

        // Taskbar progress bar
        if (TaskbarItemInfo != null)
        {
            TaskbarItemInfo.ProgressValue = displayPercent / 100.0;
            if (_isStopped)
                TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
            else if (isOvertime)
                TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Error;
            else if (isPaused)
                TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Paused;
            else
                TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
        }
    }

    // ── Bar drawing ───────────────────────────────────────────

    private void DrawBar(double progress)
    {
        double canvasWidth = barCanvas.ActualWidth;
        double canvasHeight = barCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        // Dynamically compute segment count to fit the available width
        const double targetSegWidth = 12;
        const double gap = 2;
        int segCount = Math.Max(10, (int)((canvasWidth + gap) / (targetSegWidth + gap)));
        double segWidth = (canvasWidth - gap * (segCount - 1)) / segCount;
        if (segWidth < 2) segWidth = 2;

        int filledSegments = (int)Math.Min(
            Math.Floor(progress / 100.0 * segCount), segCount);
        // Always show at least 1 filled segment when the day has started
        if (filledSegments == 0 && progress > 0 && !_isStopped)
            filledSegments = 1;
        bool isOvertime = progress > 100;

        // Skip full redraw if segment count hasn't changed
        if (filledSegments == _prevBarFilled && !isOvertime && barCanvas.Children.Count > 0)
            return;
        _prevBarFilled = filledSegments;

        barCanvas.Children.Clear();
        _lastFilledSegment = null;

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

                // Track the last filled segment for blink
                if (i == filledSegments - 1)
                    _lastFilledSegment = rect;
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
        _prevBarFilled = -1; // Force redraw on resize
        DrawBar(_session.GetProgressPercent());
    }

    private void UpdateTrayIcon(double progress, TimeSpan effectiveWork, double displayPercent)
    {
        int filled = (int)(Math.Min(progress, 100) / 100.0 * 14);
        bool isOvertime = progress > 100;
        int trayKey = isOvertime ? -1 : filled;

        _trayIcon.Text = $"Dayloader \u2013 {displayPercent:F0}% ({FormatTime(effectiveWork)})";

        // Skip icon redraw if pixel fill hasn't changed
        if (trayKey == _prevTrayFilled) return;
        _prevTrayFilled = trayKey;

        var oldIcon = _trayIcon.Icon;
        _trayIcon.Icon = CreateTrayIcon(progress);

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

            if (win.LanguageChanged)
            {
                // Restart the app to apply the new language
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    // Start new instance with a small delay so the mutex is released
                    var psi = new System.Diagnostics.ProcessStartInfo(exePath)
                    {
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    System.Windows.Application.Current.Shutdown();
                    return;
                }
            }

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
            btnPause.ToolTip = isPaused ? Strings.Tooltip_Resume : Strings.Tooltip_Pause;
            txtPauseIndicator.Visibility = (isPaused && !_isStopped) ? Visibility.Visible : Visibility.Collapsed;
            _trayPauseItem.Text = isPaused ? Strings.Tray_Resume : Strings.Tray_Pause;
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
            Strings.Msg_OvertimeStarted,
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
        btnStop.ToolTip = Strings.Tooltip_ResumeDay;
        btnPause.IsEnabled = false;
        _trayPauseItem.Visible = false;
        txtPauseIndicator.Visibility = Visibility.Collapsed;
        txtStopIndicator.Visibility = Visibility.Visible;
        _trayStopItem.Text = Strings.Tray_Resume;

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
        btnStop.ToolTip = Strings.Tooltip_EndDay;
        btnPause.IsEnabled = true;
        _trayPauseItem.Visible = true;
        txtStopIndicator.Visibility = Visibility.Collapsed;
        _trayStopItem.Text = Strings.Tray_EndDay;

        ShowWindow();
    }

    private void ResetDay()
    {
        var result = MessageBox.Show(Strings.Msg_ResetConfirm,
            "Dayloader Clock", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _isStopped = false;
        _session.ResetDay();

        // Reset cached draw state
        _prevBarFilled = -1;
        _prevTrayFilled = -1;
        _prevPomodoroFilled = -1;

        // Reset UI
        btnStop.Content = "\u23F9";
        btnStop.ToolTip = Strings.Tooltip_EndDay;
        btnPause.IsEnabled = true;
        btnPause.Content = "\u23F8";
        btnPause.ToolTip = Strings.Tooltip_Pause;
        txtPauseIndicator.Visibility = Visibility.Collapsed;
        txtStopIndicator.Visibility = Visibility.Collapsed;
        _trayPauseItem.Text = Strings.Tray_Pause;
        _trayPauseItem.Visible = true;
        _trayStopItem.Text = Strings.Tray_EndDay;
        txtStartTime.Text = string.Format(Strings.StartTime, _session.LoginTime.ToString("HH:mm"));

        UpdateDisplay();
    }

    private void Pomodoro_Click(object sender, RoutedEventArgs e) => TogglePomodoro();

    private void BarCanvas_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMiniMode();
    }

    private void ToggleMiniMode()
    {
        _isMiniMode = !_isMiniMode;

        if (_isMiniMode)
        {
            _savedWidth = Width;
            _savedHeight = Height;

            rowTitle.Visibility = Visibility.Collapsed;
            rowInfo.Visibility = Visibility.Collapsed;
            rowMarkers.Visibility = Visibility.Collapsed;
            rowFooter.Visibility = Visibility.Collapsed;
            mainGrid.Margin = new Thickness(2, 2, 2, 2);
            outerBorder.Margin = new Thickness(1);
            outerBorder.CornerRadius = new CornerRadius(8);
            outerBorder.Effect = null; // Remove shadow in mini mode
            innerBorder.Margin = new Thickness(2);
            innerBorder.CornerRadius = new CornerRadius(6);
            txtMiniPercent.Visibility = Visibility.Visible;

            MinHeight = 0;
            MinWidth = 200;
            Height = double.NaN; // Auto height — let content decide
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            rowTitle.Visibility = Visibility.Visible;
            rowInfo.Visibility = Visibility.Visible;
            rowMarkers.Visibility = Visibility.Visible;
            rowFooter.Visibility = Visibility.Visible;
            mainGrid.Margin = new Thickness(20, 10, 20, 12);
            outerBorder.Margin = new Thickness(6);
            outerBorder.CornerRadius = new CornerRadius(14);
            outerBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4, Opacity = 0.55
            };
            innerBorder.Margin = new Thickness(4);
            innerBorder.CornerRadius = new CornerRadius(11);
            txtMiniPercent.Visibility = Visibility.Collapsed;

            SizeToContent = SizeToContent.Manual;
            MinHeight = 160;
            MinWidth = 435;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = _savedWidth;
            Height = _savedHeight;
        }
    }

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
        btnPomodoro.ToolTip = Strings.Tooltip_StopPomodoro;
        _trayPomodoroItem.Text = Strings.Tray_StopPomodoro;

        // Dedicated 1-second timer for countdown
        _pomodoroTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pomodoroTimer.Tick += PomodoroTick;
        _pomodoroTimer.Start();

        UpdatePomodoroDisplay();

        _trayIcon.ShowBalloonTip(
            3000, "Pomodoro",
            string.Format(Strings.Msg_PomodoroFocus, _settings.PomodoroMinutes),
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
        btnPomodoro.ToolTip = string.Format(Strings.Tooltip_Pomodoro, _settings.PomodoroMinutes);
        _trayPomodoroItem.Text = Strings.Tray_Pomodoro;
        pomodoroBar.Visibility = Visibility.Collapsed;
        txtPomodoro.Text = "";
        _prevPomodoroFilled = -1;
        _lastPomodoroSegment = null;

        if (!cancelled)
        {
            _trayIcon.ShowBalloonTip(
                5000, Strings.Msg_PomodoroCompleted,
                Strings.Msg_PomodoroBreak,
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
        DrawPomodoroBar(progress);
    }

    private void DrawPomodoroBar(double progress)
    {
        double canvasWidth = pomodoroCanvas.ActualWidth;
        double canvasHeight = pomodoroCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        const double targetSegWidth = 9;
        const double gap = 1.5;
        int segCount = Math.Max(8, (int)((canvasWidth + gap) / (targetSegWidth + gap)));
        double segWidth = (canvasWidth - gap * (segCount - 1)) / segCount;
        if (segWidth < 1.5) segWidth = 1.5;

        int filledSegments = (int)Math.Min(
            Math.Floor(progress * segCount), segCount);
        if (filledSegments == 0 && progress > 0)
            filledSegments = 1;

        // Skip full redraw if segment count hasn't changed
        if (filledSegments == _prevPomodoroFilled && pomodoroCanvas.Children.Count > 0)
            return;
        _prevPomodoroFilled = filledSegments;

        pomodoroCanvas.Children.Clear();
        _lastPomodoroSegment = null;

        // Red gradient: from warm orange-red to deep red
        var colorStart = Color.FromRgb(0xE8, 0x70, 0x30);
        var colorEnd = Color.FromRgb(0xCC, 0x30, 0x30);
        var colorEmpty = Color.FromRgb(0x30, 0x28, 0x1A);

        for (int i = 0; i < segCount; i++)
        {
            var rect = new Rectangle
            {
                Width = segWidth,
                Height = canvasHeight,
                RadiusX = 1.5,
                RadiusY = 1.5
            };

            if (i < filledSegments)
            {
                double t = segCount > 1 ? (double)i / (segCount - 1) : 0;
                var c = InterpolateColor(colorStart, colorEnd, t);
                rect.Fill = new SolidColorBrush(c);

                if (i == filledSegments - 1)
                    _lastPomodoroSegment = rect;
            }
            else
            {
                rect.Fill = new SolidColorBrush(colorEmpty);
            }

            Canvas.SetLeft(rect, i * (segWidth + gap));
            Canvas.SetTop(rect, 0);
            pomodoroCanvas.Children.Add(rect);
        }
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
