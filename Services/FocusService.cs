using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DayloaderClock.Services;

/// <summary>
/// Activates Windows Do Not Disturb to block all notification popups
/// (Teams, Outlook, Edge, etc.) during a Pomodoro session.
///
/// Note: This blocks the *popups* at the OS level. The actual Teams "status"
/// (visible to colleagues) requires Microsoft Graph API and is not changed here.
/// An optional helper opens Teams for a quick manual status toggle.
/// </summary>
public static class FocusService
{
    private static bool _wasToastEnabled = true;
    private static int _previousQuietHoursState;

    // ── Public API ────────────────────────────────────────────

    /// <summary>
    /// Enable Windows DND — blocks all toast notification popups.
    /// </summary>
    public static void EnableDnd()
    {
        try
        {
            _wasToastEnabled = GetToastEnabled();
            _previousQuietHoursState = GetQuietHoursState();

            // Disable toast notifications (Win10/11)
            SetToastEnabled(false);

            // Enable Focus Assist / Quiet Hours — "Alarms only" mode
            SetQuietHoursState(2);
        }
        catch { }
    }

    /// <summary>
    /// Restore previous notification state.
    /// </summary>
    public static void DisableDnd()
    {
        try
        {
            if (_wasToastEnabled)
                SetToastEnabled(true);

            SetQuietHoursState(_previousQuietHoursState);
        }
        catch { }
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
        catch { }
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
        catch { }
    }

    // ── Focus Assist / Quiet Hours ───────────────────────────

    private static int GetQuietHoursState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings");
            var val = key?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED");
            return val is int i ? i : 1; // 1 = enabled (default)
        }
        catch { return 1; }
    }

    private static void SetQuietHoursState(int state)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings");
            key?.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", state, RegistryValueKind.DWord);

            NativeMethods.SendNotifyMessage(
                NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, 0, 0);
        }
        catch { }
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
