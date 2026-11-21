using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland;

public enum PreviewMode { Live, Idle, Playing, Expanded, Volume, Song }

public partial class IslandWindow
{
    private static readonly Color DemoAccent = Color.FromRgb(0xFF, 0x8A, 0x65);
    private static readonly Brush DemoArt = MakeDemoArt();
    private static readonly object DemoTag = new();

    private readonly DispatcherTimer _demoTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private PreviewMode _preview = PreviewMode.Live;
    private PreviewMode _demoReturn;
    private bool _applyQueued;
    private bool _shapeHidden;

    public double HorizontalOffsetLimit =>
        Math.Max(0, Math.Floor((SystemParameters.PrimaryScreenWidth - MaxShapeWidth * _settings.Scale) / 2 - 8));

    private bool Glass => _settings.Surface == SurfaceStyle.LiquidGlass;
    private Color Good => Glass ? _textColor : Green;
    private Color Bad => Glass ? GlassBrushes.Danger : Red;
    private Color Warm => Glass ? _textColor : Amber;

    private void QueueApplySettings()
    {
        if (_applyQueued) return;
        _applyQueued = true;
        Dispatcher.InvokeAsync(() => { _applyQueued = false; ApplySettings(); }, DispatcherPriority.Normal);
    }

    private void ApplySettings()
    {
        IslandSettings s = _settings;
        Color island = ColorUtil.Parse(s.IslandColor, Colors.Black);
        Color text = ColorUtil.Parse(s.TextColor, Colors.White);
        Color outline = ColorUtil.Parse(s.OutlineColor, Colors.White);
        _textColor = text;

        bool glass = Glass;
        Shape.Background = glass ? GlassBrushes.Fill(island, s.IslandOpacity) : Frozen(ColorUtil.WithAlpha(island, s.IslandOpacity));
        Shape.BorderBrush = glass ? GlassBrushes.Rim(outline, s.OutlineOpacity) : Frozen(ColorUtil.WithAlpha(outline, s.OutlineOpacity));
        Shape.BorderThickness = new Thickness(s.OutlineOpacity > 0.001 ? s.OutlineWidth : 0);

        FontFamily mainFont = BundledFonts.Resolve(s.FontFamily);
        Resources["PrimaryBg"] = glass ? GlassBrushes.Solid : Brushes.Transparent;
        Resources["PrimaryFg"] = glass ? Brushes.Black : Frozen(text);
        Resources["ControlBg"] = glass ? GlassBrushes.Sheen : Brushes.Transparent;
        Resources["ControlRim"] = glass ? GlassBrushes.Steel : Brushes.Transparent;
        Resources["ControlRimWidth"] = new Thickness(glass ? 2 : 0);
        Resources["LabelFont"] = glass ? BundledFonts.Mono : mainFont;
        ProgressFill.Background = glass ? GlassBrushes.Done(text) : _accent;
        CpuFill.Background = glass ? GlassBrushes.Done(text) : Frozen(Color.FromRgb(0x64, 0xD2, 0xFF));
        RamFill.Background = glass ? GlassBrushes.Done(text) : Frozen(Color.FromRgb(0xBF, 0x5A, 0xF2));
        BenchCpuRing.Fill = CpuFill.Background;
        BenchRamRing.Fill = RamFill.Background;
        BenchTempRing.Fill = glass ? GlassBrushes.Done(text) : Frozen(Red);
        BenchFreqRing.Fill = glass ? GlassBrushes.Done(text) : Frozen(Amber);
        BenchGpuLine.Stroke = BenchNvidiaLine.Stroke = glass ? GlassBrushes.Done(text) : Frozen(Green);
        BenchDiskLine.Stroke = glass ? GlassBrushes.Done(text) : Frozen(Amber);
        FooterBolt.Foreground = InfoBolt.Foreground = Frozen(Good);
        FontWeight = glass ? FontWeights.SemiBold : FontWeights.Medium;
        Resources["IslandText"] = Frozen(text);
        Resources["SubtleText"] = Frozen(ColorUtil.WithAlpha(text, 0.60));
        Resources["FaintText"] = Frozen(ColorUtil.WithAlpha(text, 0.45));
        Resources["TrackBrush"] = glass ? GlassBrushes.Remaining : Frozen(ColorUtil.WithAlpha(text, 0.20));
        Resources["HoverBrush"] = Frozen(ColorUtil.WithAlpha(text, 0.13));
        Resources["PressedBrush"] = Frozen(ColorUtil.WithAlpha(text, 0.24));
        Resources["ArtGlyphBrush"] = Frozen(ColorUtil.WithAlpha(text, 0.55));
        Resources["ArtPlaceholder"] = Frozen(ColorUtil.Blend(island, text, 0.16));
        FontFamily = mainFont;

        if (s.Glow)
        {
            _glow.BlurRadius = 10 + 26 * s.GlowStrength;
            _glow.Opacity = 0.35 + 0.55 * s.GlowStrength;
            Shape.Effect = _glow;
        }
        else
        {
            Shape.Effect = null;
        }
        SetAccent(DesiredAccent(), animate: false);

        EqBars.Visibility = s.ShowEqualizer ? Visibility.Visible : Visibility.Collapsed;
        CompactTitle.Margin = new Thickness(34, 0, s.ShowEqualizer ? 42 : 14, 0);
        if (!_inlineLevelTimer.IsEnabled) HideInlineLevel();
        if (_preview == PreviewMode.Volume) FillDemoLevel();

        ShapeScale.ScaleX = ShapeScale.ScaleY = s.Scale;
        Shape.Margin = new Thickness(0, s.TopOffset, 0, 0);
        _morph.SetMotion(s.Motion);
        _hoverIn.Interval = TimeSpan.FromMilliseconds(Math.Max(1, s.HoverDelayMs));
        ApplyShortcut();
        if (_hwnd != IntPtr.Zero) UpdateHidden();
        LayoutWindow();
        _morph.Refresh();
        MorphToView();
        if (_cpState != null) ApplyCp(_cpState);

        if (_view is View.ExpandedMedia or View.ExpandedStats) ApplyStats(_lastStats);

        if (_lastMode == null) _lastMode = s.Mode;
        if (s.Mode != _lastMode)
        {
            _lastMode = s.Mode;
            if (!_expanded && s.Mode != IslandMode.Live)
                ShowPopup(s.Mode switch { IslandMode.Clipboard => View.Clipboard, IslandMode.Notifications => View.Notifications, _ => View.Cp }, 2200);
        }
        UpdateView();
    }

    private void LayoutWindow()
    {
        double pad = 24 + (_settings.Glow ? 44 : 0);
        Width = Math.Ceiling(MaxShapeWidth * _settings.Scale + pad * 2);
        Height = Math.Ceiling(_settings.TopOffset + OpenDrop + MaxShapeHeight * _settings.Scale + pad);
        PositionWindow();
    }

    private void PositionWindow()
    {
        if (_settings.Dragged)
        {
            Point spot = ClampDragged(_settings.DragX, _settings.DragY);
            PlaceWindow(spot.X, spot.Y);
            return;
        }
        double limit = HorizontalOffsetLimit;
        double offset = Math.Clamp(_settings.HorizontalOffset, -limit, limit);
        PlaceWindow(SystemParameters.PrimaryScreenWidth / 2 + offset, 0);
    }

    private Color DesiredAccent()
    {
        if (_settings.AccentSource == AccentSource.Custom) return ColorUtil.Parse(_settings.AccentColor, Colors.DodgerBlue);
        if (_nowPlaying != null) return _nowPlaying.Accent;
        if (_preview is PreviewMode.Playing or PreviewMode.Expanded or PreviewMode.Song) return DemoAccent;
        return _lastAlbumAccent ?? _textColor;
    }

    private void SetAccent(Color color, bool animate)
    {
        if (color == _accentTarget) return;
        _accentTarget = color;
        if (animate)
        {
            var duration = TimeSpan.FromMilliseconds(450);
            _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, duration));
            _glow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(color, duration));
        }
        else
        {
            _accent.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _accent.Color = color;
            _glow.BeginAnimation(DropShadowEffect.ColorProperty, null);
            _glow.Color = color;
        }
    }

    private IslandMode? _lastMode;

    private void UpdateIdleVisibility()
    {
        bool hide = _view == View.Idle && _settings.HideWhenIdle;
        if (hide == _shapeHidden) return;
        _shapeHidden = hide;
        Shape.BeginAnimation(OpacityProperty, new DoubleAnimation(hide ? 0 : 1, TimeSpan.FromMilliseconds(hide ? 260 : 140)));
    }

    public void SetPreview(PreviewMode mode)
    {
        _demoTimer.Stop();
        ApplyPreview(mode);
    }

    public void PlayMorphDemo()
    {
        if (_demoTimer.IsEnabled) return;
        _demoReturn = _preview;
        ApplyPreview(_preview == PreviewMode.Expanded ? PreviewMode.Idle : PreviewMode.Expanded);
        _demoTimer.Start();
    }

    private void ApplyPreview(PreviewMode mode)
    {
        if (_preview == mode) return;
        _preview = mode;
        _popupTimer.Stop();
        _popup = View.None;

        switch (mode)
        {
            case PreviewMode.Playing or PreviewMode.Expanded when _nowPlaying == null:
                FillDemoMedia();
                break;
            case PreviewMode.Song:
                if (_nowPlaying != null) FillTrackInfo(_nowPlaying);
                else FillDemoTrackInfo();
                break;
            case PreviewMode.Volume:
                FillDemoLevel();
                break;
        }
        SetAccent(DesiredAccent(), animate: true);
        UpdateView();
    }

    private void FillDemoMedia()
    {
        TitleText.Text = CompactTitle.Text = "Golden Hour";
        ArtistText.Text = "Preview Artist";
        ArtistText.Visibility = Visibility.Visible;
        SourceText.Text = "Spotify";
        ShowDemoArt(CompactArt, CompactArtGlyph);
        ShowDemoArt(ExpandedArt, ExpandedArtGlyph);
        PlayPauseButton.Content = Glyph.Pause;
        PrevButton.IsEnabled = NextButton.IsEnabled = true;
        ProgressRow.Visibility = Visibility.Visible;
        ProgressFill.BeginAnimation(WidthProperty, null);
        ProgressFill.Width = ProgressTrackWidth * 0.38;
        ElapsedText.Text = "1:24";
        RemainingText.Text = "-2:17";
    }

    private void FillDemoTrackInfo()
    {
        _peekTrackKey = null;
        InfoBattery.Visibility = Visibility.Collapsed;
        InfoArt.Visibility = Visibility.Visible;
        ShowDemoArt(InfoArt, InfoArtGlyph);
        InfoTitle.Text = "Golden Hour";
        InfoSubtitle.Text = "Preview Artist";
        InfoBolt.Visibility = Visibility.Collapsed;
        InfoTrailing.Text = "";
    }

    private void FillDemoLevel() => FillLevel(Glyph.Volume, 0.6, "60%", _textColor);

    private static void ShowDemoArt(Border frame, TextBlock placeholder)
    {
        frame.Tag = DemoTag;
        frame.Background = DemoArt;
        placeholder.Visibility = Visibility.Collapsed;
    }

    private static Brush MakeDemoArt()
    {
        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromRgb(0xFF, 0xB3, 0x47), 0),
                new(Color.FromRgb(0xFF, 0x5E, 0x62), 0.55),
                new(Color.FromRgb(0x7B, 0x3F, 0xE4), 1),
            },
            new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }
}
