namespace DayloaderClock.Helpers;

/// <summary>
/// Centralized version string accessor.
/// </summary>
public static class AppVersion
{
    /// <summary>Display string like "v0.2.0".</summary>
    public static string Display { get; } = GetVersionString();

    private static string GetVersionString()
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v?";
    }
}
