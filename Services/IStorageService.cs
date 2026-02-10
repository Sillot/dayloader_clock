using DayloaderClock.Models;

namespace DayloaderClock.Services;

/// <summary>
/// Abstraction over persistence of settings and session data.
/// </summary>
public interface IStorageService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    SessionStore LoadSessions();
    void SaveSessions(SessionStore store);
    string GetDataFolderPath();
}
