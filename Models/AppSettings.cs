using System.Globalization;

namespace DayloaderClock.Models;

/// <summary>
/// Application settings, persisted as JSON in %APPDATA%/DayloaderClock/settings.json
/// </summary>
public class AppSettings
{
    /// <summary>Work day duration in minutes (default 480 = 8h)</summary>
    public int WorkDayMinutes { get; set; } = 480;

    /// <summary>Lunch break start time in HH:mm format</summary>
    public string LunchStartTime { get; set; } = "12:00";

    /// <summary>Lunch break duration in minutes</summary>
    public int LunchDurationMinutes { get; set; } = 60;

    /// <summary>Whether the app starts automatically with Windows</summary>
    public bool AutoStartWithWindows { get; set; } = true;

    /// <summary>Saved window position X (-1 = auto)</summary>
    public double WindowLeft { get; set; } = -1;

    /// <summary>Saved window position Y (-1 = auto)</summary>
    public double WindowTop { get; set; } = -1;

    /// <summary>Saved window width (-1 = default)</summary>
    public double WindowWidth { get; set; } = -1;

    /// <summary>Saved window height (-1 = default)</summary>
    public double WindowHeight { get; set; } = -1;

    /// <summary>Saved mini mode width (-1 = default)</summary>
    public double MiniModeWidth { get; set; } = -1;

    /// <summary>UI language override: "auto" = system default, or a culture code like "en", "fr", "es"</summary>
    public string Language { get; set; } = "auto";

    public TimeSpan GetLunchStart()
    {
        return TimeSpan.TryParse(LunchStartTime, CultureInfo.InvariantCulture, out var ts) ? ts : new TimeSpan(12, 0, 0);
    }

    public TimeSpan GetLunchEnd()
    {
        return GetLunchStart().Add(TimeSpan.FromMinutes(LunchDurationMinutes));
    }
}
