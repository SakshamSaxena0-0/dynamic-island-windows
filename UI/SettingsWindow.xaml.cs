using System;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Settings;

namespace DynamicIsland.UI;

public partial class SettingsWindow : Window
{
    public sealed record Option<T>(T Value, string Label);

    private static readonly Option<MotionStyle>[] MotionOptions =
    {
        new(MotionStyle.Snappy, "Snappy"),
        new(MotionStyle.Smooth, "Smooth (default)"),
        new(MotionStyle.Bouncy, "Bouncy"),
        new(MotionStyle.Gentle, "Gentle"),
        new(MotionStyle.Off, "No animation"),
    };

    private readonly SettingsStore _store;
    private readonly IslandSettings _settings;
    private readonly IslandWindow _island;
    private readonly List<PresetCard> _cards;
    private bool _applyingPreset;

    public SettingsWindow(SettingsStore store, IslandWindow island)
    {
        _store = store;
        _settings = store.Settings;
        _island = island;
        InitializeComponent();

        Resources["UiFont"] = BundledFonts.Manrope;
        Resources["MonoFont"] = BundledFonts.Mono;
        ApplyLook();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        DataContext = _settings;

        _cards = ThemePreset.All.Select(p => new PresetCard(p)).ToList();
        PresetList.ItemsSource = _cards;
        MotionBox.ItemsSource = MotionOptions;
        FontBox.ItemsSource = InstalledFonts(_settings.FontFamily);
        StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
        UpdateHorizontalRange();
        SyncThemeCards();

        _settings.PropertyChanged += OnSettingChanged;
        Closed += (_, _) =>
        {
            _settings.PropertyChanged -= OnSettingChanged;
            _island.SetPreview(PreviewMode.Live);
            _store.Flush();
        };
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        string name = e.PropertyName ?? "";
        if (!_applyingPreset && IslandSettings.ThemeProperties.Contains(name)) _settings.Theme = "Custom";
        if (name == nameof(IslandSettings.Theme)) SyncThemeCards();
        if (name == nameof(IslandSettings.Scale)) UpdateHorizontalRange();
    }

    private void SyncThemeCards()
    {
        foreach (PresetCard card in _cards) card.IsSelected = card.Name == _settings.Theme;
        CustomThemeNote.Visibility = _cards.Any(c => c.IsSelected) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateHorizontalRange()
    {
        double limit = _island.HorizontalOffsetLimit;
        HorizontalSlider.Minimum = -limit;
        HorizontalSlider.Maximum = limit;
    }

    private static List<string> InstalledFonts(string current)
    {
        var names = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !n.Contains("Icons") && !n.Contains("MDL2") && !n.Contains("Emoji")
                && !n.Contains("Marlett") && !n.Contains("dings") && !n.Contains("Symbol") && !n.Contains("Assets"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (string preferred in new[] { "Segoe UI Variable Text", "Segoe UI Variable Display", BundledFonts.ManropeName, BundledFonts.MonoName, current })
            if (!names.Contains(preferred, StringComparer.OrdinalIgnoreCase)) names.Add(preferred);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private void ApplyLook()
    {
#pragma warning disable WPF0001
        ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
        var look = (ResourceDictionary)Application.LoadComponent(
            new Uri("/DynamicIsland;component/UI/Themes/SettingsLiquidGlass.xaml", UriKind.Relative));
        foreach (DictionaryEntry entry in look) Resources[entry.Key] = entry.Value;

        Background = ColorUtil.Brush(Color.FromRgb(0x07, 0x07, 0x07));
        Foreground = ColorUtil.Brush(GlassBrushes.Bone);
        FontFamily = BundledFonts.Manrope;
        FontWeight = FontWeights.Medium;
        Grain.Fill = MakeGrain();
        Grain.Visibility = Visibility.Visible;
    }

    private static Brush MakeGrain()
    {
        const int size = 180;
        var pixels = new byte[size * size * 4];
        var random = new Random(7);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
            pixels[i + 3] = (byte)random.Next(256);
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PresetCard card) return;
        _applyingPreset = true;
        try { card.Preset.ApplyTo(_settings); }
        finally { _applyingPreset = false; }
        SyncThemeCards();
    }

    private void Center_Click(object sender, RoutedEventArgs e)
    {
        _settings.Dragged = false;
        _settings.HorizontalOffset = 0;
    }

    private void PlayMotion_Click(object sender, RoutedEventArgs e) => _island.PlayMorphDemo();

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        StartupRegistration.SetEnabled(StartupSwitch.IsChecked == true);
        StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Go back to the default look and behaviour?", "Reset everything",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _applyingPreset = true;
        try { _settings.CopyFrom(new IslandSettings()); }
        finally { _applyingPreset = false; }
        SyncThemeCards();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
