using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

public sealed class ClipItem
{
    public required BitmapSource Thumb { get; init; }

    public BitmapSource? Full { get; set; }
    public byte[]? Png { get; set; }
    public string? Path { get; set; }
    public required string Source { get; set; }
    public DateTime When { get; init; } = DateTime.Now;
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }

    public bool IsVideo { get; init; }
}

public sealed class ImageFeedService : IDisposable
{
    public const int MaxItems = 5;
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".heic" };
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".wmv" };
    private static readonly TimeSpan SamePictureWindow = TimeSpan.FromSeconds(8);

    private readonly Dispatcher _dispatcher;
    private readonly HwndSource _window;
    private readonly List<ClipItem> _items = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _clipboardDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private DateTime _ignoreClipboardUntil;
    private bool _disposed;

    public event Action<bool>? Changed;

    public IReadOnlyList<ClipItem> Items => _items;

    public ImageFeedService(Dispatcher dispatcher, HwndSource window)
    {
        _dispatcher = dispatcher;
        _window = window;
        _clipboardDelay.Tick += (_, _) => { _clipboardDelay.Stop(); ReadClipboard(); };
        _window.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(_window.Handle);
        WatchFolders();
    }

    public void WatchFolders()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new (string Path, string Label, bool Deep)[]
        {
            (Path.Combine(pictures, "Screenshots"), "Screenshot", false),
            (Path.Combine(profile, "Pictures", "Screenshots"), "Screenshot", false),
            (Path.Combine(pictures, "Camera Roll"), "Photo", false),
            (Path.Combine(profile, "Pictures", "Camera Roll"), "Photo", false),
            (Path.Combine(profile, "Videos", "Captures"), "Capture", false),
            (Path.Combine(videos, "Captures"), "Capture", false),
            (Path.Combine(videos, "Screen Recordings"), "Recording", false),
            (Path.Combine(profile, "Videos", "Screen Recordings"), "Recording", false),
            (Path.Combine(profile, "Downloads", "Phone Link"), "Phone Link", true),
            (Path.Combine(profile, "Downloads", "Mobile Devices"), "Phone Link", true),
            (Path.Combine(localAppData, "Packages", "Microsoft.YourPhone_8wekyb3d8bbwe", "LocalCache", "Indexed"), "Phone Link", true),
        };

        foreach (var (path, label, deep) in candidates)
        {
            if (_watched.Contains(path) || !Directory.Exists(path)) continue;
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = deep,
                    NotifyFilter = NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                };
                watcher.Created += (_, e) => OnFile(e.FullPath, label);
                watcher.Renamed += (_, e) => OnFile(e.FullPath, label);
                _watchers.Add(watcher);
                _watched.Add(path);
            }
            catch (Exception ex)
            {
                Log.Error("images.watch", ex);
            }
        }
    }

    private void OnFile(string path, string label)
    {
        string extension = Path.GetExtension(path);
        bool video = VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        if (!video && !ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return;
        if (!_loading.TryAdd(path, 0)) return;
        Task.Run(() =>
        {
            try
            {
                ClipItem? item = video ? LoadVideo(path, label) : LoadFile(path, label);
                if (item != null) _dispatcher.BeginInvoke(() => AddFile(item));
            }
            finally
            {
                _loading.TryRemove(path, out _);
            }
        });
    }

    private static ClipItem? LoadFile(string path, string label)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                int width, height;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length == 0) throw new IOException("empty");
                    BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                }

                var thumb = new BitmapImage();
                thumb.BeginInit();
                thumb.CacheOption = BitmapCacheOption.OnLoad;
                thumb.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                thumb.DecodePixelWidth = Math.Min(220, Math.Max(1, width));
                thumb.UriSource = new Uri(path);
                thumb.EndInit();
                thumb.Freeze();
                return new ClipItem { Thumb = thumb, Path = path, Source = label, PixelWidth = width, PixelHeight = height };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileFormatException or NotSupportedException
                                           or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                Thread.Sleep(300);
            }
        }
        return null;
    }

    private ClipItem? LoadVideo(string path, string label)
    {
        DateTime giveUp = DateTime.UtcNow.AddHours(3);
        while (!_disposed && DateTime.UtcNow < giveUp)
        {
            try
            {
                if (!File.Exists(path)) return null;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    if (stream.Length == 0) throw new IOException("empty");

                BitmapSource? thumb = ShellThumbnail.Get(path, 220);
                if (thumb == null) return null;
                return new ClipItem { Thumb = thumb, Path = path, Source = label, IsVideo = true };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(1500);
            }
        }
        return null;
    }

    private void AddFile(ClipItem item)
    {
        if (_disposed || _items.Any(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase))) return;

        ClipItem? twin = _items.FirstOrDefault(i => i.Path == null && SamePicture(i, item));
        if (twin != null)
        {
            twin.Path = item.Path;
            twin.Source = item.Source;
            twin.Full = null;
            twin.Png = null;
            Changed?.Invoke(false);
            return;
        }
        Add(item);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            _clipboardDelay.Stop();
            _clipboardDelay.Start();
        }
        return IntPtr.Zero;
    }

    private void ReadClipboard()
    {
        if (_disposed || DateTime.UtcNow < _ignoreClipboardUntil) return;
        try
        {
            if (!Clipboard.ContainsImage()) return;
            BitmapSource? source = Clipboard.GetImage();
            if (source == null || source.PixelWidth < 8 || source.PixelHeight < 8) return;

            var full = new WriteableBitmap(new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0));
            full.Freeze();
            var item = new ClipItem
            {
                Thumb = MakeThumb(full),
                Full = full,
                Source = "Copied",
                PixelWidth = full.PixelWidth,
                PixelHeight = full.PixelHeight,
            };
            if (_items.Any(i => SamePicture(i, item))) return;
            Add(item);
            Compress(item);
        }
        catch (Exception ex)
        {
            if (ex is not System.Runtime.InteropServices.COMException) Log.Error("images.clipboard", ex);
        }
    }

    private void Compress(ClipItem item)
    {
        if (item.Full is not BitmapSource full) return;
        Task.Run(() =>
        {
            try
            {
                byte[] png = EncodePng(full);
                _dispatcher.BeginInvoke(() =>
                {
                    if (item.Path != null || item.Full == null) return;
                    item.Png = png;
                    item.Full = null;
                });
            }
            catch (Exception ex)
            {
                Log.Error("images.compress", ex);
            }
        });
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    private static BitmapSource? FullImage(ClipItem item)
    {
        if (item.Full != null || item.Png == null) return item.Full;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(item.Png);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource MakeThumb(BitmapSource full)
    {
        double scale = Math.Min(1.0, 220.0 / Math.Max(full.PixelWidth, full.PixelHeight));
        var thumb = new WriteableBitmap(new TransformedBitmap(full, new ScaleTransform(scale, scale)));
        thumb.Freeze();
        return thumb;
    }

    private static bool SamePicture(ClipItem a, ClipItem b) =>
        a.PixelWidth == b.PixelWidth && a.PixelHeight == b.PixelHeight && (a.When - b.When).Duration() < SamePictureWindow;

    private void Add(ClipItem item)
    {
        _items.Insert(0, item);
        while (_items.Count > MaxItems) _items.RemoveAt(_items.Count - 1);
        Changed?.Invoke(true);
    }

    public string? DragFile(ClipItem item)
    {
        try
        {
            if (item.Path != null && File.Exists(item.Path)) return item.Path;
            if (item.IsVideo || (item.Full == null && item.Png == null)) return null;
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DynamicIsland");
            Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, $"Picture {item.When:yyyy-MM-dd HHmmss}.png");
            item.Png ??= EncodePng(item.Full!);
            item.Full = null;
            if (!File.Exists(file)) File.WriteAllBytes(file, item.Png);
            item.Path = file;
            return file;
        }
        catch (Exception ex)
        {
            Log.Error("images.drag", ex);
            return null;
        }
    }

    public bool Copy(ClipItem item)
    {
        try
        {
            var data = new DataObject();
            if (item.IsVideo)
            {
                if (item.Path == null || !File.Exists(item.Path)) return false;
                data.SetFileDropList(new StringCollection { item.Path });
                _ignoreClipboardUntil = DateTime.UtcNow.AddSeconds(1.5);
                Clipboard.SetDataObject(data, true);
                return true;
            }
            BitmapSource? image = FullImage(item);
            if (image == null && item.Path != null && File.Exists(item.Path))
            {
                var loaded = new BitmapImage();
                loaded.BeginInit();
                loaded.CacheOption = BitmapCacheOption.OnLoad;
                loaded.UriSource = new Uri(item.Path);
                loaded.EndInit();
                loaded.Freeze();
                image = loaded;
                data.SetFileDropList(new StringCollection { item.Path });
            }
            if (image == null) return false;
            data.SetImage(image);
            _ignoreClipboardUntil = DateTime.UtcNow.AddSeconds(1.5);
            Clipboard.SetDataObject(data, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("images.copy", ex);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clipboardDelay.Stop();
        NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _window.RemoveHook(WndProc);
        foreach (FileSystemWatcher watcher in _watchers) watcher.Dispose();
        _items.Clear();
    }
}
