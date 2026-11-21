using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using DynamicIsland.UI;

namespace DynamicIsland.Settings;

public enum AccentSource { AlbumArt, Custom }

public enum SurfaceStyle { Flat, LiquidGlass }

public enum IslandMode { Live, Clipboard, CP, Notifications }

public enum MotionStyle { Snappy, Smooth, Bouncy, Gentle, Off }

public sealed class IslandSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public static readonly IReadOnlySet<string> ThemeProperties = new HashSet<string>
    {
        nameof(IslandColor), nameof(IslandOpacity), nameof(TextColor), nameof(AccentSource), nameof(AccentColor),
        nameof(OutlineColor), nameof(OutlineOpacity), nameof(OutlineWidth), nameof(Glow), nameof(GlowStrength),
        nameof(Surface),
    };

    private IslandMode _mode = IslandMode.Live;
    public IslandMode Mode { get => _mode; set => Set(ref _mode, value); }

    private string _theme = "Classic";
    public string Theme { get => _theme; set => Set(ref _theme, value); }

    private SurfaceStyle _surface = SurfaceStyle.Flat;
    public SurfaceStyle Surface { get => _surface; set => Set(ref _surface, value); }

    private string _islandColor = "#000000";
    public string IslandColor { get => _islandColor; set => Set(ref _islandColor, value); }

    private double _islandOpacity = 1.0;
    public double IslandOpacity { get => _islandOpacity; set => Set(ref _islandOpacity, value); }

    private string _textColor = "#FFFFFF";
    public string TextColor { get => _textColor; set => Set(ref _textColor, value); }

    private AccentSource _accentSource = AccentSource.AlbumArt;
    public AccentSource AccentSource { get => _accentSource; set => Set(ref _accentSource, value); }

    private string _accentColor = "#0A84FF";
    public string AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }

    private string _outlineColor = "#FFFFFF";
    public string OutlineColor { get => _outlineColor; set => Set(ref _outlineColor, value); }

    private double _outlineOpacity = 0.15;
    public double OutlineOpacity { get => _outlineOpacity; set => Set(ref _outlineOpacity, value); }

    private double _outlineWidth = 1;
    public double OutlineWidth { get => _outlineWidth; set => Set(ref _outlineWidth, value); }

    private bool _glow;
    public bool Glow { get => _glow; set => Set(ref _glow, value); }

    private double _glowStrength = 0.5;
    public double GlowStrength { get => _glowStrength; set => Set(ref _glowStrength, value); }

    private double _scale = 1.0;
    public double Scale { get => _scale; set => Set(ref _scale, value); }

    private double _roundness = 1.0;
    public double Roundness { get => _roundness; set => Set(ref _roundness, value); }

    private double _idleWidth = 120;
    public double IdleWidth { get => _idleWidth; set => Set(ref _idleWidth, value); }

    private bool _hideWhenIdle;
    public bool HideWhenIdle { get => _hideWhenIdle; set => Set(ref _hideWhenIdle, value); }

    private string _fontFamily = "Segoe UI Variable Text";
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }

    private bool _showEqualizer = true;
    public bool ShowEqualizer { get => _showEqualizer; set => Set(ref _showEqualizer, value); }

    private double _topOffset = 6;
    public double TopOffset { get => _topOffset; set => Set(ref _topOffset, value); }

    private double _horizontalOffset;
    public double HorizontalOffset { get => _horizontalOffset; set => Set(ref _horizontalOffset, value); }

    private bool _dragged;

    public bool Dragged { get => _dragged; set => Set(ref _dragged, value); }

    private double _dragX;

    public double DragX { get => _dragX; set => Set(ref _dragX, value); }

    private double _dragY;
    public double DragY { get => _dragY; set => Set(ref _dragY, value); }

    private MotionStyle _motion = MotionStyle.Smooth;
    public MotionStyle Motion { get => _motion; set => Set(ref _motion, value); }

    private bool _openOnHover = true;
    public bool OpenOnHover
    {
        get => _openOnHover;
        set
        {
            if (_openOnHover == value) return;
            Set(ref _openOnHover, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpenByClick)));
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool OpenByClick { get => !OpenOnHover; set => OpenOnHover = !value; }

    private double _hoverDelayMs = 140;
    public double HoverDelayMs { get => _hoverDelayMs; set => Set(ref _hoverDelayMs, value); }

    private bool _hideInFullscreen = true;
    public bool HideInFullscreen { get => _hideInFullscreen; set => Set(ref _hideInFullscreen, value); }

    private string _shortcut = "Ctrl+Alt+F12";

    public string Shortcut { get => _shortcut; set => Set(ref _shortcut, value ?? ""); }

    private bool _shortcutOnly;

    public bool ShortcutOnly { get => _shortcutOnly; set => Set(ref _shortcutOnly, value); }

    private bool _trackPopup = true;
    public bool TrackPopup { get => _trackPopup; set => Set(ref _trackPopup, value); }

    private bool _volumePopup = true;
    public bool VolumePopup { get => _volumePopup; set => Set(ref _volumePopup, value); }

    private bool _brightnessPopup = true;
    public bool BrightnessPopup { get => _brightnessPopup; set => Set(ref _brightnessPopup, value); }

    private bool _imagePopup = true;
    public bool ImagePopup { get => _imagePopup; set => Set(ref _imagePopup, value); }

    private bool _powerPopup = true;
    public bool PowerPopup { get => _powerPopup; set => Set(ref _powerPopup, value); }

    private double _popupSeconds = 1.5;
    public double PopupSeconds { get => _popupSeconds; set => Set(ref _popupSeconds, value); }

    public void CopyFrom(IslandSettings other)
    {
        foreach (PropertyInfo p in typeof(IslandSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.CanWrite) p.SetValue(this, p.GetValue(other));
    }

    public void Normalize()
    {
        var d = new IslandSettings();
        Theme = string.IsNullOrWhiteSpace(Theme) ? d.Theme : Theme;
        IslandColor = ValidColor(IslandColor, d.IslandColor);
        TextColor = ValidColor(TextColor, d.TextColor);
        AccentColor = ValidColor(AccentColor, d.AccentColor);
        OutlineColor = ValidColor(OutlineColor, d.OutlineColor);
        if (!Enum.IsDefined(AccentSource)) AccentSource = d.AccentSource;
        if (!Enum.IsDefined(Motion)) Motion = d.Motion;
        if (!Enum.IsDefined(Surface)) Surface = d.Surface;
        if (!Enum.IsDefined(Mode)) Mode = d.Mode;
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? d.FontFamily : FontFamily.Trim();

        IslandOpacity = Clamp(IslandOpacity, 0.2, 1, d.IslandOpacity);
        OutlineOpacity = Clamp(OutlineOpacity, 0, 1, d.OutlineOpacity);
        OutlineWidth = Clamp(OutlineWidth, 0.5, 3, d.OutlineWidth);
        GlowStrength = Clamp(GlowStrength, 0.1, 1, d.GlowStrength);
        Scale = Clamp(Scale, 0.8, 1.5, d.Scale);
        Roundness = Clamp(Roundness, 0, 1, d.Roundness);
        IdleWidth = Clamp(IdleWidth, 40, 240, d.IdleWidth);
        TopOffset = Clamp(TopOffset, 0, 48, d.TopOffset);
        HorizontalOffset = Clamp(HorizontalOffset, -4000, 4000, d.HorizontalOffset);
        HoverDelayMs = Clamp(HoverDelayMs, 0, 800, d.HoverDelayMs);
        PopupSeconds = Clamp(PopupSeconds, 0.8, 5, d.PopupSeconds);
    }

    private static string ValidColor(string? value, string fallback) =>
        ColorUtil.TryParse(value, out var c) ? ColorUtil.ToHex(c) : fallback;

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
