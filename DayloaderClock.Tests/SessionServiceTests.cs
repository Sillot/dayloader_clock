using DayloaderClock.Models;
using DayloaderClock.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DayloaderClock.Tests;

/// <summary>
/// Unit tests for <see cref="Services.SessionService"/> — core time calculations.
/// </summary>
public class SessionServiceTests
{
    // ── Effective work time ──────────────────────────────────

    [Fact]
    public void NewSession_EffectiveWorkTime_IsZero()
    {
        var (svc, _, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        Assert.Equal(TimeSpan.Zero, svc.GetEffectiveWorkTime());
    }

    [Fact]
    public void After4Hours_EffectiveWorkTime_Is4Hours()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(4));

        Assert.Equal(TimeSpan.FromHours(4), svc.GetEffectiveWorkTime());
    }

    [Fact]
    public void After8Hours_EffectiveWorkTime_Is8Hours()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(8));

        Assert.Equal(TimeSpan.FromHours(8), svc.GetEffectiveWorkTime());
    }

    // ── Progress percentage ──────────────────────────────────

    [Fact]
    public void ProgressAt0_Is0Percent()
    {
        var (svc, _, _) = new SessionServiceBuilder().Build();
        Assert.Equal(0.0, svc.GetProgressPercent(), precision: 1);
    }

    [Fact]
    public void ProgressAtHalfDay_Is50Percent()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(4)); // 240 min / 480 min = 50%

        Assert.Equal(50.0, svc.GetProgressPercent(), precision: 1);
    }

    [Fact]
    public void ProgressAtFullDay_Is100Percent()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(8));

        Assert.Equal(100.0, svc.GetProgressPercent(), precision: 1);
    }

    [Fact]
    public void ProgressCanExceed100()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(10)); // 125%

        Assert.Equal(125.0, svc.GetProgressPercent(), precision: 1);
    }

    // ── Filled cells ─────────────────────────────────────────

    [Theory]
    [InlineData(0, 80, 0)]
    [InlineData(240, 80, 40)]  // 50% → 40/80
    [InlineData(480, 80, 80)]  // 100% → 80/80
    [InlineData(600, 80, 80)]  // >100% → capped at 80
    public void FilledCells_Proportional(int workedMinutes, int totalCells, int expectedFilled)
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromMinutes(workedMinutes));

        Assert.Equal(expectedFilled, svc.GetFilledCells(totalCells));
    }

    // ── Remaining time ───────────────────────────────────────

    [Fact]
    public void RemainingTime_AtStart_IsFullDay()
    {
        var (svc, _, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .Build();

        Assert.Equal(TimeSpan.FromMinutes(480), svc.GetRemainingTime());
    }

    [Fact]
    public void RemainingTime_AfterFullDay_IsZero()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(8));

        Assert.Equal(TimeSpan.Zero, svc.GetRemainingTime());
    }

    [Fact]
    public void RemainingTime_InOvertime_IsZero()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(10));

        Assert.Equal(TimeSpan.Zero, svc.GetRemainingTime());
    }

    // ── Overtime ─────────────────────────────────────────────

    [Fact]
    public void Overtime_BeforeDayEnd_IsZero()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(7));

        Assert.Equal(TimeSpan.Zero, svc.GetOvertimeTime());
        Assert.False(svc.IsOvertime);
    }

    [Fact]
    public void Overtime_After9Hours_Is1Hour()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(9));

        Assert.Equal(TimeSpan.FromHours(1), svc.GetOvertimeTime());
        Assert.True(svc.IsOvertime);
    }

    [Fact]
    public void OvertimeNotification_FiredOnce()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        int fireCount = 0;
        svc.OvertimeStarted += () => fireCount++;

        // Not overtime yet
        time.Advance(TimeSpan.FromHours(7));
        svc.CheckAndNotifyOvertime();
        Assert.Equal(0, fireCount);

        // Cross the threshold
        time.Advance(TimeSpan.FromHours(2)); // total 9h
        svc.CheckAndNotifyOvertime();
        Assert.Equal(1, fireCount);

        // Still overtime — should NOT fire again
        time.Advance(TimeSpan.FromHours(1));
        svc.CheckAndNotifyOvertime();
        Assert.Equal(1, fireCount);
    }

    // ── Pause / Resume ───────────────────────────────────────

    [Fact]
    public void Pause_SubtractsPausedTimeFromEffective()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .WithSettings(s => s.WorkDayMinutes = 480)
            .Build();

        // Work 2 hours
        time.Advance(TimeSpan.FromHours(2));

        // Pause for 30 minutes
        svc.Pause();
        Assert.True(svc.IsPaused);
        time.Advance(TimeSpan.FromMinutes(30));
        svc.Resume();
        Assert.False(svc.IsPaused);

        // Work 1 more hour
        time.Advance(TimeSpan.FromHours(1));

        // Effective = 3.5h elapsed - 0.5h pause = 3h
        var effective = svc.GetEffectiveWorkTime();
        Assert.Equal(TimeSpan.FromHours(3), effective);
    }

    [Fact]
    public void MultiplePauses_Accumulate()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .WithSettings(s => s.WorkDayMinutes = 480)
            .Build();

        time.Advance(TimeSpan.FromHours(1));

        // Pause 1: 15 min
        svc.Pause();
        time.Advance(TimeSpan.FromMinutes(15));
        svc.Resume();

        time.Advance(TimeSpan.FromHours(1));

        // Pause 2: 10 min
        svc.Pause();
        time.Advance(TimeSpan.FromMinutes(10));
        svc.Resume();

        time.Advance(TimeSpan.FromHours(1));

        // Total elapsed = 3h25m, total paused = 25m → effective = 3h
        Assert.Equal(TimeSpan.FromHours(3), svc.GetEffectiveWorkTime());
        Assert.Equal(TimeSpan.FromMinutes(25), svc.TotalPausedTime);
    }

    [Fact]
    public void TotalPausedTime_IncludesCurrentPause()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(1));
        svc.Pause();
        time.Advance(TimeSpan.FromMinutes(20));

        // Still paused — TotalPausedTime should include current pause
        Assert.True(svc.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(20), svc.TotalPausedTime);
    }

    [Fact]
    public void Pause_WhenAlreadyPaused_IsNoOp()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        svc.Pause();
        time.Advance(TimeSpan.FromMinutes(10));
        svc.Pause(); // should not reset the pause start time
        time.Advance(TimeSpan.FromMinutes(10));
        svc.Resume();

        Assert.Equal(TimeSpan.FromMinutes(20), svc.TotalPausedTime);
    }

    [Fact]
    public void Resume_WhenNotPaused_IsNoOp()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(1));
        svc.Resume(); // no-op

        Assert.Equal(TimeSpan.FromHours(1), svc.GetEffectiveWorkTime());
    }

    [Fact]
    public void TogglePause_SwitchesState()
    {
        var (svc, _, _) = new SessionServiceBuilder().Build();

        Assert.False(svc.IsPaused);
        svc.TogglePause();
        Assert.True(svc.IsPaused);
        svc.TogglePause();
        Assert.False(svc.IsPaused);
    }

    [Fact]
    public void PauseStateChanged_EventFired()
    {
        var (svc, _, _) = new SessionServiceBuilder().Build();
        var states = new List<bool>();
        svc.PauseStateChanged += paused => states.Add(paused);

        svc.Pause();
        svc.Resume();

        Assert.Equal(new[] { true, false }, states);
    }

    // ── ResetDay ─────────────────────────────────────────────

    [Fact]
    public void ResetDay_ClearsAllState()
    {
        var (svc, time, storage) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .WithSettings(s => s.WorkDayMinutes = 480)
            .Build();

        time.Advance(TimeSpan.FromHours(3));
        svc.Pause();
        time.Advance(TimeSpan.FromMinutes(15));

        svc.ResetDay();

        Assert.False(svc.IsPaused);
        Assert.Equal(TimeSpan.Zero, svc.TotalPausedTime);
        Assert.Equal(TimeSpan.Zero, svc.GetEffectiveWorkTime());
    }

    // ── CheckNewDay ──────────────────────────────────────────

    [Fact]
    public void CheckNewDay_SameDay_ReturnsFalse()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(4));

        Assert.False(svc.CheckNewDay());
    }

    [Fact]
    public void CheckNewDay_NextDay_ReturnsTrue_ResetsSession()
    {
        var (svc, time, storage) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .WithSettings(s => s.WorkDayMinutes = 480)
            .Build();

        // Work a full day
        time.Advance(TimeSpan.FromHours(8));

        // Jump to next morning
        time.Advance(TimeSpan.FromHours(16)); // now Feb 11, 08:00

        Assert.True(svc.CheckNewDay());
        Assert.Equal(TimeSpan.Zero, svc.GetEffectiveWorkTime());
    }

    // ── Estimated end time ───────────────────────────────────

    [Fact]
    public void EstimatedEndTime_BeforeLunch_IncludesLunchDuration()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s =>
            {
                s.WorkDayMinutes = 480;
                s.LunchStartTime = "12:00";
                s.LunchDurationMinutes = 60;
            })
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        // At 08:00, expected end = 08:00 + 8h work + 1h lunch = 17:00
        var end = svc.GetEstimatedEndTime();
        Assert.Equal(new DateTime(2026, 2, 10, 17, 0, 0), end);
    }

    [Fact]
    public void EstimatedEndTime_AfterLunch_ExcludesLunch()
    {
        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s =>
            {
                s.WorkDayMinutes = 480;
                s.LunchStartTime = "12:00";
                s.LunchDurationMinutes = 60;
            })
            .WithTime(new DateTimeOffset(2026, 2, 10, 14, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        // Started at 14:00, past lunch → end = 14:00 + 8h = 22:00
        var end = svc.GetEstimatedEndTime();
        Assert.Equal(new DateTime(2026, 2, 10, 22, 0, 0), end);
    }

    // ── SaveState ────────────────────────────────────────────

    [Fact]
    public void SaveState_PersistsToStorage()
    {
        var (svc, time, storage) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        time.Advance(TimeSpan.FromHours(2));
        svc.SaveState();

        storage.Received().SaveSessions(Arg.Any<SessionStore>());
    }

    // ── Session resume from disk ─────────────────────────────

    [Fact]
    public void ResumeSession_RestoresLoginTimeAndPausedTime()
    {
        var store = new SessionStore
        {
            CurrentSession = new DaySession
            {
                Date = "2026-02-10",
                FirstLoginTime = new DateTime(2026, 2, 10, 7, 30, 0).ToString("o"),
                TotalPausedMinutes = 15,
                TotalLunchMinutes = 0,
                IsPaused = false
            }
        };

        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 10, 0, 0, TimeSpan.FromHours(1)))
            .WithStore(store)
            .Build();

        // Login at 7:30, now 10:00 → 2.5h elapsed − 15min paused = 2h15m
        var expected = TimeSpan.FromHours(2.5) - TimeSpan.FromMinutes(15);
        Assert.Equal(expected, svc.GetEffectiveWorkTime());
    }

    [Fact]
    public void ResumeSession_RestoresPausedState()
    {
        var store = new SessionStore
        {
            CurrentSession = new DaySession
            {
                Date = "2026-02-10",
                FirstLoginTime = new DateTime(2026, 2, 10, 8, 0, 0).ToString("o"),
                TotalPausedMinutes = 10,
                IsPaused = true,
                PauseStartTime = new DateTime(2026, 2, 10, 9, 50, 0).ToString("o"),
            }
        };

        var (svc, time, _) = new SessionServiceBuilder()
            .WithTime(new DateTimeOffset(2026, 2, 10, 10, 0, 0, TimeSpan.FromHours(1)))
            .WithStore(store)
            .Build();

        Assert.True(svc.IsPaused);
        // Total paused = 10min saved + 10min current (9:50→10:00)
        Assert.Equal(TimeSpan.FromMinutes(20), svc.TotalPausedTime);
    }

    // ── AppSettings model tests ──────────────────────────────

    [Fact]
    public void AppSettings_GetLunchStart_ParsesCorrectly()
    {
        var s = new AppSettings { LunchStartTime = "12:30" };
        Assert.Equal(new TimeSpan(12, 30, 0), s.GetLunchStart());
    }

    [Fact]
    public void AppSettings_GetLunchEnd_AddsLunchDuration()
    {
        var s = new AppSettings { LunchStartTime = "12:00", LunchDurationMinutes = 45 };
        Assert.Equal(new TimeSpan(12, 45, 0), s.GetLunchEnd());
    }

    [Fact]
    public void AppSettings_InvalidLunchStart_FallsBackTo12()
    {
        var s = new AppSettings { LunchStartTime = "invalid" };
        Assert.Equal(new TimeSpan(12, 0, 0), s.GetLunchStart());
    }
}
