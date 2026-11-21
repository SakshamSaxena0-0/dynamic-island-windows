using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Source,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionUpdatedAt,
    BitmapSource? Art,
    Color Accent,
    bool CanPrevious,
    bool CanNext)
{
    public string TrackKey => Title + "\u0001" + Artist;

    public TimeSpan EstimatedPosition
    {
        get
        {
            if (Duration <= TimeSpan.Zero) return TimeSpan.Zero;
            TimeSpan p = Position;
            if (IsPlaying && PositionUpdatedAt != default) p += DateTimeOffset.Now - PositionUpdatedAt;
            if (p < TimeSpan.Zero) return TimeSpan.Zero;
            return p > Duration ? Duration : p;
        }
    }
}

public sealed class MediaService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _debounce;
    private readonly List<GlobalSystemMediaTransportControlsSession> _watched = new();

    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlayback;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onProperties;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _active;
    private bool _sessionsDirty = true;
    private bool _artDirty = true;
    private bool _refreshing, _refreshAgain;
    private string? _artKey;
    private BitmapSource? _art;
    private Color _accent = Colors.White;

    public event Action<MediaSnapshot?>? Changed;

    public MediaService(Dispatcher ui)
    {
        _ui = ui;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshAsync();
        };

        _onPlayback = (_, _) => Schedule();
        _onTimeline = (_, _) => Schedule();
        _onProperties = (_, _) => { _artDirty = true; Schedule(); };
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            Log.Error("media.start", ex);
            return;
        }

        _manager.SessionsChanged += (_, _) => { _sessionsDirty = true; Schedule(); };
        _manager.CurrentSessionChanged += (_, _) => { _artDirty = true; Schedule(); };
        Schedule();
    }

    public Task TogglePlayPauseAsync() => Send(s => s.TryTogglePlayPauseAsync());
    public Task NextAsync() => Send(s => s.TrySkipNextAsync());
    public Task PreviousAsync() => Send(s => s.TrySkipPreviousAsync());

    private async Task Send(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        var session = _active;
        if (session == null) return;
        try { await command(session); }
        catch (Exception ex) { Log.Error("media.command", ex); }
    }

    private void Schedule() => _ui.InvokeAsync(() =>
    {
        _debounce.Stop();
        _debounce.Start();
    });

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                var snapshot = await ReadAsync();
                Changed?.Invoke(snapshot);
            } while (_refreshAgain);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task<MediaSnapshot?> ReadAsync()
    {
        if (_manager == null) return null;
        try
        {
            if (_sessionsDirty) Rewatch();

            var session = PickSession();
            if (!ReferenceEquals(session, _active)) _artDirty = true;
            _active = session;
            if (session == null) return null;

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null || string.IsNullOrWhiteSpace(props.Title)) return null;

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            string key = session.SourceAppUserModelId + "|" + props.Title + "|" + props.Artist;
            if (_artDirty || key != _artKey)
            {
                _artDirty = false;
                _artKey = key;
                (_art, _accent) = await LoadArtAsync(props.Thumbnail);
            }

            string artist = (props.Artist ?? "").Trim();
            if (artist.Length == 0) artist = (props.AlbumArtist ?? "").Trim();

            var controls = playback?.Controls;
            return new MediaSnapshot(
                Title: props.Title.Trim(),
                Artist: artist,
                Source: FriendlySource(session.SourceAppUserModelId),
                IsPlaying: playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Position: timeline.Position - timeline.StartTime,
                Duration: timeline.EndTime - timeline.StartTime,
                PositionUpdatedAt: timeline.LastUpdatedTime,
                Art: _art,
                Accent: _accent,
                CanPrevious: controls?.IsPreviousEnabled ?? true,
                CanNext: controls?.IsNextEnabled ?? true);
        }
        catch (Exception ex)
        {
            Log.Error("media.read", ex);
            return null;
        }
    }

    private void Rewatch()
    {
        _sessionsDirty = false;
        Unwatch();
        foreach (var session in _manager!.GetSessions())
        {
            session.PlaybackInfoChanged += _onPlayback;
            session.MediaPropertiesChanged += _onProperties;
            session.TimelinePropertiesChanged += _onTimeline;
            _watched.Add(session);
        }
    }

    private void Unwatch()
    {
        foreach (var session in _watched)
        {
            try
            {
                session.PlaybackInfoChanged -= _onPlayback;
                session.MediaPropertiesChanged -= _onProperties;
                session.TimelinePropertiesChanged -= _onTimeline;
            }
            catch
            {
            }
        }
        _watched.Clear();
    }

    private GlobalSystemMediaTransportControlsSession? PickSession()
    {
        var current = _manager!.GetCurrentSession();
        if (IsPlaying(current)) return current;
        foreach (var session in _watched)
            if (IsPlaying(session)) return session;
        return current ?? (_watched.Count > 0 ? _watched[0] : null);
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession? session)
    {
        try
        {
            return session?.GetPlaybackInfo()?.PlaybackStatus ==
                   GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(BitmapSource?, Color)> LoadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null) return (null, Colors.White);
        try
        {
            using var winrtStream = await thumbnail.OpenReadAsync();
            using var input = winrtStream.AsStreamForRead();
            var buffer = new MemoryStream();
            await input.CopyToAsync(buffer);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return (image, AccentFrom(image));
        }
        catch (Exception ex)
        {
            Log.Error("media.art", ex);
            return (null, Colors.White);
        }
    }

    private static Color AccentFrom(BitmapSource source)
    {
        var small = new FormatConvertedBitmap(
            new TransformedBitmap(source, new ScaleTransform(12.0 / source.PixelWidth, 12.0 / source.PixelHeight)),
            PixelFormats.Bgra32, null, 0);
        int w = small.PixelWidth, h = small.PixelHeight;
        var px = new byte[w * h * 4];
        small.CopyPixels(px, w * 4, 0);

        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            double pb = px[i] / 255.0, pg = px[i + 1] / 255.0, pr = px[i + 2] / 255.0;
            double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
            double saturation = max <= 0 ? 0 : (max - min) / max;
            double weight = 0.05 + saturation * saturation * max;
            r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
        }
        r /= total; g /= total; b /= total;

        double peak = Math.Max(r, Math.Max(g, b));
        if (peak < 0.78)
        {
            double k = 0.78 / Math.Max(peak, 0.01);
            r *= k; g *= k; b *= k;
        }
        return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));

        static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
    }

    private static string FriendlySource(string? appId)
    {
        if (string.IsNullOrEmpty(appId)) return "";
        string id = appId.ToLowerInvariant();
        if (id.Contains("spotify")) return "Spotify";
        if (id.Contains("chrome")) return "Chrome";
        if (id.Contains("msedge") || id.Contains("microsoftedge")) return "Edge";
        if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) return "Firefox";
        if (id.Contains("brave")) return "Brave";
        if (id.Contains("opera")) return "Opera";
        if (id.Contains("vlc")) return "VLC";
        if (id.Contains("zunemusic")) return "Media Player";
        if (id.Contains("applemusic")) return "Apple Music";

        string name = appId;
        int bang = name.IndexOf('!');
        if (bang > 0) name = name[..bang];
        name = Path.GetFileNameWithoutExtension(name);
        int underscore = name.IndexOf('_');
        if (underscore > 0) name = name[..underscore];
        int dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        return name;
    }

    public void Dispose()
    {
        _debounce.Stop();
        Unwatch();
    }
}
