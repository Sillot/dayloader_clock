using System.IO;
using System.Text.Json;
using DayloaderClock.Models;

namespace DayloaderClock.Services;

/// <summary>
/// Handles persistence of settings and session data to %APPDATA%/DayloaderClock/.
/// </summary>
public static class StorageService
{
    private static readonly string AppFolder;
    private static readonly string SettingsFile;
    private static readonly string SessionFile;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    static StorageService()
    {
        AppFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DayloaderClock");
        SettingsFile = Path.Combine(AppFolder, "settings.json");
        SessionFile = Path.Combine(AppFolder, "sessions.json");
        Directory.CreateDirectory(AppFolder);
    }

    // ── Settings ──────────────────────────────────────────────

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch { /* corrupted file → return defaults */ }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFile, json);
    }

    // ── Sessions ──────────────────────────────────────────────

    public static SessionStore LoadSessions()
    {
        try
        {
            if (File.Exists(SessionFile))
            {
                var json = File.ReadAllText(SessionFile);
                return JsonSerializer.Deserialize<SessionStore>(json, JsonOptions) ?? new SessionStore();
            }
        }
        catch { /* corrupted file → return empty */ }
        return new SessionStore();
    }

    public static void SaveSessions(SessionStore store)
    {
        var json = JsonSerializer.Serialize(store, JsonOptions);
        File.WriteAllText(SessionFile, json);
    }

    /// <summary>Returns the path to the data folder (for display in settings).</summary>
    public static string GetDataFolderPath() => AppFolder;
}
