using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ClosedXML.Excel;
using DayloaderClock.Helpers;
using DayloaderClock.Models;
using DayloaderClock.Services;
using DayloaderClock.Resources;
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
    private static readonly SolidColorBrush BrushEmpty = new(Color.FromRgb(58, 46, 30));
    private static readonly SolidColorBrush BrushToday = new(Color.FromRgb(79, 172, 254));
    private static readonly SolidColorBrush BrushDayText = new(Color.FromRgb(74, 53, 32));
    private static readonly SolidColorBrush BrushWeekend = new(Color.FromRgb(139, 115, 85));
    private static readonly SolidColorBrush BrushHoursText = new(Color.FromRgb(232, 213, 184));

    private readonly SessionService _session;

    public HistoryWindow(SessionService session)
    {
        InitializeComponent();

        _session = session;
        _store = StorageService.Instance.LoadSessions();
        _sessionsByDate = new Dictionary<string, DaySession>();

        foreach (var s in _store.History)
            _sessionsByDate[s.Date] = s;

        // For today's session, use live values from the shared SessionService
        if (_store.CurrentSession != null)
        {
            var today = _store.CurrentSession;
            if (today.Date == DateTime.Today.ToString("yyyy-MM-dd"))
            {
                today.TotalEffectiveWorkMinutes = _session.GetEffectiveWorkTime().TotalMinutes;
                today.TotalLunchMinutes = _session.GetLunchDeduction().TotalMinutes;
            }
            _sessionsByDate[today.Date] = today;
        }

        _displayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1, 0, 0, 0, DateTimeKind.Local);
        BuildCalendar();
    }

    // ── Calendar building ─────────────────────────────────────

    private void BuildCalendar()
    {
        calendarGrid.Children.Clear();

        txtMonth.Text = $"{CultureInfo.CurrentUICulture.DateTimeFormat.GetMonthName(_displayedMonth.Month)} {_displayedMonth.Year}";

        int startOffset = ((int)_displayedMonth.DayOfWeek + 6) % 7; // Monday=0
        int daysInMonth = DateTime.DaysInMonth(_displayedMonth.Year, _displayedMonth.Month);
        var today = DateTime.Today;

        int activeDays = 0;
        double totalMinutes = 0;

        for (int slot = 0; slot < 42; slot++) // 6 rows × 7 cols
        {
            int dayNum = slot - startOffset + 1;
            bool isCurrentMonth = dayNum >= 1 && dayNum <= daysInMonth;

            if (isCurrentMonth)
            {
                var (cell, minutes) = BuildDayCell(dayNum, today);
                if (minutes > 0) { activeDays++; totalMinutes += minutes; }
                calendarGrid.Children.Add(cell);
            }
            else
            {
                calendarGrid.Children.Add(BuildOutOfMonthCell());
            }
        }

        UpdateMonthlySummary(activeDays, totalMinutes);
    }

    private (Border Cell, double Minutes) BuildDayCell(int dayNum, DateTime today)
    {
        var date = new DateTime(_displayedMonth.Year, _displayedMonth.Month, dayNum, 0, 0, 0, DateTimeKind.Local);
        var dateStr = date.ToString("yyyy-MM-dd");
        bool isToday = date == today;
        bool isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        bool hasSession = _sessionsByDate.TryGetValue(dateStr, out var session);

        var cell = new Border { CornerRadius = new CornerRadius(5), Margin = new Thickness(2) };
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        stack.Children.Add(new TextBlock
        {
            Text = dayNum.ToString(),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Foreground = GetDayTextBrush(isToday, isWeekend)
        });

        double minutes = 0;

        if (hasSession && session != null)
        {
            minutes = session.TotalEffectiveWorkMinutes;
            ApplySessionVisuals(cell, stack, session, date);
        }
        else
        {
            cell.Background = isWeekend
                ? new SolidColorBrush(Color.FromRgb(45, 36, 22))
                : BrushEmpty;
        }

        if (isToday)
        {
            cell.BorderBrush = BrushToday;
            cell.BorderThickness = new Thickness(2);
        }

        cell.Child = stack;
        return (cell, minutes);
    }

    private static void ApplySessionVisuals(Border cell, StackPanel stack, DaySession session, DateTime date)
    {
        double hours = session.EffectiveHours;

        stack.Children.Add(new TextBlock
        {
            Text = FormatHelper.FormatDuration(session.TotalEffectiveWorkMinutes),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Foreground = BrushHoursText
        });

        cell.Background = new SolidColorBrush(GetDayCellColor(hours));

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

        cell.ToolTip = string.Format(Strings.History_Tooltip,
            date.ToString("dd/MM/yyyy"),
            session.GetLoginFormatted(),
            session.GetLastActivityFormatted(),
            FormatHelper.FormatDuration(session.TotalEffectiveWorkMinutes),
            FormatHelper.FormatDuration(session.TotalLunchMinutes));
    }

    private static Border BuildOutOfMonthCell()
    {
        return new Border
        {
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(2),
            Background = new SolidColorBrush(Color.FromRgb(35, 28, 17)),
            Opacity = 0.4,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            }
        };
    }

    private void UpdateMonthlySummary(int activeDays, double totalMinutes)
    {
        txtDaysCount.Text = activeDays.ToString();
        txtTotalHours.Text = FormatHelper.FormatDuration(totalMinutes);
        txtAvgHours.Text = activeDays > 0 ? FormatHelper.FormatDuration(totalMinutes / activeDays) : "—";
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

    private static Color Lerp(Color a, Color b, double t) => ColorHelper.Lerp(a, b, t);

    private static SolidColorBrush GetDayTextBrush(bool isToday, bool isWeekend)
    {
        if (isToday) return BrushToday;
        if (isWeekend) return BrushWeekend;
        return BrushDayText;
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
            Title = Strings.History_CsvDialogTitle
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var sessions = GetAllSortedSessions();
            var sb = new StringBuilder();
            sb.AppendLine($"{Strings.Export_Date};{Strings.Export_Login};{Strings.Export_LastActivity};{Strings.Export_MinutesWorked};{Strings.Export_HoursWorked};{Strings.Export_LunchMinutes};{Strings.Export_DayComplete}");

            foreach (var s in sessions)
            {
                var login = s.GetLoginFormatted("");
                var last = s.GetLastActivityFormatted("");
                double hours = s.EffectiveHours;
                sb.AppendLine($"{s.Date};{login};{last};{s.TotalEffectiveWorkMinutes:F0};{hours:F2};{s.TotalLunchMinutes:F0};{(s.DayCompleted ? Strings.History_Yes : Strings.History_No)}");
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            System.Windows.MessageBox.Show(string.Format(Strings.History_ExportSuccess, "CSV", dlg.FileName),
                Strings.History_ExportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(string.Format(Strings.History_ExportError, ex.Message),
                Strings.History_ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"dayloader_{DateTime.Today:yyyy-MM-dd}.xlsx",
            Title = Strings.History_XlsxDialogTitle
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var sessions = GetAllSortedSessions();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(Strings.Export_SheetName);

            // Header
            var headers = new[] { Strings.Export_Date, Strings.Export_Login, Strings.Export_LastActivity, Strings.Export_MinutesWorked, Strings.Export_HoursWorked, Strings.Export_LunchMinutes, Strings.Export_DayComplete };
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
                var login = s.GetLoginFormatted("");
                var last = s.GetLastActivityFormatted("");
                double hours = s.EffectiveHours;

                ws.Cell(row, 1).Value = s.Date;
                ws.Cell(row, 2).Value = login;
                ws.Cell(row, 3).Value = last;
                ws.Cell(row, 4).Value = Math.Round(s.TotalEffectiveWorkMinutes);
                ws.Cell(row, 5).Value = Math.Round(hours, 2);
                ws.Cell(row, 6).Value = Math.Round(s.TotalLunchMinutes);
                ws.Cell(row, 7).Value = s.DayCompleted ? Strings.History_Yes : Strings.History_No;

                // Color row by hours
                if (hours >= 8)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#D5F5E3");
                else if (hours >= 4)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF9E7");
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(dlg.FileName);

            System.Windows.MessageBox.Show(string.Format(Strings.History_ExportSuccess, "Excel", dlg.FileName),
                Strings.History_ExportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(string.Format(Strings.History_ExportError, ex.Message),
                Strings.History_ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
