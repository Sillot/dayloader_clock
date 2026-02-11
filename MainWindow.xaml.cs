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
    private System.Windows.Forms.ToolStripMenuItem _trayStopItem = null!;
    private ContextMenu? _windowContextMenu;
    private bool _isExiting;
    private bool _isStopped;

    // ── Blink (last segment heartbeat) ───────────────────────
    private Rectangle? _lastFilledSegment;
    private int _prevBarFilled = -1;
    private int _prevTrayFilled = -1;
    private DispatcherTimer? _blinkTimer;
    private bool _blinkOn = true;

    // ── Mini mode ───────────────────────────────────────────
    private bool _isMiniMode;
    private double _savedWidth;
    private double _savedHeight;
    private double _miniModeLockedHeight = -1;

    // Colors are centralized in ColorHelper

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public MainWindow()
    {
        InitializeComponent();

        // Version
        txtTitle.ToolTip = $"Dayloader Clock {AppVersion.Display}";

        _settings = StorageService.Instance.LoadSettings();
        _session = new SessionService(_settings);
        _session.OvertimeStarted += OnOvertimeStarted;
        _session.LunchBreakStarted += OnLunchBreakStarted;
        _session.PauseStateChanged += OnPauseStateChanged;

        InitializeTrayIcon();
        InitializeWindowContextMenu();
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
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Strings.Tray_ResetDay, null, (_, _) => Dispatcher.Invoke(ResetDay));
        menu.Items.Add(Strings.Tray_History, null, (_, _) => Dispatcher.Invoke(OpenHistory));
        menu.Items.Add(Strings.Tray_Settings, null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var versionItem = new System.Windows.Forms.ToolStripMenuItem($"Dayloader Clock {AppVersion.Display}") { Enabled = false };
        menu.Items.Add(versionItem);
        menu.Items.Add(Strings.Tray_Quit, null, (_, _) => Dispatcher.Invoke(ExitApp));
        _trayIcon.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Build a WPF ContextMenu used in mini mode (mirrors the tray menu).
    /// </summary>
    private void InitializeWindowContextMenu()
    {
        _windowContextMenu = new ContextMenu();
        RebuildWindowContextMenu();
    }

    private void RebuildWindowContextMenu()
    {
        if (_windowContextMenu == null) return;
        _windowContextMenu.Items.Clear();

        _windowContextMenu.Items.Add(new MenuItem { Header = Strings.Tray_Show, Command = new RelayCommand(ShowWindow) });
        _windowContextMenu.Items.Add(new MenuItem { Header = Strings.Tray_MiniMode, Command = new RelayCommand(ToggleMiniMode) });
        _windowContextMenu.Items.Add(new Separator());

        var stopItem = new MenuItem
        {
            Header = _isStopped ? Strings.Tray_Resume : Strings.Tray_EndDay,
            Command = new RelayCommand(ToggleStop)
        };
        _windowContextMenu.Items.Add(stopItem);

        if (!_isStopped)
        {
            var pauseItem = new MenuItem
            {
                Header = _session.IsPaused ? Strings.Tray_Resume : Strings.Tray_Pause,
                Command = new RelayCommand(() => _session.TogglePause())
            };
            _windowContextMenu.Items.Add(pauseItem);
        }

        _windowContextMenu.Items.Add(new Separator());
        _windowContextMenu.Items.Add(new MenuItem { Header = Strings.Tray_ResetDay, Command = new RelayCommand(ResetDay) });
        _windowContextMenu.Items.Add(new MenuItem { Header = Strings.Tray_History, Command = new RelayCommand(OpenHistory) });
        _windowContextMenu.Items.Add(new MenuItem { Header = Strings.Tray_Settings, Command = new RelayCommand(OpenSettings) });
        _windowContextMenu.Items.Add(new Separator());
        _windowContextMenu.Items.Add(new MenuItem { Header = $"Dayloader Clock {AppVersion.Display}", IsEnabled = false });
        _windowContextMenu.Items.Add(new MenuItem { Header = Strings.Tray_Quit, Command = new RelayCommand(ExitApp) });
    }

    /// <summary>Minimal ICommand for context menu items.</summary>
    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
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
            _session.CheckAndNotifyLunch();
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
        txtLunch.Visibility = (isLunch && !_isMiniMode) ? Visibility.Visible : Visibility.Collapsed;

        UpdateOvertimeDisplay(isOvertime, remaining);

        var displayPercent = Math.Min(progress, 100);
        DrawBar(progress);
        UpdateMiniModeText(isOvertime, isPaused, isLunch, remaining, displayPercent);
        if (_isMiniMode) RebuildWindowContextMenu();
        UpdateTrayIcon(progress, effectiveWork, displayPercent, isLunch);
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

    private void UpdateMiniModeText(bool isOvertime, bool isPaused, bool isLunch, TimeSpan remaining, double displayPercent)
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
        else if (isLunch)
        {
            txtMiniPercent.Text = string.Format(Strings.Mini_Lunch, displayPercent.ToString("0"));
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

    private void UpdateTrayIcon(double progress, TimeSpan effectiveWork, double displayPercent, bool isLunch)
    {
        int filled = (int)(Math.Min(progress, 100) / 100.0 * 14);
        bool isOvertime = progress > 100;
        int trayKey = (isOvertime ? -1 : filled) + (isLunch ? 1000 : 0);

        _trayIcon.Text = $"Dayloader \u2013 {displayPercent:F0}% ({FormatTime(effectiveWork)})";

        // Skip icon redraw if pixel fill hasn't changed
        if (trayKey == _prevTrayFilled) return;
        _prevTrayFilled = trayKey;

        var oldIcon = _trayIcon.Icon;
        _trayIcon.Icon = CreateTrayIcon(progress, isLunch);

        if (oldIcon != null)
        {
            DestroyIcon(oldIcon.Handle);
            oldIcon.Dispose();
        }
    }

    // Color helpers and FormatTime are in Helpers/ColorHelper.cs and Helpers/FormatHelper.cs
    private static string FormatTime(TimeSpan ts) => FormatHelper.FormatTime(ts);

    // ── Tray icon drawing ─────────────────────────────────────

    private static System.Drawing.Icon CreateTrayIcon(double progress, bool isLunch = false)
    {
        using var bitmap = new System.Drawing.Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(System.Drawing.Color.FromArgb(44, 36, 25));

            // Yellow border when lunch break
            if (isLunch)
            {
                using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 215, 0), 1);
                g.DrawRectangle(pen, 0, 0, 15, 15);
            }

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

    private void OnLunchBreakStarted()
    {
        _trayIcon.ShowBalloonTip(
            5000,
            "Dayloader Clock",
            Strings.Msg_LunchBreak,
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void RestoreWindowPosition()
    {
        if (_settings.WindowWidth >= MinWidth && _settings.WindowHeight >= MinHeight)
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

    private void MiniMode_Click(object sender, RoutedEventArgs e) => ToggleMiniMode();

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
            rowActions.Visibility = Visibility.Collapsed;
            rowFooter.Visibility = Visibility.Collapsed;
            mainGrid.Margin = new Thickness(2, 2, 2, 2);
            outerBorder.Margin = new Thickness(1);
            outerBorder.CornerRadius = new CornerRadius(8);
            outerBorder.Effect = null; // Remove shadow in mini mode
            innerBorder.Margin = new Thickness(2);
            innerBorder.CornerRadius = new CornerRadius(6);
            innerBorder.ContextMenu = _windowContextMenu;
            txtMiniPercent.Visibility = Visibility.Visible;

            MinHeight = 0;
            MinWidth = 100;
            Height = double.NaN; // Auto height — let content decide
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            _miniModeLockedHeight = -1; // will be set after layout

            // Restore saved mini mode width
            if (_settings.MiniModeWidth >= MinWidth)
                Width = _settings.MiniModeWidth;

            Dispatcher.InvokeAsync(() =>
            {
                _miniModeLockedHeight = ActualHeight;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            rowTitle.Visibility = Visibility.Visible;
            rowInfo.Visibility = Visibility.Visible;
            rowMarkers.Visibility = Visibility.Visible;
            rowActions.Visibility = Visibility.Visible;
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
            innerBorder.ContextMenu = null;
            txtMiniPercent.Visibility = Visibility.Collapsed;

            SizeToContent = SizeToContent.Manual;
            MinHeight = 160;
            MinWidth = 340;
            ResizeMode = ResizeMode.NoResize;
            _miniModeLockedHeight = -1;
            Width = _savedWidth;
            Height = _savedHeight;
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
            // In mini mode, lock the height to prevent vertical resizing
            if (_isMiniMode && _miniModeLockedHeight > 0 && Math.Abs(ActualHeight - _miniModeLockedHeight) > 1)
            {
                Height = _miniModeLockedHeight;
                return;
            }

            if (_isMiniMode)
            {
                _settings.MiniModeWidth = Width;
            }
            else
            {
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
            }
        }
    }
}
