namespace DayloaderClock.Models;

/// <summary>
/// Represents a single work day session.
/// </summary>
public class DaySession
{
    /// <summary>Date string in yyyy-MM-dd format</summary>
    public string Date { get; set; } = "";

    /// <summary>First login time (ISO 8601)</summary>
    public string FirstLoginTime { get; set; } = "";

    /// <summary>Last recorded activity time (ISO 8601)</summary>
    public string? LastActivityTime { get; set; }

    /// <summary>Total effective work minutes (excluding lunch)</summary>
    public double TotalEffectiveWorkMinutes { get; set; }

    /// <summary>Whether the day was completed (8h reached or day ended)</summary>
    public bool DayCompleted { get; set; }
}

/// <summary>
/// Container for current session + history, persisted as JSON.
/// </summary>
public class SessionStore
{
    public DaySession? CurrentSession { get; set; }
    public List<DaySession> History { get; set; } = new();
}
