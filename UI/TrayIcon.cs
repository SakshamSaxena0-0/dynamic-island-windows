using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.UI;

public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 0x51;
    private const int CmdCustomize = 1, CmdStartup = 2, CmdExit = 3;

    private readonly Action _customize;
    private readonly Action _exit;
    private readonly HwndSource _window;
    private readonly int _taskbarCreated;
    private readonly IntPtr _icon;
    private bool _disposed;

    public TrayIcon(Action customize, Action exit)
    {
        _customize = customize;
        _exit = exit;
        _icon = DrawIcon();

        _window = new HwndSource(new HwndSourceParameters("Dynamic Island tray") { WindowStyle = 0, ExtendedWindowStyle = 0x80   });
        _window.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        Show(NimAdd);
    }

    private void Show(int action)
    {
        var data = new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            Window = _window.Handle,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = CallbackMessage,
            Icon = _icon,
            Tip = "Dynamic Island",
        };
        if (!Shell_NotifyIcon(action, ref data) && action == NimAdd)
            Log.Error("tray", new InvalidOperationException("Windows did not accept the tray icon"));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreated && !_disposed)
        {
            Show(NimAdd);
        }
        else if (msg == CallbackMessage)
        {
            switch ((int)lParam)
            {
                case 0x0203: _customize(); break;
                case 0x0205: ShowMenu(); break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MfString, CmdCustomize, "Customize…");
            AppendMenu(menu, MfSeparator, 0, null);

            AppendMenu(menu, MfString | (StartupRegistration.IsEnabled ? MfChecked : 0), CmdStartup, "Start with Windows");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, CmdExit, "Exit");
            SetMenuDefaultItem(menu, CmdCustomize, 0);

            GetCursorPos(out CursorPoint at);
            SetForegroundWindow(_window.Handle);
            int command = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton | TpmNoNotify, at.X, at.Y, _window.Handle, IntPtr.Zero);
            PostMessage(_window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            switch (command)
            {
                case CmdCustomize: _customize(); break;
                case CmdStartup: StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled); break;
                case CmdExit: _exit(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static IntPtr DrawIcon()
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
            dc.DrawRoundedRectangle(Brushes.Black, new Pen(Brushes.White, 2), new Rect(2, 10, 28, 12), 6, 6);
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            if (alpha is 0 or 255) continue;
            for (int c = 0; c < 3; c++) pixels[i + c] = (byte)Math.Min(255, pixels[i + c] * 255 / alpha);
        }

        IntPtr color = CreateBitmap(size, size, 1, 32, pixels);
        IntPtr mask = CreateBitmap(size, size, 1, 1, new byte[size * size / 8]);
        var info = new IconInfo { IsIcon = true, Mask = mask, Color = color };
        IntPtr icon = CreateIconIndirect(ref info);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Show(NimDelete);
        DestroyIcon(_icon);
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    private const int NimAdd = 0, NimDelete = 2;
    private const int NifMessage = 1, NifIcon = 2, NifTip = 4;
    private const uint MfString = 0, MfChecked = 8, MfSeparator = 0x800;
    private const uint TpmRightButton = 0x2, TpmNoNotify = 0x80, TpmReturnCmd = 0x100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public int State;
        public int StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public int TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public int InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public int HotspotX, HotspotY;
        public IntPtr Mask, Color;
    }

    private struct CursorPoint { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nint id, string? text);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
