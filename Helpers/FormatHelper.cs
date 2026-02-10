namespace DayloaderClock.Helpers;

/// <summary>
/// Shared formatting utilities for time display.
/// </summary>
public static class FormatHelper
{
    /// <summary>Format a <see cref="TimeSpan"/> as compact text: "1h 30m" or "5m".</summary>
    public static string FormatTime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
        return $"{ts.Minutes}m";
    }

    /// <summary>Format total minutes as "1h 30min" or "45min".</summary>
    public static string FormatDuration(double totalMinutes)
    {
        int mins = (int)Math.Round(totalMinutes);
        int h = mins / 60;
        int m = mins % 60;
        return h > 0 ? $"{h}h {m:D2}min" : $"{m}min";
    }
}
