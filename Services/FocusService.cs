using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DayloaderClock.Services;

/// <summary>
/// Blocks all Windows notification popups (Teams, Outlook, Edge, etc.)
/// during a Pomodoro session by toggling the ToastEnabled registry value.
/// </summary>
public static class FocusService
{
    private static bool _wasToastEnabled = true;

    // ── Public API ────────────────────────────────────────────

    /// <summary>
    /// Disable Windows toast notifications.
    /// </summary>
    public static void EnableDnd()
    {
        try
        {
            _wasToastEnabled = GetToastEnabled();
            SetToastEnabled(false);
        }
        catch
        {
            // Registry/notification APIs may be unavailable on some Windows editions
        }
    }

    /// <summary>
    /// Restore previous notification state.
    /// </summary>
    public static void DisableDnd()
    {
        try
        {
            SetToastEnabled(_wasToastEnabled);
        }
        catch
        {
            // Registry/notification APIs may be unavailable on some Windows editions
        }
    }

    /// <summary>
    /// Opens Teams so the user can quickly toggle their status to DND manually.
    /// Uses the msteams: protocol which works with both classic and new Teams.
    /// </summary>
    public static void OpenTeamsForStatusChange()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msteams:",
                UseShellExecute = true
            });
        }
        catch
        {
            // Teams may not be installed or the protocol handler may not be registered
        }
    }

    // ── Toast notifications (registry) ───────────────────────

    private static bool GetToastEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\PushNotifications");
            var val = key?.GetValue("ToastEnabled");
            return val == null || (int)val != 0;
        }
        catch { return true; }
    }

    private static void SetToastEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", true);
            key?.SetValue("ToastEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);

            // Notify the shell so the change takes effect immediately
            NativeMethods.SendNotifyMessage(
                NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, 0, 0);
        }
        catch
        {
            // Registry access may be restricted in corporate environments
        }
    }

    // ── Native interop ───────────────────────────────────────

    private static class NativeMethods
    {
        public const int HWND_BROADCAST = 0xFFFF;
        public const int WM_SETTINGCHANGE = 0x001A;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SendNotifyMessage(int hWnd, int msg, int wParam, int lParam);
    }
}
