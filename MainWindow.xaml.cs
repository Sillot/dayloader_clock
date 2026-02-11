using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DayloaderClock.Helpers;
using DayloaderClock.Models;
using DayloaderClock.Services;
using DayloaderClock.Resources;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace DayloaderClock;

// CA1001: disposable fields are cleaned up in ExitApp() — WPF Window should not implement IDisposable
#pragma warning disable CA1001
public partial class MainWindow : Window
#pragma warning restore CA1001
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

    // Colors are centralized in ColorHelper

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public MainWindow()
    {
        InitializeComponent();

        // Version
        txtTitle.ToolTip = $"Dayloader Clock {AppVersion.Display}";
        txtVersion.Text = AppVersion.Display;

        _settings = StorageService.Instance.LoadSettings();
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
            if (_session.CheckNewDay())
                ResetStoppedState();
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
        txtPauseIndicator.Visibility = isPaused ? Visibility.Visible : Visibility.Collapsed;
        txtLunch.Visibility = isLunch ? Visibility.Visible : Visibility.Collapsed;

        UpdateOvertimeDisplay(isOvertime, remaining);

        var displayPercent = Math.Min(progress, 100);
        DrawBar(progress);
        UpdateMiniModeText(isOvertime, isPaused, remaining, displayPercent);
        UpdateTrayIcon(progress, effectiveWork, displayPercent);
        UpdateTaskbarProgress(displayPercent, isOvertime, isPaused);
    }

    private void UpdateOvertimeDisplay(bool isOvertime, TimeSpan remaining)
    {
        if (isOvertime)
        {
            var overtime = _session.GetOvertimeTime();
            txtRemaining.Text = Strings.Finished;
            txtRemaining.Foreground = new SolidColorBrush(ColorHelper.Overtime);
            txtOvertime.Text = string.Format(Strings.Overtime, FormatTime(overtime));
            txtOvertime.Visibility = Visibility.Visible;
        }
        else
        {
            txtRemaining.Text = string.Format(Strings.Remaining, FormatTime(remaining));
            txtRemaining.Foreground = new SolidColorBrush(Color.FromRgb(107, 80, 53));
            txtOvertime.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateMiniModeText(bool isOvertime, bool isPaused, TimeSpan remaining, double displayPercent)
    {
        if (!_isMiniMode) return;

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

    private void UpdateTaskbarProgress(double displayPercent, bool isOvertime, bool isPaused)
    {
        if (TaskbarItemInfo == null) return;

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

    // ── Bar drawing ───────────────────────────────────────────

    private static (int SegCount, double SegWidth) CalculateSegmentLayout(double canvasWidth, double targetSegWidth, double gap)
    {
        int segCount = Math.Max(10, (int)((canvasWidth + gap) / (targetSegWidth + gap)));
        double segWidth = (canvasWidth - gap * (segCount - 1)) / segCount;
        if (segWidth < 2) segWidth = 2;
        return (segCount, segWidth);
    }

    private static Rectangle CreateBarSegment(int index, int filledSegments, int segCount,
        double segWidth, double canvasHeight, bool isOvertime)
    {
        var rect = new Rectangle
        {
            Width = segWidth,
            Height = canvasHeight,
            RadiusX = 2,
            RadiusY = 2
        };

        if (index < filledSegments)
        {
            var color = ColorHelper.GetBarGradient(index, segCount);
            if (isOvertime)
                color = ColorHelper.Lerp(color, ColorHelper.Overtime, 0.4);
            rect.Fill = new SolidColorBrush(color);
        }
        else
        {
            rect.Fill = new SolidColorBrush(ColorHelper.Empty);
        }

        return rect;
    }

    private void DrawBar(double progress)
    {
        double canvasWidth = barCanvas.ActualWidth;
        double canvasHeight = barCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        const double gap = 2;
        var (segCount, segWidth) = CalculateSegmentLayout(canvasWidth, targetSegWidth: 12, gap);

        int filledSegments = (int)Math.Min(
            Math.Floor(progress / 100.0 * segCount), segCount);
        if (filledSegments == 0 && progress > 0 && !_isStopped)
            filledSegments = 1;
        bool isOvertime = progress > 100;

        if (filledSegments == _prevBarFilled && !isOvertime && barCanvas.Children.Count > 0)
            return;
        _prevBarFilled = filledSegments;

        barCanvas.Children.Clear();
        _lastFilledSegment = null;

        for (int i = 0; i < segCount; i++)
        {
            var rect = CreateBarSegment(i, filledSegments, segCount, segWidth, canvasHeight, isOvertime);

            if (i < filledSegments && i == filledSegments - 1)
                _lastFilledSegment = rect;

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

    // Color helpers and FormatTime are in Helpers/ColorHelper.cs and Helpers/FormatHelper.cs
    private static string FormatTime(TimeSpan ts) => FormatHelper.FormatTime(ts);

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
                System.Drawing.Color c = isOvertime
                    ? System.Drawing.Color.FromArgb(204, 51, 51)
                    : ColorHelper.GetBarGradientDrawing(t);

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
            StorageService.Instance.SaveSettings(_settings);

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
        var win = new HistoryWindow(_session) { Owner = this };
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

    /// <summary>
    /// Reset the stopped state and UI when a new day is detected.
    /// Unlike ResumeDay(), this does not call session.Resume() since
    /// the session has already been reset by CheckNewDay().
    /// </summary>
    private void ResetStoppedState()
    {
        _isStopped = false;
        _prevBarFilled = -1;
        _prevTrayFilled = -1;
        _prevPomodoroFilled = -1;

        btnStop.Content = "\u23F9";  // ⏹
        btnStop.ToolTip = Strings.Tooltip_EndDay;
        btnPause.IsEnabled = true;
        btnPause.Content = "\u23F8";  // ⏸
        btnPause.ToolTip = Strings.Tooltip_Pause;
        txtPauseIndicator.Visibility = Visibility.Collapsed;
        txtStopIndicator.Visibility = Visibility.Collapsed;
        _trayPauseItem.Text = Strings.Tray_Pause;
        _trayPauseItem.Visible = true;
        _trayStopItem.Text = Strings.Tray_EndDay;
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

    private static readonly Color PomodoroColorStart = Color.FromRgb(0xE8, 0x70, 0x30);
    private static readonly Color PomodoroColorEnd = Color.FromRgb(0xCC, 0x30, 0x30);
    private static readonly Color PomodoroColorEmpty = Color.FromRgb(0x30, 0x28, 0x1A);

    private static Rectangle CreatePomodoroSegment(int index, int filledSegments, int segCount,
        double segWidth, double canvasHeight)
    {
        var rect = new Rectangle
        {
            Width = segWidth,
            Height = canvasHeight,
            RadiusX = 1.5,
            RadiusY = 1.5
        };

        if (index < filledSegments)
        {
            double t = segCount > 1 ? (double)index / (segCount - 1) : 0;
            rect.Fill = new SolidColorBrush(ColorHelper.Lerp(PomodoroColorStart, PomodoroColorEnd, t));
        }
        else
        {
            rect.Fill = new SolidColorBrush(PomodoroColorEmpty);
        }

        return rect;
    }

    private void DrawPomodoroBar(double progress)
    {
        double canvasWidth = pomodoroCanvas.ActualWidth;
        double canvasHeight = pomodoroCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        const double gap = 1.5;
        var (segCount, segWidth) = CalculateSegmentLayout(canvasWidth, targetSegWidth: 9, gap);
        if (segWidth < 1.5) segWidth = 1.5;

        int filledSegments = (int)Math.Min(
            Math.Floor(progress * segCount), segCount);
        if (filledSegments == 0 && progress > 0)
            filledSegments = 1;

        if (filledSegments == _prevPomodoroFilled && pomodoroCanvas.Children.Count > 0)
            return;
        _prevPomodoroFilled = filledSegments;

        pomodoroCanvas.Children.Clear();
        _lastPomodoroSegment = null;

        for (int i = 0; i < segCount; i++)
        {
            var rect = CreatePomodoroSegment(i, filledSegments, segCount, segWidth, canvasHeight);

            if (i < filledSegments && i == filledSegments - 1)
                _lastPomodoroSegment = rect;

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
