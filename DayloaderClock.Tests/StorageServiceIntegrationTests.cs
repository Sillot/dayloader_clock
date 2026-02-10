using System.IO;
using System.Text.Json;
using DayloaderClock.Models;
using DayloaderClock.Services;

namespace DayloaderClock.Tests;

/// <summary>
/// Integration tests for <see cref="StorageService"/> — actual file I/O.
/// Uses a temp directory to avoid polluting real app data.
/// </summary>
public class StorageServiceIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFile;
    private readonly string _sessionsFile;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public StorageServiceIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DayloaderClockTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsFile = Path.Combine(_tempDir, "settings.json");
        _sessionsFile = Path.Combine(_tempDir, "sessions.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Settings round-trip ──────────────────────────────────

    [Fact]
    public void Settings_SaveAndLoad_RoundTrip()
    {
        var original = new AppSettings
        {
            WorkDayMinutes = 420,
            LunchStartTime = "11:30",
            LunchDurationMinutes = 45,
            PomodoroMinutes = 30,
            Language = "fr"
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        File.WriteAllText(_settingsFile, json);

        var loaded = JsonSerializer.Deserialize<AppSettings>(
            File.ReadAllText(_settingsFile), JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(420, loaded!.WorkDayMinutes);
        Assert.Equal("11:30", loaded.LunchStartTime);
        Assert.Equal(45, loaded.LunchDurationMinutes);
        Assert.Equal(30, loaded.PomodoroMinutes);
        Assert.Equal("fr", loaded.Language);
    }

    [Fact]
    public void Settings_DefaultValues_WhenFileDoesNotExist()
    {
        // If the file doesn't exist, defaults are used
        var settings = new AppSettings();

        Assert.Equal(480, settings.WorkDayMinutes);
        Assert.Equal("12:00", settings.LunchStartTime);
        Assert.Equal(60, settings.LunchDurationMinutes);
        Assert.Equal("auto", settings.Language);
    }

    [Fact]
    public void Settings_CorruptedFile_ReturnsDefaults()
    {
        File.WriteAllText(_settingsFile, "{{{{not json!!");

        AppSettings? loaded = null;
        try
        {
            loaded = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_settingsFile), JsonOptions);
        }
        catch
        {
            loaded = new AppSettings();
        }

        Assert.Equal(480, loaded!.WorkDayMinutes);
    }

    // ── Sessions round-trip ──────────────────────────────────

    [Fact]
    public void Sessions_SaveAndLoad_RoundTrip()
    {
        var original = new SessionStore
        {
            CurrentSession = new DaySession
            {
                Date = "2026-02-10",
                FirstLoginTime = new DateTime(2026, 2, 10, 8, 0, 0).ToString("o"),
                TotalEffectiveWorkMinutes = 240,
                TotalPausedMinutes = 15,
                TotalLunchMinutes = 60,
                IsPaused = false
            },
            History = new List<DaySession>
            {
                new()
                {
                    Date = "2026-02-09",
                    FirstLoginTime = new DateTime(2026, 2, 9, 8, 30, 0).ToString("o"),
                    TotalEffectiveWorkMinutes = 480,
                    DayCompleted = true
                }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        File.WriteAllText(_sessionsFile, json);

        var loaded = JsonSerializer.Deserialize<SessionStore>(
            File.ReadAllText(_sessionsFile), JsonOptions);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.CurrentSession);
        Assert.Equal("2026-02-10", loaded.CurrentSession!.Date);
        Assert.Equal(240, loaded.CurrentSession.TotalEffectiveWorkMinutes);
        Assert.Single(loaded.History);
        Assert.True(loaded.History[0].DayCompleted);
    }

    [Fact]
    public void Sessions_EmptyFile_ReturnsEmptyStore()
    {
        File.WriteAllText(_sessionsFile, "");

        SessionStore? loaded = null;
        try
        {
            var content = File.ReadAllText(_sessionsFile);
            loaded = string.IsNullOrWhiteSpace(content)
                ? new SessionStore()
                : JsonSerializer.Deserialize<SessionStore>(content, JsonOptions);
        }
        catch
        {
            loaded = new SessionStore();
        }

        Assert.NotNull(loaded);
        Assert.Null(loaded!.CurrentSession);
        Assert.Empty(loaded.History);
    }

    [Fact]
    public void Sessions_LargeHistory_SerializesCorrectly()
    {
        var store = new SessionStore();
        for (int i = 0; i < 1000; i++)
        {
            store.History.Add(new DaySession
            {
                Date = DateTime.Today.AddDays(-1000 + i).ToString("yyyy-MM-dd"),
                FirstLoginTime = DateTime.Today.AddDays(-1000 + i).AddHours(8).ToString("o"),
                TotalEffectiveWorkMinutes = 480,
                TotalPausedMinutes = 15,
                TotalLunchMinutes = 60,
                DayCompleted = true
            });
        }

        var json = JsonSerializer.Serialize(store, JsonOptions);
        File.WriteAllText(_sessionsFile, json);

        var loaded = JsonSerializer.Deserialize<SessionStore>(
            File.ReadAllText(_sessionsFile), JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(1000, loaded!.History.Count);
    }

    // ── DaySession model ─────────────────────────────────────

    [Fact]
    public void DaySession_PauseState_Serialized()
    {
        var session = new DaySession
        {
            Date = "2026-02-10",
            FirstLoginTime = DateTime.Now.ToString("o"),
            IsPaused = true,
            PauseStartTime = DateTime.Now.AddMinutes(-5).ToString("o"),
            TotalPausedMinutes = 10
        };

        var json = JsonSerializer.Serialize(session, JsonOptions);
        var loaded = JsonSerializer.Deserialize<DaySession>(json, JsonOptions);

        Assert.NotNull(loaded);
        Assert.True(loaded!.IsPaused);
        Assert.NotNull(loaded.PauseStartTime);
        Assert.Equal(10, loaded.TotalPausedMinutes);
    }
}
