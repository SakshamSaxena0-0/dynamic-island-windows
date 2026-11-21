using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.UI;
using Microsoft.Win32;

namespace DynamicIsland;

public partial class IslandWindow : Window
{
    private enum View { None, Idle, CompactMedia, ExpandedMedia, ExpandedStats, LevelPopup, InfoPopup, Clipboard, Cp, CpMenu, Notifications }

    private readonly record struct ShapeSize(double Width, double Height, double Radius);

    private static readonly Dictionary<View, ShapeSize> BaseSizes = new()
    {
        [View.Idle] = new(120, 32, 16),
        [View.CompactMedia] = new(236, 32, 16),
        [View.ExpandedMedia] = new(400, 186, 34),
        [View.ExpandedStats] = new(360, 124, 30),
        [View.LevelPopup] = new(300, 40, 20),
        [View.InfoPopup] = new(340, 52, 26),
        [View.Clipboard] = new(400, 116, 28),
        [View.Cp] = new(806, 130, 30),
        [View.CpMenu] = new(400, 186, 30),
        [View.Notifications] = new(400, 186, 30),
    };
    private const double MaxShapeWidth = 806, MaxShapeHeight = 186;

    private const double ProgressTrackWidth = 282, StatTrackWidth = 232, LevelTrackWidth = 194, FooterLevelTrackWidth = 150;
    private const double InfoBatteryFillMax = 19, FooterBatteryFillMax = 15;

    private static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x45, 0x3A);
    private static readonly Color Amber = Color.FromRgb(0xFF, 0xD6, 0x0A);
    private static readonly int[] EqPeriodsMs = { 420, 330, 510, 380 };

    private readonly IslandSettings _settings;
    private readonly IslandMorph _morph;
    private readonly MediaService _media;
    private readonly SystemStatsService _stats = new();
    private AudioVolumeService? _volume;
    private BrightnessService? _brightness;

    private readonly DispatcherTimer _hoverIn = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer _hoverOut = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _popupTimer = new();
    private readonly DispatcherTimer _inlineLevelTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private long _lastModeScroll;
    private readonly DispatcherTimer _housekeeping = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromSeconds(8) };

    private readonly SolidColorBrush _accent = new(Colors.White);
    private readonly DropShadowEffect _glow = new() { ShadowDepth = 0, BlurRadius = 20, Opacity = 0.6, Color = Colors.White };
    private readonly ScaleTransform[] _eqScales;
    private readonly Dictionary<View, FrameworkElement> _panels;

    private View _view = View.None;
    private View _popup = View.None;
    private bool _expanded;
    private bool _hiddenForFullscreen;
    private bool _eqRunning;
    private MediaSnapshot? _nowPlaying;
    private Color _accentTarget = Colors.White;
    private Color? _lastAlbumAccent;
    private Color _textColor = Colors.White;
    private string? _lastTrackKey;
    private string? _peekTrackKey;
    private bool _mediaSeen;
    private bool? _lastPluggedIn;
    private int _lowBatteryStage;
    private SystemStats _lastStats;
    private IntPtr _hwnd;

    public event Action? CustomizeRequested;

    public IslandWindow(IslandSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        _morph = new IslandMorph(Shape, ShapeContent);
        _panels = new()
        {
            [View.Idle] = IdlePanel,
            [View.CompactMedia] = CompactMediaPanel,
            [View.ExpandedMedia] = ExpandedMediaPanel,
            [View.ExpandedStats] = ExpandedStatsPanel,
            [View.LevelPopup] = LevelPopupPanel,
            [View.InfoPopup] = InfoPopupPanel,
            [View.Clipboard] = ClipboardPanel,
            [View.Cp] = CpPanel,
            [View.CpMenu] = CpMenuPanel,
            [View.Notifications] = NotificationsPanel,
        };
        _eqScales = EqBars.Children.OfType<Rectangle>()
            .Select(bar => { bar.Fill = _accent; return (ScaleTransform)bar.RenderTransform; })
            .ToArray();
        ProgressFill.Background = _accent;

        CompactArtGlyph.Text = ExpandedArtGlyph.Text = InfoArtGlyph.Text = Glyph.Music;
        PrevButton.Content = Glyph.Previous;
        NextButton.Content = Glyph.Next;
        PlayPauseButton.Content = Glyph.Play;
        FooterBolt.Text = InfoBolt.Text = Glyph.Bolt;

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;

        _hoverIn.Tick += (_, _) => { _hoverIn.Stop(); SetExpanded(true); };
        _hoverOut.Tick += (_, _) =>
        {
            if (_pinned || _clipDragging) { _hoverOut.Stop(); return; }
            if (CursorAboveIsland()) return;
            _hoverOut.Stop();
            SetExpanded(false);
        };
        _popupTimer.Tick += (_, _) =>
        {
            _popupTimer.Stop();
            _popup = View.None;
            UpdateView();
            if (!_expanded) { _settle.Stop(); _settle.Start(); }
        };
        _inlineLevelTimer.Tick += (_, _) => { _inlineLevelTimer.Stop(); HideInlineLevel(); };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _housekeeping.Tick += (_, _) => Housekeeping();
        _settle.Tick += (_, _) => Settle();
        _demoTimer.Tick += (_, _) => { _demoTimer.Stop(); ApplyPreview(_demoReturn); };

        Shape.MouseEnter += (_, _) =>
        {
            _hoverOut.Stop();
            if (_popup == View.Clipboard) { _popupTimer.Stop(); return; }
            if (!_expanded && _settings.OpenOnHover) _hoverIn.Start();
        };
        Shape.MouseLeave += (_, _) =>
        {
            _hoverIn.Stop();
            if (_clipDragging) return;
            if (_popup == View.Clipboard)
            {
                _popupTimer.Interval = TimeSpan.FromMilliseconds(500);
                _popupTimer.Start();
                return;
            }
            if (_expanded && !_pinned) _hoverOut.Start();
        };
        Shape.MouseLeftButtonUp += (_, _) =>
        {
            if (EndDrag()) return;
            OnIslandClicked();
        };
        HookDrag();
        Shape.MouseRightButtonUp += (_, e) => { e.Handled = true; CustomizeRequested?.Invoke(); };

        Shape.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            if (_view == View.CpMenu)
            {
                ScrollList(CpMenuScroll, e.Delta);
                return;
            }

            if (_view == View.Notifications && NotesScroll.IsMouseOver && NotesScroll.ScrollableHeight > 0)
            {
                ScrollList(NotesScroll, e.Delta);
                return;
            }

            long now = Environment.TickCount64;
            if (now - _lastModeScroll < 350) return;
            _lastModeScroll = now;
            _hoverIn.Stop();
            SetExpanded(true);

            var modes = Enum.GetValues<IslandMode>().Where(m => m != IslandMode.Live || _nowPlaying != null).ToArray();
            int i = Array.IndexOf(modes, ShownMode) + (e.Delta < 0 ? 1 : -1);
            _settings.Mode = modes[(i + modes.Length) % modes.Length];
        };

        _settings.PropertyChanged += (_, _) => QueueApplySettings();
        ApplySettings();

        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) => await StartServicesAsync();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex = (ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
        PositionWindow();
        ApplyShortcut();
    }

    private async Task StartServicesAsync()
    {
        _lastStats = _stats.Sample();
        _lastPluggedIn = _lastStats.Power.PluggedIn;
        StartImageFeed();
        UpdateView();

        try
        {
            _volume = new AudioVolumeService(Dispatcher);
            _volume.Changed += OnVolumeChanged;
        }
        catch (Exception ex)
        {
            Log.Error("audio.start", ex);
        }

        _brightness = new BrightnessService(Dispatcher);
        _brightness.Changed += OnBrightnessChanged;
        _brightness.Start();

        _housekeeping.Start();
        _settle.Start();
        await _media.StartAsync();
    }

    private void Settle()
    {
        _settle.Stop();
        if (_expanded || _popup != View.None) return;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    public void StopServices()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        foreach (var timer in new[] { _hoverIn, _hoverOut, _popupTimer, _inlineLevelTimer, _progressTimer, _housekeeping, _demoTimer, _settle })
            timer.Stop();
        _clipHintTimer.Stop();
        _images?.Dispose();
        _media.Dispose();
        _volume?.Dispose();
        _brightness?.Dispose();
    }

    private void KeepOnTop()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => { PositionWindow(); KeepOnTop(); });

    private void OnIslandClicked()
    {
        _hoverIn.Stop();
        if (!_expanded) SetExpanded(true);
        else if (!_settings.OpenOnHover) SetExpanded(false);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (expanded)
        {
            _popupTimer.Stop();
            _popup = View.None;
        }
        else
        {
            _pinned = false;
            _inlineLevelTimer.Stop();
            HideInlineLevel();
            _cpMenu = CpMenu.None;
            _cpMenuLoad++;
            _settle.Stop();
            _settle.Start();
        }

        ShapeDrop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(expanded ? OpenDrop : 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        UpdateView();
    }

    private const double OpenDrop = 14;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    private struct CursorPoint { public int X, Y; }

    private bool CursorAboveIsland()
    {
        try
        {
            if (!GetCursorPos(out CursorPoint raw)) return false;
            Point p = PointFromScreen(new Point(raw.X, raw.Y));
            Rect shape = Shape.TransformToAncestor(this).TransformBounds(new Rect(Shape.RenderSize));
            return p.X >= shape.Left && p.X <= shape.Right && p.Y >= 0 && p.Y < shape.Top;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void UpdateView()
    {
        View next = _preview switch
        {
            PreviewMode.Idle => View.Idle,
            PreviewMode.Playing => View.CompactMedia,
            PreviewMode.Expanded => View.ExpandedMedia,
            PreviewMode.Volume => View.LevelPopup,
            PreviewMode.Song => View.InfoPopup,
            _ => _popup != View.None ? _popup
                : _expanded ? ShownMode switch
                {
                    IslandMode.Clipboard => View.Clipboard,
                    IslandMode.CP => _cpMenu != CpMenu.None ? View.CpMenu : View.Cp,
                    IslandMode.Notifications => View.Notifications,
                    _ => View.ExpandedMedia,
                }

                : _settings.Mode == IslandMode.Live && _nowPlaying?.IsPlaying == true ? View.CompactMedia
                : View.Idle,
        };

        if (next != _view)
        {
            _view = next;
            MorphToView();
            foreach (var (view, panel) in _panels) Fade(panel, view == next);
            if (next is View.ExpandedMedia or View.ExpandedStats) ApplyStats(_lastStats);
            if (next == View.Idle) UpdateClock();
            if (next == View.Cp)
            {
                RefreshCp();
                ResetBench();
                ApplyBench(_lastStats);
            }
            if (next == View.Notifications)
            {
                NotesScroll.ScrollToTop();
                RefreshNotifications();
            }
        }

        _housekeeping.Interval = TimeSpan.FromSeconds(_expanded || _view is View.Cp or View.CpMenu or View.Notifications ? 1 : 2);
        UpdateIdleVisibility();
        RefreshAnimations();
    }

    private IslandMode ShownMode =>
        _settings.Mode == IslandMode.Live && _nowPlaying == null ? IslandMode.CP : _settings.Mode;

    private void UpdateClock()
    {
        string now = DateTime.Now.ToString("t");
        if (IdleClock.Text != now) IdleClock.Text = now;
    }

    private ShapeSize SizeOf(View view)
    {
        ShapeSize size = view == View.Idle ? new(Math.Max(_settings.IdleWidth, 84), 32, 16) : BaseSizes[view];
        return size with { Radius = Math.Max(3, size.Radius * _settings.Roundness) };
    }

    private void MorphToView()
    {
        if (_view == View.None) return;
        ShapeSize size = SizeOf(_view);
        KeepOnScreen(animate: true);
        _morph.MorphTo(size.Width, size.Height, size.Radius);
    }

    private void RefreshAnimations()
    {
        bool reallyPlaying = _nowPlaying?.IsPlaying == true && !_hiddenForFullscreen;
        _progressTimer.IsEnabled = reallyPlaying && _view == View.ExpandedMedia;

        bool playing = reallyPlaying || _preview == PreviewMode.Playing;
        bool eq = playing && _view == View.CompactMedia && _settings.ShowEqualizer;
        if (eq == _eqRunning) return;
        _eqRunning = eq;
        for (int i = 0; i < _eqScales.Length; i++)
        {
            if (eq)
            {
                var bounce = new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(EqPeriodsMs[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(bounce, 30);
                _eqScales[i].BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
            }
            else
            {
                _eqScales[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _eqScales[i].ScaleY = 0.35;
            }
        }
    }

    private static void Fade(FrameworkElement panel, bool show)
    {
        if (!show && panel.Visibility == Visibility.Collapsed) return;
        panel.IsHitTestVisible = show;
        if (show) panel.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(show ? 220 : 110),
            BeginTime = TimeSpan.FromMilliseconds(show ? 60 : 0),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (!show)
            fade.Completed += (_, _) => { if (!panel.IsHitTestVisible) panel.Visibility = Visibility.Collapsed; };
        panel.BeginAnimation(OpacityProperty, fade);
    }

    private bool ShowPopup(View popup, int milliseconds)
    {
        if (_expanded || _hiddenForFullscreen || _preview != PreviewMode.Live) return false;
        _popup = popup;
        _popupTimer.Stop();
        _popupTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _popupTimer.Start();
        UpdateView();
        return true;
    }

    private int PopupMs(int extra = 0) => (int)Math.Round(_settings.PopupSeconds * 1000) + extra;

    private void OnMediaChanged(MediaSnapshot? snap)
    {
        _nowPlaying = snap;
        if (snap != null)
        {
            TitleText.Text = CompactTitle.Text = snap.Title;
            ArtistText.Text = snap.Artist;
            ArtistText.Visibility = snap.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            SourceText.Text = snap.Source;
            SetArt(CompactArt, CompactArtGlyph, snap.Art);
            SetArt(ExpandedArt, ExpandedArtGlyph, snap.Art);
            PlayPauseButton.Content = snap.IsPlaying ? Glyph.Pause : Glyph.Play;
            PrevButton.IsEnabled = snap.CanPrevious;
            NextButton.IsEnabled = snap.CanNext;
            ProgressRow.Visibility = snap.Duration > TimeSpan.FromSeconds(1) ? Visibility.Visible : Visibility.Hidden;

            _lastAlbumAccent = snap.Accent;
            SetAccent(DesiredAccent(), animate: true);
            UpdateProgress();

            bool newTrack = snap.TrackKey != _lastTrackKey;
            if (newTrack && _mediaSeen && snap.IsPlaying) ShowTrackPeek(snap);
            else if (_view == View.InfoPopup && _peekTrackKey == snap.TrackKey) SetArt(InfoArt, InfoArtGlyph, snap.Art);
            _lastTrackKey = snap.TrackKey;
        }
        _mediaSeen = true;
        UpdateView();
    }

    private void ShowTrackPeek(MediaSnapshot snap)
    {
        if (!_settings.TrackPopup || _expanded || _hiddenForFullscreen || _preview != PreviewMode.Live) return;
        FillTrackInfo(snap);
        ShowPopup(View.InfoPopup, PopupMs(1100));
    }

    private void FillTrackInfo(MediaSnapshot snap)
    {
        _peekTrackKey = snap.TrackKey;
        InfoBattery.Visibility = Visibility.Collapsed;
        InfoArt.Visibility = Visibility.Visible;
        SetArt(InfoArt, InfoArtGlyph, snap.Art);
        InfoTitle.Text = snap.Title;
        InfoSubtitle.Text = snap.Artist.Length > 0 ? snap.Artist : snap.Source;
        InfoBolt.Visibility = Visibility.Collapsed;
        InfoTrailing.Text = "";
    }

    private void UpdateProgress()
    {
        var snap = _nowPlaying;
        if (snap == null || snap.Duration <= TimeSpan.FromSeconds(1)) return;
        TimeSpan position = snap.EstimatedPosition;
        ProgressFill.BeginAnimation(WidthProperty, null);
        ProgressFill.Width = ProgressTrackWidth * Math.Clamp(position.TotalSeconds / snap.Duration.TotalSeconds, 0, 1);
        ElapsedText.Text = FormatTime(position);
        RemainingText.Text = "-" + FormatTime(snap.Duration - position);
    }

    private static void SetArt(Border frame, TextBlock placeholder, BitmapSource? art)
    {
        if (art != null && ReferenceEquals(frame.Tag, art)) return;
        frame.Tag = art;
        if (art != null)
        {
            var brush = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            frame.Background = brush;
            placeholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            frame.SetResourceReference(Border.BackgroundProperty, "ArtPlaceholder");
            placeholder.Visibility = Visibility.Visible;
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_nowPlaying != null) PlayPauseButton.Content = _nowPlaying.IsPlaying ? Glyph.Play : Glyph.Pause;
        await _media.TogglePlayPauseAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();

    private async void Previous_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

    private void OnVolumeChanged(double level, bool muted)
    {
        if (!_expanded && !_settings.VolumePopup) return;
        int percent = (int)Math.Round(level * 100);
        bool silent = muted || percent == 0;
        ShowLevel(silent ? Glyph.Mute : Glyph.Volume, "Volume", muted ? 0 : level, muted ? "Off" : $"{percent}%", _textColor);
    }

    private void OnBrightnessChanged(int value)
    {
        if (!_expanded && !_settings.BrightnessPopup) return;
        ShowLevel(Glyph.Brightness, "Brightness", value / 100.0, $"{value}%", Warm);
    }

    private void ShowLevel(string glyph, string label, double fraction, string valueText, Color color)
    {
        if (_hiddenForFullscreen) return;

        if (_expanded)
        {
            FooterLevelGlyph.Text = glyph;
            FooterLevelValue.Text = valueText;
            FooterLevelFill.Background = Frozen(color);
            AnimateWidth(FooterLevelFill, FooterLevelTrackWidth * fraction);
            FooterStats.Visibility = Visibility.Collapsed;
            FooterLevel.Visibility = Visibility.Visible;
            StatsHintGlyph.Text = glyph;
            StatsHint.Text = valueText == "Off" ? $"{label} muted" : $"{label} {valueText}";
            _inlineLevelTimer.Stop();
            _inlineLevelTimer.Start();
            return;
        }

        if (_preview != PreviewMode.Live) return;
        FillLevel(glyph, fraction, valueText, color);
        ShowPopup(View.LevelPopup, PopupMs());
    }

    private void FillLevel(string glyph, double fraction, string valueText, Color color)
    {
        LevelGlyph.Text = glyph;
        LevelValue.Text = valueText;
        LevelFill.Background = Frozen(color);
        AnimateWidth(LevelFill, LevelTrackWidth * fraction);
    }

    private void HideInlineLevel()
    {
        FooterLevel.Visibility = Visibility.Collapsed;
        FooterStats.Visibility = Visibility.Visible;
        StatsHintGlyph.Text = "";
        StatsHint.Text = "Scroll to switch view";
    }

    private void Housekeeping()
    {
        _lastStats = _stats.Sample();
        if (_view is View.ExpandedMedia or View.ExpandedStats) ApplyStats(_lastStats);
        TickModes();
        if (_view == View.Cp) RefreshCp();
        if (_view == View.Notifications) RefreshNotifications();
        UpdateClock();
        CheckPower(_lastStats.Power);

        UpdateHidden();
        if (!_hiddenForFullscreen) KeepOnTop();
    }

    private void UpdateHidden()
    {
        bool fullscreen = _settings.HideInFullscreen && _hwnd != IntPtr.Zero && NativeMethods.IsFullscreenAppActive(_hwnd);
        bool hide = (fullscreen || _settings.ShortcutOnly) && !_pinned && _preview == PreviewMode.Live;
        if (hide == _hiddenForFullscreen) return;
        _hiddenForFullscreen = hide;
        if (hide)
        {
            _hoverIn.Stop();
            _hoverOut.Stop();
            _popupTimer.Stop();
            _popup = View.None;
            SetExpanded(false);
            Hide();
        }
        else
        {
            Show();
        }
        UpdateView();
    }

    private void ApplyStats(SystemStats s)
    {
        string cpu = $"{s.CpuPercent:0}%", ram = $"{s.RamPercent:0}%";
        FooterCpu.Text = CpuValue.Text = cpu;
        FooterRam.Text = RamValue.Text = ram;
        AnimateWidth(CpuFill, StatTrackWidth * s.CpuPercent / 100);
        AnimateWidth(RamFill, StatTrackWidth * s.RamPercent / 100);

        PowerInfo p = s.Power;
        Visibility batteryVisibility = p.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        FooterBattery.Visibility = batteryVisibility;
        BatteryRow.Visibility = batteryVisibility;
        if (!p.HasBattery) return;

        bool low = !p.PluggedIn && p.Percent <= 20;
        FooterBatteryFill.Background = Frozen(p.PluggedIn ? Good : low ? Bad : _textColor);
        FooterBatteryFill.Width = Math.Max(1.5, FooterBatteryFillMax * p.Percent / 100.0);
        FooterBolt.Visibility = p.PluggedIn ? Visibility.Visible : Visibility.Collapsed;
        FooterBat.Text = $"{p.Percent}%";

        BatteryCaption.Text = p.Charging ? "Charging" : "Battery";
        BatFill.Background = Frozen(low ? Bad : Good);
        AnimateWidth(BatFill, StatTrackWidth * p.Percent / 100.0);
        BatValue.Text = $"{p.Percent}%";
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
            Dispatcher.BeginInvoke(() => CheckPower(SystemStatsService.ReadPower()));
        else if (e.Mode == PowerModes.Resume)
            Dispatcher.BeginInvoke(() => { PositionWindow(); KeepOnTop(); });
    }

    private void CheckPower(PowerInfo p)
    {
        if (!p.HasBattery) return;
        bool? previous = _lastPluggedIn;
        _lastPluggedIn = p.PluggedIn;
        if (p.PluggedIn) _lowBatteryStage = 0;
        if (_expanded) return;

        if (previous is bool was && was != p.PluggedIn)
        {
            ShowPowerPopup(p, p.PluggedIn ? "Plugged in" : "On battery");
            return;
        }

        if (p.PluggedIn) return;
        if (p.Percent <= 10 && _lowBatteryStage < 2)
        {
            if (ShowPowerPopup(p, "Battery very low")) _lowBatteryStage = 2;
        }
        else if (p.Percent <= 20 && _lowBatteryStage < 1)
        {
            if (ShowPowerPopup(p, "Low battery")) _lowBatteryStage = 1;
        }
    }

    private bool ShowPowerPopup(PowerInfo p, string title)
    {
        if (!_settings.PowerPopup || _hiddenForFullscreen || _preview != PreviewMode.Live) return false;
        Color color = p.PluggedIn ? Good : p.Percent <= 20 ? Bad : _textColor;
        InfoArt.Visibility = Visibility.Collapsed;
        InfoBattery.Visibility = Visibility.Visible;
        InfoBatteryFill.Background = Frozen(color);
        InfoBatteryFill.Width = Math.Max(2, InfoBatteryFillMax * p.Percent / 100.0);
        InfoTitle.Text = title;
        InfoSubtitle.Text = p.PluggedIn
            ? (p.Charging ? "Charging" : p.Percent >= 99 ? "Fully charged" : "Not charging")
            : p.Remaining is TimeSpan left && left > TimeSpan.Zero ? $"About {FormatDuration(left)} left" : "Running on battery";
        InfoBolt.Visibility = p.PluggedIn ? Visibility.Visible : Visibility.Collapsed;
        InfoTrailing.Text = $"{p.Percent}%";
        InfoTrailing.Foreground = Frozen(color);
        return ShowPopup(View.InfoPopup, PopupMs(1300));
    }

    private static void AnimateWidth(FrameworkElement element, double to) =>
        element.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Max(0, to), TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });

    private static SolidColorBrush Frozen(Color color) => ColorUtil.Brush(color);

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{Math.Max(1, t.Minutes)} min";
}
