using DayloaderClock.Models;

namespace DayloaderClock.Services;

/// <summary>
/// Core time-tracking logic for the work day.
/// Calculates effective work time, progress, overtime, and estimated end time.
/// </summary>
public class SessionService
{
    private AppSettings _settings;
    private SessionStore _store;
    private string _currentDate;
    private bool _overtimeNotified;

    /// <summary>Time the user first logged in today.</summary>
    public DateTime LoginTime { get; private set; }

    /// <summary>Fired once when overtime starts (effective work > work day duration).</summary>
    public event Action? OvertimeStarted;

    public SessionService(AppSettings settings)
    {
        _settings = settings;
        _store = StorageService.LoadSessions();
        _currentDate = "";
        Initialize();
    }

    private void Initialize()
    {
        _currentDate = DateTime.Today.ToString("yyyy-MM-dd");

        if (_store.CurrentSession != null && _store.CurrentSession.Date == _currentDate)
        {
            // Resume today's session
            LoginTime = DateTime.Parse(_store.CurrentSession.FirstLoginTime);
        }
        else
        {
            // Archive previous session if present
            if (_store.CurrentSession != null)
            {
                _store.CurrentSession.DayCompleted = true;
                _store.History.Add(_store.CurrentSession);
            }

            // Start a brand new session
            LoginTime = DateTime.Now;
            _store.CurrentSession = new DaySession
            {
                Date = _currentDate,
                FirstLoginTime = LoginTime.ToString("o")
            };
            StorageService.SaveSessions(_store);
        }
    }

    /// <summary>
    /// Check if the date changed (e.g. left running overnight).
    /// Returns true if a new day was started.
    /// </summary>
    public bool CheckNewDay()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (today == _currentDate) return false;

        // Archive current session
        if (_store.CurrentSession != null)
        {
            _store.CurrentSession.DayCompleted = true;
            _store.CurrentSession.TotalEffectiveWorkMinutes = GetEffectiveWorkTime().TotalMinutes;
            _store.History.Add(_store.CurrentSession);
        }

        // Start new day
        _currentDate = today;
        LoginTime = DateTime.Now;
        _store.CurrentSession = new DaySession
        {
            Date = _currentDate,
            FirstLoginTime = LoginTime.ToString("o")
        };
        _overtimeNotified = false;
        StorageService.SaveSessions(_store);
        return true;
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
    }

    // ── Time calculations ─────────────────────────────────────

    /// <summary>
    /// Effective work time = elapsed since login − lunch overlap.
    /// The countdown never stops once logged in; lunch time is simply excluded.
    /// </summary>
    public TimeSpan GetEffectiveWorkTime()
    {
        var now = DateTime.Now;
        var totalElapsed = now - LoginTime;
        if (totalElapsed < TimeSpan.Zero) totalElapsed = TimeSpan.Zero;

        // Calculate overlap between [LoginTime, Now] and [LunchStart, LunchEnd]
        var lunchStart = DateTime.Today.Add(_settings.GetLunchStart());
        var lunchEnd = DateTime.Today.Add(_settings.GetLunchEnd());

        var overlapStart = LoginTime > lunchStart ? LoginTime : lunchStart;
        var overlapEnd = now < lunchEnd ? now : lunchEnd;

        var lunchOverlap = TimeSpan.Zero;
        if (overlapStart < overlapEnd)
        {
            lunchOverlap = overlapEnd - overlapStart;
        }

        var effective = totalElapsed - lunchOverlap;
        return effective > TimeSpan.Zero ? effective : TimeSpan.Zero;
    }

    /// <summary>Progress percentage (can exceed 100).</summary>
    public double GetProgressPercent()
    {
        return GetEffectiveWorkTime().TotalMinutes / _settings.WorkDayMinutes * 100.0;
    }

    /// <summary>Number of filled cells (capped at totalCells).</summary>
    public int GetFilledCells(int totalCells)
    {
        var percent = Math.Min(GetProgressPercent(), 100.0);
        return (int)Math.Floor(percent / 100.0 * totalCells);
    }

    /// <summary>Remaining work time (zero if overtime).</summary>
    public TimeSpan GetRemainingTime()
    {
        var remaining = _settings.WorkDayMinutes - GetEffectiveWorkTime().TotalMinutes;
        return remaining > 0 ? TimeSpan.FromMinutes(remaining) : TimeSpan.Zero;
    }

    /// <summary>Overtime amount (zero if not overtime).</summary>
    public TimeSpan GetOvertimeTime()
    {
        var overtime = GetEffectiveWorkTime().TotalMinutes - _settings.WorkDayMinutes;
        return overtime > 0 ? TimeSpan.FromMinutes(overtime) : TimeSpan.Zero;
    }

    public bool IsOvertime => GetEffectiveWorkTime().TotalMinutes > _settings.WorkDayMinutes;

    public bool IsLunchTime
    {
        get
        {
            var now = DateTime.Now.TimeOfDay;
            return now >= _settings.GetLunchStart() && now < _settings.GetLunchEnd();
        }
    }

    /// <summary>
    /// Estimated departure time, accounting for remaining lunch if applicable.
    /// </summary>
    public DateTime GetEstimatedEndTime()
    {
        var remaining = GetRemainingTime();
        var now = DateTime.Now;
        var lunchStart = DateTime.Today.Add(_settings.GetLunchStart());
        var lunchEnd = DateTime.Today.Add(_settings.GetLunchEnd());

        if (now < lunchStart)
        {
            // Lunch hasn't started → add full lunch duration
            return now + remaining + TimeSpan.FromMinutes(_settings.LunchDurationMinutes);
        }
        else if (now < lunchEnd)
        {
            // Currently in lunch → add remaining lunch time
            return now + remaining + (lunchEnd - now);
        }
        else
        {
            // Lunch already passed
            return now + remaining;
        }
    }

    /// <summary>Check overtime once and fire event if just crossed the threshold.</summary>
    public void CheckAndNotifyOvertime()
    {
        if (IsOvertime && !_overtimeNotified)
        {
            _overtimeNotified = true;
            OvertimeStarted?.Invoke();
        }
    }

    /// <summary>Persist current session state to disk.</summary>
    public void SaveState()
    {
        if (_store.CurrentSession != null)
        {
            _store.CurrentSession.TotalEffectiveWorkMinutes = GetEffectiveWorkTime().TotalMinutes;
            _store.CurrentSession.LastActivityTime = DateTime.Now.ToString("o");
            StorageService.SaveSessions(_store);
        }
    }
}
