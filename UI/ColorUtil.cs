using System;
using System.Windows.Media;

namespace DynamicIsland.UI;

public static class ColorUtil
{
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim();
        if (s[0] != '#' && s.Length is 3 or 6 or 8 && IsHex(s)) s = "#" + s;
        try
        {
            if (ColorConverter.ConvertFromString(s) is Color c)
            {
                color = Color.FromRgb(c.R, c.G, c.B);
                return true;
            }
        }
        catch (FormatException) { }
        catch (NotSupportedException) { }
        return false;
    }

    public static Color Parse(string? text, Color fallback) => TryParse(text, out var c) ? c : fallback;

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    public static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    public static SolidColorBrush Brush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static bool IsHex(string s)
    {
        foreach (char ch in s)
            if (!Uri.IsHexDigit(ch)) return false;
        return true;
    }
}
