using DayloaderClock.Models;
using Microsoft.Win32;

namespace DayloaderClock.Services;

/// <summary>
/// Core time-tracking logic for the work day.
/// Calculates effective work time, progress, overtime, and estimated end time.
/// Lunch break is detected via screen lock/unlock during the configured lunch window.
/// </summary>
public class SessionService
{
    private AppSettings _settings;
    private readonly IStorageService _storage;
    private readonly TimeProvider _timeProvider;
    private SessionStore _store;
    private string _currentDate;
    private bool _overtimeNotified;

    // ── Pause tracking ────────────────────────────────────────
    private bool _isPaused;
    private DateTime _pauseStartTime;
    private TimeSpan _totalPausedTime = TimeSpan.Zero;

    // ── Lunch tracking (screen-lock based) ────────────────────
    private bool _isScreenLocked;
    private DateTime _lockTimeStamp;
    private TimeSpan _totalLunchTime = TimeSpan.Zero;
    private bool _isOnLunch; // true while screen is locked during lunch window

    /// <summary>Time the user first logged in today.</summary>
    public DateTime LoginTime { get; private set; }

    /// <summary>Whether the clock is currently paused.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>Total time spent paused today.</summary>
    public TimeSpan TotalPausedTime => _isPaused
        ? _totalPausedTime + (Now - _pauseStartTime)
        : _totalPausedTime;

    /// <summary>Fired once when overtime starts (effective work > work day duration).</summary>
    public event Action? OvertimeStarted;

    /// <summary>Fired when pause state changes.</summary>
    public event Action<bool>? PauseStateChanged;

    /// <summary>Current local time from the injected TimeProvider.</summary>
    private DateTime Now => _timeProvider.GetLocalNow().DateTime;

    /// <summary>Current local date from the injected TimeProvider.</summary>
    private DateTime Today => _timeProvider.GetLocalNow().Date;

    public SessionService(AppSettings settings)
        : this(settings, StorageService.Instance, TimeProvider.System)
    {
    }

    public SessionService(AppSettings settings, IStorageService storage, TimeProvider timeProvider)
    {
        _settings = settings;
        _storage = storage;
        _timeProvider = timeProvider;
        _store = _storage.LoadSessions();
        _currentDate = "";
        Initialize();

        // Listen for screen lock / unlock
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    ~SessionService()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _isScreenLocked = true;
            _lockTimeStamp = Now;

            // If we're inside the lunch window, flag as lunch
            if (IsInLunchWindow)
                _isOnLunch = true;
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            if (_isScreenLocked)
            {
                var lockDuration = Now - _lockTimeStamp;

                if (_isOnLunch)
                {
                    // All time spent locked counts as lunch (capped to configured max)
                    var maxLunch = TimeSpan.FromMinutes(_settings.LunchDurationMinutes);
                    var lunchSoFar = _totalLunchTime + lockDuration;
                    _totalLunchTime = lunchSoFar > maxLunch ? maxLunch : lunchSoFar;
                    _isOnLunch = false;
                }

                _isScreenLocked = false;
            }
        }
    }

    private void Initialize()
    {
        _currentDate = Today.ToString("yyyy-MM-dd");

        if (_store.CurrentSession != null && _store.CurrentSession.Date == _currentDate)
        {
            // Resume today's session
            LoginTime = DateTime.Parse(_store.CurrentSession.FirstLoginTime);

            // Restore paused time from disk
            _totalPausedTime = TimeSpan.FromMinutes(_store.CurrentSession.TotalPausedMinutes);
            if (_store.CurrentSession.IsPaused && _store.CurrentSession.PauseStartTime != null)
            {
                _isPaused = true;
                _pauseStartTime = DateTime.Parse(_store.CurrentSession.PauseStartTime);
            }

            // Restore lunch time from disk
            _totalLunchTime = TimeSpan.FromMinutes(_store.CurrentSession.TotalLunchMinutes);
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
            LoginTime = Now;
            _store.CurrentSession = new DaySession
            {
                Date = _currentDate,
                FirstLoginTime = LoginTime.ToString("o")
            };
            _storage.SaveSessions(_store);
        }
    }

    /// <summary>
    /// Check if the date changed (e.g. left running overnight).
    /// Returns true if a new day was started.
    /// </summary>
    public bool CheckNewDay()
    {
        var today = Today.ToString("yyyy-MM-dd");
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
        LoginTime = Now;
        _store.CurrentSession = new DaySession
        {
            Date = _currentDate,
            FirstLoginTime = LoginTime.ToString("o")
        };
        _overtimeNotified = false;
        _totalPausedTime = TimeSpan.Zero;
        _totalLunchTime = TimeSpan.Zero;
        _isPaused = false;
        _isOnLunch = false;
        _storage.SaveSessions(_store);
        return true;
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
    }

    // ── Pause / Resume ────────────────────────────────────────

    public void Pause()
    {
        if (_isPaused) return;
        _isPaused = true;
        _pauseStartTime = Now;
        PauseStateChanged?.Invoke(true);
    }

    public void Resume()
    {
        if (!_isPaused) return;
        _totalPausedTime += Now - _pauseStartTime;
        _isPaused = false;
        PauseStateChanged?.Invoke(false);
    }

    public void TogglePause()
    {
        if (_isPaused) Resume(); else Pause();
    }

    /// <summary>Reset the current day: new login time = now, clear all pause time.</summary>
    public void ResetDay()
    {
        _isPaused = false;
        _totalPausedTime = TimeSpan.Zero;
        _totalLunchTime = TimeSpan.Zero;
        _isOnLunch = false;
        _overtimeNotified = false;
        LoginTime = Now;

        _store.CurrentSession = new DaySession
        {
            Date = _currentDate,
            FirstLoginTime = LoginTime.ToString("o")
        };
        _storage.SaveSessions(_store);
        PauseStateChanged?.Invoke(false);
    }

    // ── Time calculations ─────────────────────────────────────

    /// <summary>
    /// Effective work time = elapsed since login − actual lunch taken − paused time.
    /// Lunch is only deducted when the screen was locked during the lunch window.
    /// </summary>
    public TimeSpan GetEffectiveWorkTime()
    {
        var now = Now;
        var totalElapsed = now - LoginTime;
        if (totalElapsed < TimeSpan.Zero) totalElapsed = TimeSpan.Zero;

        // Current lunch time being recorded (screen still locked during lunch)
        var currentLunchExtra = TimeSpan.Zero;
        if (_isScreenLocked && _isOnLunch)
        {
            var maxLunch = TimeSpan.FromMinutes(_settings.LunchDurationMinutes);
            var potentialTotal = _totalLunchTime + (now - _lockTimeStamp);
            currentLunchExtra = (potentialTotal > maxLunch ? maxLunch : potentialTotal) - _totalLunchTime;
        }

        var effective = totalElapsed - (_totalLunchTime + currentLunchExtra) - TotalPausedTime;
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

    /// <summary>Whether we are currently in the configured lunch time window.</summary>
    public bool IsInLunchWindow
    {
        get
        {
            var now = Now.TimeOfDay;
            return now >= _settings.GetLunchStart() && now < _settings.GetLunchEnd();
        }
    }

    /// <summary>Whether the user is currently on lunch (screen locked during lunch window).</summary>
    public bool IsOnLunchBreak => _isOnLunch && _isScreenLocked;

    /// <summary>Whether we're in the lunch window — used for the UI indicator.</summary>
    public bool IsLunchTime => IsInLunchWindow;

    /// <summary>
    /// Estimated departure time, accounting for remaining lunch if applicable.
    /// If no lunch has been taken yet and we're before the lunch window,
    /// we assume the full configured duration will be taken.
    /// </summary>
    public DateTime GetEstimatedEndTime()
    {
        var remaining = GetRemainingTime();
        var now = Now;
        var lunchStart = Today.Add(_settings.GetLunchStart());
        var lunchEnd = Today.Add(_settings.GetLunchEnd());
        var maxLunch = TimeSpan.FromMinutes(_settings.LunchDurationMinutes);
        var lunchRemaining = maxLunch - _totalLunchTime;
        if (lunchRemaining < TimeSpan.Zero) lunchRemaining = TimeSpan.Zero;

        if (now < lunchStart && lunchRemaining > TimeSpan.Zero)
        {
            // Lunch hasn't started → assume full remaining lunch will be taken
            return now + remaining + lunchRemaining;
        }
        else if (now < lunchEnd && lunchRemaining > TimeSpan.Zero)
        {
            // In the lunch window with remaining lunch to take
            return now + remaining + lunchRemaining;
        }
        else
        {
            // Lunch window passed or all lunch taken
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
            _store.CurrentSession.TotalPausedMinutes = TotalPausedTime.TotalMinutes;
            _store.CurrentSession.IsPaused = _isPaused;
            _store.CurrentSession.PauseStartTime = _isPaused ? _pauseStartTime.ToString("o") : null;
            _store.CurrentSession.TotalLunchMinutes = _totalLunchTime.TotalMinutes;
            _store.CurrentSession.LastActivityTime = Now.ToString("o");
            _storage.SaveSessions(_store);
        }
    }
}
