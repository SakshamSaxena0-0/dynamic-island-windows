using System;
using System.Windows.Media;

namespace DynamicIsland.UI;

public static class BundledFonts
{
    public const string ManropeName = "Manrope";
    public const string MonoName = "JetBrains Mono";

    private static readonly Uri Root = new("pack://application:,,,/Assets/Fonts/");

    public static readonly FontFamily Manrope = new(Root, "./#Manrope ExtraLight, Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new(Root, "./#JetBrains Mono, Cascadia Mono, Consolas");

    public static FontFamily Resolve(string name) => name.Trim() switch
    {
        ManropeName => Manrope,
        MonoName => Mono,
        _ => new FontFamily($"{name}, Segoe UI Variable Text, Segoe UI"),
    };
}
