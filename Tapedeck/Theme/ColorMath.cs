using System;
using Avalonia.Media;

namespace Tapedeck.Theme;

/// <summary>Color helpers for deriving theme colors (WCAG contrast, alpha, mixing).</summary>
public static class ColorMath
{
    public static Color WithAlpha(this Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>Linear interpolation towards <paramref name="target"/>; <paramref name="amount"/> 0 keeps the color.</summary>
    public static Color Mix(this Color color, Color target, double amount)
    {
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(color.A, Lerp(color.R, target.R), Lerp(color.G, target.G), Lerp(color.B, target.B));
    }

    /// <summary>WCAG 2 contrast ratio between two opaque colors, from 1 to 21.</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
