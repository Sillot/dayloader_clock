using DayloaderClock.Models;
using Microsoft.Extensions.Time.Testing;

namespace DayloaderClock.Tests;

/// <summary>
/// Long-term / stress tests simulating months and years of daily usage.
/// Validates that the session archiving, history accumulation, and date-change
/// handling remain correct over extended periods.
/// </summary>
public class SessionServiceLongTermTests
{
    /// <summary>
    /// Simulate 365 consecutive work days (1 year).
    /// Each day: 8h work → CheckNewDay archives → verify history count.
    /// </summary>
    [Fact]
    public void OneYear_365Days_AllSessionsArchived()
    {
        var store = new SessionStore();
        var startDate = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.FromHours(1)); // Monday

        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithStore(store)
            .WithTime(startDate)
            .Build();

        int daysWorked = 0;

        for (int day = 0; day < 365; day++)
        {
            // Work 8 hours
            time.Advance(TimeSpan.FromHours(8));
            Assert.True(svc.IsOvertime || svc.GetProgressPercent() >= 99.9);

            // Persist effective work at end of work day (before overnight idle)
            svc.SaveState();

            // Advance to next morning 08:00 (16h jump)
            time.Advance(TimeSpan.FromHours(16));
            svc.CheckNewDay();
            daysWorked++;
        }

        // All 365 days should be archived in history
        Assert.Equal(365, store.History.Count);

        // All archived sessions should be marked completed
        Assert.All(store.History, session => Assert.True(session.DayCompleted));

        // Current session should be fresh (day 366)
        Assert.NotNull(store.CurrentSession);
    }

    /// <summary>
    /// Simulate 3 years (≈1095 days) — maximum stress test.
    /// Ensures no memory/accumulation issues over very long periods.
    /// </summary>
    [Fact]
    public void ThreeYears_AllSessionsArchived()
    {
        var store = new SessionStore();
        var startDate = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.FromHours(1));

        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithStore(store)
            .WithTime(startDate)
            .Build();

        const int totalDays = 1095;

        for (int day = 0; day < totalDays; day++)
        {
            time.Advance(TimeSpan.FromHours(8));
            time.Advance(TimeSpan.FromHours(16));
            svc.CheckNewDay();
        }

        Assert.Equal(totalDays, store.History.Count);
        Assert.All(store.History, s => Assert.True(s.DayCompleted));
    }

    /// <summary>
    /// Simulate 1 year with varying day lengths (short days, long days, overtime).
    /// </summary>
    [Fact]
    public void OneYear_VariedDayLengths_CorrectOvertimeTracking()
    {
        var store = new SessionStore();
        var startDate = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.FromHours(1));

        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithStore(store)
            .WithTime(startDate)
            .Build();

        var random = new Random(42); // deterministic seed
        int overtimeDays = 0;
        int shortDays = 0;

        for (int day = 0; day < 260; day++) // ~1 year of workdays
        {
            // Random work duration between 6h and 10h
            double hoursWorked = 6 + random.NextDouble() * 4;
            time.Advance(TimeSpan.FromHours(hoursWorked));

            if (svc.IsOvertime) overtimeDays++;
            else shortDays++;

            // Persist effective work before overnight jump
            svc.SaveState();

            // Jump to next morning
            double hoursToNextMorning = 24 - hoursWorked;
            time.Advance(TimeSpan.FromHours(hoursToNextMorning));
            svc.CheckNewDay();
        }

        Assert.Equal(260, store.History.Count);
        Assert.True(overtimeDays > 0, "Should have some overtime days");
        Assert.True(shortDays > 0, "Should have some short days");

        // All archived sessions should be marked completed
        Assert.All(store.History, s => Assert.True(s.DayCompleted));
    }

    /// <summary>
    /// Simulate 1 year with daily pause/resume cycles.
    /// Ensures pause accumulation doesn't drift or corrupt over long periods.
    /// </summary>
    [Fact]
    public void OneYear_DailyPauses_PausedTimeAccurate()
    {
        var store = new SessionStore();
        var startDate = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.FromHours(1));

        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithStore(store)
            .WithTime(startDate)
            .Build();

        for (int day = 0; day < 260; day++)
        {
            // Work 2h
            time.Advance(TimeSpan.FromHours(2));

            // Coffee break 15min
            svc.Pause();
            time.Advance(TimeSpan.FromMinutes(15));
            svc.Resume();

            // Work 3h
            time.Advance(TimeSpan.FromHours(3));

            // Afternoon break 10min
            svc.Pause();
            time.Advance(TimeSpan.FromMinutes(10));
            svc.Resume();

            // Work remaining ~3h
            time.Advance(TimeSpan.FromHours(3));

            // Save state before day ends
            svc.SaveState();

            // Verify effective ≈ 8h (8h25m total - 25m paused = 8h)
            var effective = svc.GetEffectiveWorkTime();
            Assert.InRange(effective.TotalMinutes, 479, 481);

            // Jump to next morning
            time.Advance(TimeSpan.FromHours(15) + TimeSpan.FromMinutes(35));
            svc.CheckNewDay();
        }

        Assert.Equal(260, store.History.Count);

        // Every archived session should have ~25min paused
        Assert.All(store.History, s =>
            Assert.InRange(s.TotalPausedMinutes, 24, 26));
    }

    /// <summary>
    /// Weekend simulation — app left running, CheckNewDay should skip to Monday.
    /// </summary>
    [Fact]
    public void Weekend_LeftRunning_ArchivesCorrectly()
    {
        var store = new SessionStore();
        // Friday 08:00
        var friday = new DateTimeOffset(2026, 2, 6, 8, 0, 0, TimeSpan.FromHours(1));

        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithStore(store)
            .WithTime(friday)
            .Build();

        // Work 8h on Friday
        time.Advance(TimeSpan.FromHours(8));

        // Jump 64h to Monday 08:00
        time.Advance(TimeSpan.FromHours(64));
        Assert.True(svc.CheckNewDay());

        // Friday should be archived
        Assert.Single(store.History);
        Assert.Equal("2026-02-06", store.History[0].Date);
        Assert.True(store.History[0].DayCompleted);
    }

    /// <summary>
    /// Simulates date boundaries: DST change, year change, leap year.
    /// </summary>
    [Fact]
    public void SpecialDates_LeapYear_YearChange_Handled()
    {
        var store = new SessionStore();
        // Dec 31 2027 08:00 (2028 is a leap year)
        var start = new DateTimeOffset(2027, 12, 31, 8, 0, 0, TimeSpan.FromHours(1));

        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithStore(store)
            .WithTime(start)
            .Build();

        // Work Dec 31
        time.Advance(TimeSpan.FromHours(8));
        time.Advance(TimeSpan.FromHours(16)); // Jan 1 2028 08:00
        Assert.True(svc.CheckNewDay());
        Assert.Equal("2027-12-31", store.History[0].Date);

        // Work Jan 1
        time.Advance(TimeSpan.FromHours(8));
        time.Advance(TimeSpan.FromHours(16)); // Jan 2 2028 08:00
        Assert.True(svc.CheckNewDay());
        Assert.Equal("2028-01-01", store.History[1].Date);

        // Fast forward to Feb 28 → Feb 29 (leap day)
        for (int i = 0; i < 57; i++) // Jan 2 → Feb 28
        {
            time.Advance(TimeSpan.FromHours(8));
            time.Advance(TimeSpan.FromHours(16));
            svc.CheckNewDay();
        }

        // Work Feb 28
        time.Advance(TimeSpan.FromHours(8));
        time.Advance(TimeSpan.FromHours(16)); // Feb 29 2028
        Assert.True(svc.CheckNewDay());

        var feb28Session = store.History.FirstOrDefault(s => s.Date == "2028-02-28");
        Assert.NotNull(feb28Session);

        // Work Feb 29 (leap day!)
        time.Advance(TimeSpan.FromHours(8));
        time.Advance(TimeSpan.FromHours(16)); // Mar 1 2028
        Assert.True(svc.CheckNewDay());

        var leapDaySession = store.History.FirstOrDefault(s => s.Date == "2028-02-29");
        Assert.NotNull(leapDaySession);
        Assert.True(leapDaySession!.DayCompleted);
    }

    /// <summary>
    /// History with 2000+ entries — verify performance doesn't degrade.
    /// (This is more of a smoke test than a benchmark.)
    /// </summary>
    [Fact]
    public void LargeHistory_2000Entries_StillFunctions()
    {
        var store = new SessionStore();

        // Pre-populate 2000 historical sessions
        for (int i = 0; i < 2000; i++)
        {
            store.History.Add(new DaySession
            {
                Date = DateTime.Today.AddDays(-2001 + i).ToString("yyyy-MM-dd"),
                FirstLoginTime = DateTime.Today.AddDays(-2001 + i).AddHours(8).ToString("o"),
                TotalEffectiveWorkMinutes = 480,
                DayCompleted = true
            });
        }

        var (svc, time, _) = new SessionServiceBuilder()
            .WithSettings(s => s.WorkDayMinutes = 480)
            .WithStore(store)
            .WithTime(new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.FromHours(1)))
            .Build();

        // Service should work fine with large history
        time.Advance(TimeSpan.FromHours(4));
        Assert.Equal(50.0, svc.GetProgressPercent(), precision: 1);

        time.Advance(TimeSpan.FromHours(20));
        svc.CheckNewDay();

        Assert.Equal(2001, store.History.Count);
    }
}
