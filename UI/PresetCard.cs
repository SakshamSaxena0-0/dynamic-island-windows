using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DynamicIsland.Settings;

namespace DynamicIsland.UI;

public sealed class PresetCard : INotifyPropertyChanged
{
    private static readonly Color SampleAlbumAccent = Color.FromRgb(0xFF, 0x9F, 0x6B);
    private bool _isSelected;

    public PresetCard(ThemePreset preset)
    {
        Preset = preset;
        Color island = ColorUtil.Parse(preset.Island, Colors.Black);
        Color text = ColorUtil.Parse(preset.Text, Colors.White);
        Color accent = preset.Accent == AccentSource.Custom ? ColorUtil.Parse(preset.AccentColor, Colors.DodgerBlue) : SampleAlbumAccent;

        Color outline = ColorUtil.Parse(preset.Outline, Colors.White);
        bool glass = preset.Surface == SurfaceStyle.LiquidGlass;
        IslandBrush = glass ? GlassBrushes.Fill(island, preset.Opacity) : ColorUtil.Brush(ColorUtil.WithAlpha(island, preset.Opacity));
        OutlineBrush = glass ? GlassBrushes.Rim(outline, preset.OutlineOpacity) : ColorUtil.Brush(ColorUtil.WithAlpha(outline, preset.OutlineOpacity));
        OutlineThickness = new Thickness(preset.OutlineWidth);
        TextBrush = ColorUtil.Brush(text);
        AccentBrush = ColorUtil.Brush(accent);
        ArtBrush = preset.Accent == AccentSource.Custom
            ? ColorUtil.Brush(ColorUtil.Blend(island, accent, 0.55))
            : Frozen(new LinearGradientBrush(Color.FromRgb(0xFF, 0xB3, 0x47), Color.FromRgb(0x7B, 0x3F, 0xE4), 45));
        if (preset.Glow)
        {
            var glow = new DropShadowEffect { ShadowDepth = 0, Color = accent, BlurRadius = 8 + 14 * preset.GlowStrength, Opacity = 0.4 + 0.5 * preset.GlowStrength };
            glow.Freeze();
            Glow = glow;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThemePreset Preset { get; }
    public string Name => Preset.Name;
    public Brush IslandBrush { get; }
    public Brush OutlineBrush { get; }
    public Thickness OutlineThickness { get; }
    public Brush TextBrush { get; }
    public Brush AccentBrush { get; }
    public Brush ArtBrush { get; }
    public Effect? Glow { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
