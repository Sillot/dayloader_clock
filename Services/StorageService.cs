using System.IO;
using System.Text.Json;
using DayloaderClock.Models;

namespace DayloaderClock.Services;

/// <summary>
/// Handles persistence of settings and session data to %APPDATA%/DayloaderClock/.
/// Implements <see cref="IStorageService"/> for dependency injection while keeping
/// static methods for backward compatibility.
/// </summary>
public class StorageService : IStorageService
{
    private static readonly string AppFolder;
    private static readonly string SettingsFilePath;
    private static readonly string SessionFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>Singleton instance for use as <see cref="IStorageService"/>.</summary>
    public static readonly StorageService Instance = new();

    static StorageService()
    {
        AppFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DayloaderClock");
        SettingsFilePath = Path.Combine(AppFolder, "settings.json");
        SessionFilePath = Path.Combine(AppFolder, "sessions.json");
        Directory.CreateDirectory(AppFolder);
    }

    // ── Settings ──────────────────────────────────────────────

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch { /* corrupted file → return defaults */ }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    // ── Sessions ──────────────────────────────────────────────

    public static SessionStore LoadSessions()
    {
        try
        {
            if (File.Exists(SessionFilePath))
            {
                var json = File.ReadAllText(SessionFilePath);
                return JsonSerializer.Deserialize<SessionStore>(json, JsonOptions) ?? new SessionStore();
            }
        }
        catch { /* corrupted file → return empty */ }
        return new SessionStore();
    }

    public static void SaveSessions(SessionStore store)
    {
        var json = JsonSerializer.Serialize(store, JsonOptions);
        File.WriteAllText(SessionFilePath, json);
    }

    /// <summary>Returns the path to the data folder (for display in settings).</summary>
    public static string GetDataFolderPath() => AppFolder;

    // ── IStorageService explicit implementation ───────────────

    AppSettings IStorageService.LoadSettings() => LoadSettings();
    void IStorageService.SaveSettings(AppSettings settings) => SaveSettings(settings);
    SessionStore IStorageService.LoadSessions() => LoadSessions();
    void IStorageService.SaveSessions(SessionStore store) => SaveSessions(store);
    string IStorageService.GetDataFolderPath() => GetDataFolderPath();
}
