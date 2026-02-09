using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ClosedXML.Excel;
using DayloaderClock.Models;
using DayloaderClock.Services;
using Microsoft.Win32;
using FontFamily = System.Windows.Media.FontFamily;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace DayloaderClock;

public partial class HistoryWindow : Window
{
    private DateTime _displayedMonth;
    private readonly SessionStore _store;
    private readonly Dictionary<string, DaySession> _sessionsByDate;

    // ── Colors ────────────────────────────────────────────────
    private static readonly SolidColorBrush BrushEmpty = new(Color.FromRgb(58, 46, 30));       // #3A2E1E
    private static readonly SolidColorBrush BrushToday = new(Color.FromRgb(79, 172, 254));      // blue accent
    private static readonly SolidColorBrush BrushDayText = new(Color.FromRgb(74, 53, 32));      // #4A3520
    private static readonly SolidColorBrush BrushWeekend = new(Color.FromRgb(139, 115, 85));    // #8B7355
    private static readonly SolidColorBrush BrushOutside = new(Color.FromRgb(107, 80, 53));     // #6B5035
    private static readonly SolidColorBrush BrushHoursText = new(Color.FromRgb(232, 213, 184)); // #E8D5B8

    private static readonly string[] FrenchMonths =
    [
        "Janvier", "Février", "Mars", "Avril", "Mai", "Juin",
        "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"
    ];

    /// <summary>Format minutes as "Xh YYmin" (e.g. 1h 30min, 45min, 8h 00min)</summary>
    private static string FormatDuration(double totalMinutes)
    {
        int mins = (int)Math.Round(totalMinutes);
        int h = mins / 60;
        int m = mins % 60;
        return h > 0 ? $"{h}h {m:D2}min" : $"{m}min";
    }

    public HistoryWindow()
    {
        InitializeComponent();

        _store = StorageService.LoadSessions();
        _sessionsByDate = new Dictionary<string, DaySession>();

        foreach (var session in _store.History)
            _sessionsByDate[session.Date] = session;

        // For today's session, compute live effective work time
        if (_store.CurrentSession != null)
        {
            var today = _store.CurrentSession;
            // Recalculate from timestamps if it's today
            if (today.Date == DateTime.Today.ToString("yyyy-MM-dd"))
            {
                var settings = StorageService.LoadSettings();
                var now = DateTime.Now;
                if (DateTime.TryParse(today.FirstLoginTime, out var loginTime))
                {
                    var elapsed = now - loginTime;
                    if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

                    // Subtract lunch overlap
                    var lunchStart = DateTime.Today.Add(settings.GetLunchStart());
                    var lunchEnd = DateTime.Today.Add(settings.GetLunchEnd());
                    var oStart = loginTime > lunchStart ? loginTime : lunchStart;
                    var oEnd = now < lunchEnd ? now : lunchEnd;
                    if (oStart < oEnd) elapsed -= (oEnd - oStart);

                    if (elapsed > TimeSpan.Zero)
                        today.TotalEffectiveWorkMinutes = elapsed.TotalMinutes;
                }
            }
            _sessionsByDate[today.Date] = today;
        }

        _displayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BuildCalendar();
    }

    // ── Calendar building ─────────────────────────────────────

    private void BuildCalendar()
    {
        calendarGrid.Children.Clear();

        txtMonth.Text = $"{FrenchMonths[_displayedMonth.Month - 1]} {_displayedMonth.Year}";

        // Day of week the month starts on (Monday=0 ... Sunday=6)
        var firstDay = _displayedMonth;
        int startOffset = ((int)firstDay.DayOfWeek + 6) % 7; // shift so Monday=0
        int daysInMonth = DateTime.DaysInMonth(_displayedMonth.Year, _displayedMonth.Month);

        var today = DateTime.Today;

        // Monthly stats
        int activeDays = 0;
        double totalMinutes = 0;

        for (int slot = 0; slot < 42; slot++) // 6 rows × 7 cols
        {
            int dayNum = slot - startOffset + 1;
            bool isCurrentMonth = dayNum >= 1 && dayNum <= daysInMonth;

            var cell = new Border
            {
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(2)
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            if (isCurrentMonth)
            {
                var date = new DateTime(_displayedMonth.Year, _displayedMonth.Month, dayNum);
                var dateStr = date.ToString("yyyy-MM-dd");
                bool isToday = date == today;
                bool isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
                bool hasSession = _sessionsByDate.TryGetValue(dateStr, out var session);

                // Day number
                var dayText = new TextBlock
                {
                    Text = dayNum.ToString(),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = isToday ? BrushToday
                               : isWeekend ? BrushWeekend
                               : BrushDayText
                };

                stack.Children.Add(dayText);

                if (hasSession && session != null)
                {
                    double hours = session.TotalEffectiveWorkMinutes / 60.0;
                    activeDays++;
                    totalMinutes += session.TotalEffectiveWorkMinutes;

                    // Hours label
                    var hoursText = new TextBlock
                    {
                        Text = FormatDuration(session.TotalEffectiveWorkMinutes),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 9,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Foreground = BrushHoursText
                    };
                    stack.Children.Add(hoursText);

                    // Color based on hours worked
                    cell.Background = new SolidColorBrush(GetDayCellColor(hours));

                    // Glow on full days
                    if (hours >= 8)
                    {
                        cell.Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(67, 233, 123),
                            BlurRadius = 8,
                            ShadowDepth = 0,
                            Opacity = 0.4
                        };
                    }

                    // Tooltip
                    var login = DateTime.TryParse(session.FirstLoginTime, out var loginDt)
                        ? loginDt.ToString("HH:mm") : "?";
                    var lastActivity = DateTime.TryParse(session.LastActivityTime, out var lastDt)
                        ? lastDt.ToString("HH:mm") : "?";

                    cell.ToolTip = $"{date:dd/MM/yyyy}\n" +
                                   $"Login: {login}\n" +
                                   $"Dernière activité: {lastActivity}\n" +
                                   $"Travail effectif: {FormatDuration(session.TotalEffectiveWorkMinutes)}";
                }
                else
                {
                    cell.Background = isWeekend
                        ? new SolidColorBrush(Color.FromRgb(45, 36, 22))
                        : BrushEmpty;
                }

                // Today border
                if (isToday)
                {
                    cell.BorderBrush = BrushToday;
                    cell.BorderThickness = new Thickness(2);
                }
            }
            else
            {
                // Outside current month
                cell.Background = new SolidColorBrush(Color.FromRgb(35, 28, 17));
                cell.Opacity = 0.4;
            }

            cell.Child = stack;
            calendarGrid.Children.Add(cell);
        }

        // Update summary
        txtDaysCount.Text = activeDays.ToString();
        txtTotalHours.Text = FormatDuration(totalMinutes);
        txtAvgHours.Text = activeDays > 0 ? FormatDuration(totalMinutes / activeDays) : "\u2014";
    }

    /// <summary>
    /// Cell color: dark → green gradient based on hours worked.
    /// 0h = dark brown, 4h = amber, 8h+ = green.
    /// </summary>
    private static Color GetDayCellColor(double hours)
    {
        double t = Math.Clamp(hours / 8.0, 0, 1);

        if (t < 0.5)
        {
            double lt = t * 2;
            return Lerp(Color.FromRgb(74, 53, 32), Color.FromRgb(180, 140, 50), lt);
        }
        else
        {
            double lt = (t - 0.5) * 2;
            return Lerp(Color.FromRgb(180, 140, 50), Color.FromRgb(56, 168, 80), lt);
        }
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    // ── Event handlers ────────────────────────────────────────

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayedMonth = _displayedMonth.AddMonths(-1);
        BuildCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _displayedMonth = _displayedMonth.AddMonths(1);
        BuildCalendar();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Export ─────────────────────────────────────────────────

    private List<DaySession> GetAllSortedSessions()
    {
        return _sessionsByDate.Values
            .OrderBy(s => s.Date)
            .ToList();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"dayloader_{DateTime.Today:yyyy-MM-dd}.csv",
            Title = "Exporter l'historique en CSV"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var sessions = GetAllSortedSessions();
            var sb = new StringBuilder();
            sb.AppendLine("Date;Login;Dernière activité;Minutes travaillées;Heures travaillées;Journée complète");

            foreach (var s in sessions)
            {
                var login = DateTime.TryParse(s.FirstLoginTime, out var lt) ? lt.ToString("HH:mm") : "";
                var last = DateTime.TryParse(s.LastActivityTime, out var at) ? at.ToString("HH:mm") : "";
                double hours = s.TotalEffectiveWorkMinutes / 60.0;
                sb.AppendLine($"{s.Date};{login};{last};{s.TotalEffectiveWorkMinutes:F0};{hours:F2};{(s.DayCompleted ? "Oui" : "Non")}");
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            System.Windows.MessageBox.Show($"Export CSV réussi !\n{dlg.FileName}",
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Erreur lors de l'export :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"dayloader_{DateTime.Today:yyyy-MM-dd}.xlsx",
            Title = "Exporter l'historique en Excel"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var sessions = GetAllSortedSessions();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Historique");

            // Header
            var headers = new[] { "Date", "Login", "Dernière activité", "Minutes", "Heures", "Complète" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#3A2E1E");
                cell.Style.Font.FontColor = XLColor.FromHtml("#E8D5B8");
            }

            // Data
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                int row = i + 2;
                var login = DateTime.TryParse(s.FirstLoginTime, out var lt) ? lt.ToString("HH:mm") : "";
                var last = DateTime.TryParse(s.LastActivityTime, out var at) ? at.ToString("HH:mm") : "";
                double hours = s.TotalEffectiveWorkMinutes / 60.0;

                ws.Cell(row, 1).Value = s.Date;
                ws.Cell(row, 2).Value = login;
                ws.Cell(row, 3).Value = last;
                ws.Cell(row, 4).Value = Math.Round(s.TotalEffectiveWorkMinutes);
                ws.Cell(row, 5).Value = Math.Round(hours, 2);
                ws.Cell(row, 6).Value = s.DayCompleted ? "Oui" : "Non";

                // Color row by hours
                if (hours >= 8)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#D5F5E3");
                else if (hours >= 4)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF9E7");
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(dlg.FileName);

            System.Windows.MessageBox.Show($"Export Excel réussi !\n{dlg.FileName}",
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Erreur lors de l'export :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
