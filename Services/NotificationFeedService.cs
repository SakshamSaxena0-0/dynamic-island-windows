using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Services;

public sealed record NoteItem(uint Id, string App, string AppId, string Title, string Body, DateTimeOffset When, BitmapSource? Logo);

public sealed class NotificationFeedService
{
    private readonly UserNotificationListener _listener = UserNotificationListener.Current;
    private readonly Dictionary<string, BitmapSource?> _logos = new();
    private bool? _allowed;

    public bool Allowed => _allowed != false;

    public async Task<IReadOnlyList<NoteItem>?> ReadAsync()
    {
        try
        {
            if (_allowed != true)
            {
                _allowed = await _listener.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed;
                if (_allowed != true) return null;
            }
            var raw = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var items = new List<NoteItem>();
            foreach (UserNotification n in raw.OrderByDescending(n => n.CreationTime))
            {
                var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?
                    .GetTextElements().Select(t => t.Text?.Trim() ?? "").Where(t => t.Length > 0).ToList() ?? new();
                string app = "", appId = "";
                try
                {
                    app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                    appId = n.AppInfo?.AppUserModelId ?? app;
                }
                catch {   }
                items.Add(new NoteItem(n.Id, app.Length > 0 ? app : "Notification", appId,
                    texts.ElementAtOrDefault(0) ?? "", string.Join(" · ", texts.Skip(1)), n.CreationTime,
                    await LogoAsync(n, appId)));
            }
            return items;
        }
        catch (Exception ex)
        {
            Log.Error("notes.read", ex);
            return null;
        }
    }

    private async Task<BitmapSource?> LogoAsync(UserNotification n, string appId)
    {
        if (_logos.TryGetValue(appId, out BitmapSource? cached)) return cached;
        BitmapSource? logo = null;
        try
        {
            var reference = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
            if (reference != null)
            {
                using var stream = await reference.OpenReadAsync();
                using Stream read = stream.AsStreamForRead();
                var memory = new MemoryStream();
                await read.CopyToAsync(memory);
                memory.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 48;
                bitmap.StreamSource = memory;
                bitmap.EndInit();
                logo = Trim(bitmap);
                logo.Freeze();
            }
        }
        catch {   }
        _logos[appId] = logo;
        return logo;
    }

    private static BitmapSource Trim(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (pixels[(y * w + x) * 4 + 3] > 16)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        if (right < 0 || (right - left + 1 >= w - 2 && bottom - top + 1 >= h - 2)) return source;
        return new CroppedBitmap(source, new System.Windows.Int32Rect(left, top, right - left + 1, bottom - top + 1));
    }

    private HashSet<string>? _apps;

    private static HashSet<string> StartApps()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Type? type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return set;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic items = shell.NameSpace("shell:AppsFolder").Items();
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                string? path = items.Item(i)?.Path;
                if (!string.IsNullOrEmpty(path)) set.Add(path);
            }
        }
        catch (Exception ex)
        {
            Log.Error("notes.apps", ex);
        }
        return set;
    }

    public bool Open(NoteItem item)
    {
        try
        {
            if (item.AppId.Length > 0)
            {
                _apps ??= StartApps();
                if (!_apps.Contains(item.AppId)) _apps = StartApps();
                if (_apps.Contains(item.AppId))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{item.AppId}") { UseShellExecute = true });
                    Remove(item.Id);
                    return true;
                }
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-actioncenter:") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("notes.open", ex);
        }
        return false;
    }

    public void Remove(uint id)
    {
        try { _listener.RemoveNotification(id); }
        catch (Exception ex) { Log.Error("notes.remove", ex); }
    }

    public async Task ClearAllAsync()
    {
        try
        {
            foreach (UserNotification n in await _listener.GetNotificationsAsync(NotificationKinds.Toast))
            {
                try { _listener.RemoveNotification(n.Id); }
                catch (Exception ex) { Log.Error("notes.clear", ex); }
            }
        }
        catch (Exception ex)
        {
            Log.Error("notes.clear", ex);
        }
    }
}
