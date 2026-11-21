using System;
using System.Collections.Generic;

namespace DynamicIsland.Settings;

public sealed record ThemePreset(
    string Name,
    string Island, double Opacity, string Text,
    string Outline, double OutlineOpacity, double OutlineWidth,
    AccentSource Accent, string AccentColor,
    bool Glow, double GlowStrength,
    SurfaceStyle Surface = SurfaceStyle.Flat, string? Font = null, double? Roundness = null)
{
    public static readonly IReadOnlyList<ThemePreset> All = new ThemePreset[]
    {
        new("Classic",  "#000000", 1.00, "#FFFFFF", "#FFFFFF", 0.15, 1.0, AccentSource.AlbumArt, "#0A84FF", false, 0.5),
        new("Graphite", "#1C1C1E", 1.00, "#F2F2F7", "#FFFFFF", 0.10, 1.0, AccentSource.AlbumArt, "#0A84FF", false, 0.5),
        new("Glass",    "#101014", 0.55, "#FFFFFF", "#FFFFFF", 0.30, 1.0, AccentSource.AlbumArt, "#0A84FF", false, 0.5),
        new("Light",    "#F5F5F7", 1.00, "#1C1C1E", "#000000", 0.12, 1.0, AccentSource.Custom,   "#0A84FF", false, 0.5),
        new("Midnight", "#0B1022", 1.00, "#E6ECFF", "#6B8CFF", 0.40, 1.0, AccentSource.Custom,   "#7AA2FF", true,  0.45),
        new("Neon",     "#07070A", 1.00, "#FFFFFF", "#FF2BD6", 0.70, 1.5, AccentSource.Custom,   "#FF2BD6", true,  0.8),
        new("Sunset",   "#1E0C08", 1.00, "#FFE9DC", "#FF8A50", 0.40, 1.0, AccentSource.Custom,   "#FF8A50", true,  0.4),
        new("Forest",   "#0C1A12", 1.00, "#E3F5E9", "#34C759", 0.35, 1.0, AccentSource.Custom,   "#34C759", false, 0.5),

        new("Liquid Glass", "#070707", 0.96, "#ECECE8", "#FFFFFF", 0.00, 1.0, AccentSource.Custom, "#ECECE8", false, 0.5,
            SurfaceStyle.LiquidGlass, Font: "Manrope", Roundness: 0.55),
    };

    public void ApplyTo(IslandSettings s)
    {
        var defaults = new IslandSettings();
        s.IslandColor = Island;
        s.IslandOpacity = Opacity;
        s.TextColor = Text;
        s.OutlineColor = Outline;
        s.OutlineOpacity = OutlineOpacity;
        s.OutlineWidth = OutlineWidth;
        s.AccentSource = Accent;
        s.AccentColor = AccentColor;
        s.Glow = Glow;
        s.GlowStrength = GlowStrength;
        s.Surface = Surface;

        foreach (ThemePreset other in All)
        {
            if (Font == null && other.Font != null && s.FontFamily == other.Font) s.FontFamily = defaults.FontFamily;
            if (Roundness == null && other.Roundness is double r && Math.Abs(s.Roundness - r) < 0.001) s.Roundness = defaults.Roundness;
        }
        if (Font != null) s.FontFamily = Font;
        if (Roundness is double roundness) s.Roundness = roundness;
        s.Theme = Name;
    }
}
