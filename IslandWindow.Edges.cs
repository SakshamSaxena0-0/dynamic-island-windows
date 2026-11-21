using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public partial class IslandWindow
{
    private const double EdgeMargin = 8;
    private double _edgeShift;

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public MonitorRect Monitor, Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(ScreenPoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private Rect ScreenAt(double x, double y)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        var point = new ScreenPoint { X = (int)Math.Round(x * dpi.DpiScaleX), Y = (int)Math.Round(y * dpi.DpiScaleY) };
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(point, 2), ref info))
            return new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return new Rect(info.Monitor.Left / dpi.DpiScaleX, info.Monitor.Top / dpi.DpiScaleY,
                        (info.Monitor.Right - info.Monitor.Left) / dpi.DpiScaleX,
                        (info.Monitor.Bottom - info.Monitor.Top) / dpi.DpiScaleY);
    }

    private double _anchorX = double.NaN;

    private void PlaceWindow(double centreX, double top)
    {
        _anchorX = centreX;
        Top = Math.Round(top);
        Rect screen = ScreenAt(centreX, top + 1);
        double widest = MaxShapeWidth * _settings.Scale;
        double lowest = screen.Left + EdgeMargin;
        double left = Math.Clamp(centreX - widest / 2, lowest, Math.Max(lowest, screen.Right - EdgeMargin - widest));
        Left = Math.Round(left + widest / 2 - Width / 2);
        KeepOnScreen(animate: false);
    }

    private void KeepOnScreen(bool animate)
    {
        if (double.IsNaN(_anchorX)) return;
        double centre = _anchorX;
        Rect screen = ScreenAt(centre, Top + 1);
        double half = SizeOf(_view == View.None ? View.Idle : _view).Width * _settings.Scale / 2;
        double shown = centre;
        if (centre - half < screen.Left + EdgeMargin) shown = screen.Left + EdgeMargin + half;
        else if (centre + half > screen.Right - EdgeMargin) shown = screen.Right - EdgeMargin - half;

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double shift = Math.Round((shown - (Left + Width / 2)) * dpi) / dpi;
        if (Math.Abs(shift - _edgeShift) < 0.5 && (animate || Math.Abs(ShapeDrop.X - shift) < 0.5)) return;
        _edgeShift = shift;
        ShapeDrop.BeginAnimation(TranslateTransform.XProperty, animate
            ? new DoubleAnimation(shift, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }
            : null);
        if (!animate) ShapeDrop.X = shift;
    }

    private Point ClampDragged(double x, double y)
    {
        Rect screen = ScreenAt(x, y);
        double lowest = screen.Bottom - (_settings.TopOffset + 6 + OpenDrop + MaxShapeHeight * _settings.Scale + EdgeMargin);
        return new Point(Math.Clamp(x, screen.Left, screen.Right), Math.Clamp(y, screen.Top, Math.Max(screen.Top, lowest)));
    }
}
