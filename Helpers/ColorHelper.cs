using System.Windows.Media;

namespace DayloaderClock.Helpers;

/// <summary>
/// Shared color palette and interpolation helpers used by MainWindow and HistoryWindow.
/// </summary>
public static class ColorHelper
{
    // ── Standard gradient stops (Green → Yellow → Orange → Red) ──
    public static readonly Color Green   = Color.FromRgb(76, 217, 100);
    public static readonly Color Yellow  = Color.FromRgb(255, 230, 50);
    public static readonly Color Orange  = Color.FromRgb(255, 149, 0);
    public static readonly Color Red     = Color.FromRgb(235, 64, 52);
    public static readonly Color Empty   = Color.FromRgb(30, 26, 18);
    public static readonly Color Overtime = Color.FromRgb(204, 51, 51);

    /// <summary>Linear interpolation between two WPF <see cref="Color"/> values.</summary>
    public static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    /// <summary>Linear interpolation between two <see cref="System.Drawing.Color"/> values (for tray icon).</summary>
    public static System.Drawing.Color LerpDrawing(
        System.Drawing.Color from, System.Drawing.Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return System.Drawing.Color.FromArgb(
            (int)(from.R + (to.R - from.R) * t),
            (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
    }

    /// <summary>
    /// Standard bar gradient: Green → Yellow → Orange → Red.
    /// <paramref name="index"/> is the segment index and <paramref name="total"/> the segment count.
    /// </summary>
    public static Color GetBarGradient(int index, int total)
    {
        double t = (double)index / Math.Max(total - 1, 1);

        if (t < 0.33)
            return Lerp(Green, Yellow, t / 0.33);
        if (t < 0.66)
            return Lerp(Yellow, Orange, (t - 0.33) / 0.33);
        return Lerp(Orange, Red, (t - 0.66) / 0.34);
    }

    /// <summary>
    /// Same gradient as <see cref="GetBarGradient"/> but returning a <see cref="System.Drawing.Color"/>
    /// for tray icon rendering. <paramref name="t"/> is 0..1 normalized position.
    /// </summary>
    public static System.Drawing.Color GetBarGradientDrawing(double t)
    {
        var dGreen  = System.Drawing.Color.FromArgb(76, 217, 100);
        var dYellow = System.Drawing.Color.FromArgb(255, 230, 50);
        var dOrange = System.Drawing.Color.FromArgb(255, 149, 0);
        var dRed    = System.Drawing.Color.FromArgb(235, 64, 52);

        if (t < 0.33)
            return LerpDrawing(dGreen, dYellow, t / 0.33);
        if (t < 0.66)
            return LerpDrawing(dYellow, dOrange, (t - 0.33) / 0.33);
        return LerpDrawing(dOrange, dRed, (t - 0.66) / 0.34);
    }
}
