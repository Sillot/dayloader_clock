using System.IO;
using System.Text.Json;
using DayloaderClock.Models;

namespace DayloaderClock.Services;

/// <summary>
/// Handles persistence of settings and session data to %APPDATA%/DayloaderClock/.
/// Implements <see cref="IStorageService"/> for dependency injection.
/// Use <see cref="Instance"/> for production code; inject <see cref="IStorageService"/> for tests.
/// </summary>
public class StorageService : IStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Singleton instance for production use.</summary>
    public static readonly StorageService Instance = new();

    private readonly string _appFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DayloaderClock");

    private readonly string _settingsFilePath;
    private readonly string _sessionFilePath;

    public StorageService()
    {
        _settingsFilePath = Path.Combine(_appFolder, "settings.json");
        _sessionFilePath = Path.Combine(_appFolder, "sessions.json");
        Directory.CreateDirectory(_appFolder);
    }

    // ── Settings ──────────────────────────────────────────────

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupted file → return defaults
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    // ── Sessions ──────────────────────────────────────────────

    public SessionStore LoadSessions()
    {
        try
        {
            if (File.Exists(_sessionFilePath))
            {
                var json = File.ReadAllText(_sessionFilePath);
                return JsonSerializer.Deserialize<SessionStore>(json, JsonOptions) ?? new SessionStore();
            }
        }
        catch
        {
            // Corrupted file → return empty store
        }

        return new SessionStore();
    }

    public void SaveSessions(SessionStore store)
    {
        var json = JsonSerializer.Serialize(store, JsonOptions);
        File.WriteAllText(_sessionFilePath, json);
    }

    /// <summary>Returns the path to the data folder (for display in settings).</summary>
    public string GetDataFolderPath() => _appFolder;
}
