using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

public partial class IslandWindow
{
    private const double DragThreshold = 5;

    private bool _dragPending;
    private bool _dragging;
    private DragPoint _dragStartCursor;
    private Point _dragStartWindow;

    private struct DragPoint { public int X, Y; }

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    private static extern bool GetDragCursor(out DragPoint point);

    private void HookDrag()
    {
        Shape.MouseLeftButtonDown += (_, e) =>
        {
            if (_settings.OpenOnHover || !GetDragCursor(out _dragStartCursor)) return;
            _dragPending = true;
            _dragging = false;
            _dragStartWindow = new Point(_anchorX, Top);
            Shape.CaptureMouse();
        };
        Shape.MouseMove += (_, e) =>
        {
            if (!_dragPending || e.LeftButton != MouseButtonState.Pressed || !GetDragCursor(out DragPoint now)) return;
            double dx = now.X - _dragStartCursor.X, dy = now.Y - _dragStartCursor.Y;
            if (!_dragging)
            {
                if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
                _dragging = true;
                Shape.Cursor = Cursors.SizeAll;
            }
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            Point spot = ClampDragged(_dragStartWindow.X + dx / dpi.DpiScaleX, _dragStartWindow.Y + dy / dpi.DpiScaleY);
            PlaceWindow(spot.X, spot.Y);
        };
        Shape.LostMouseCapture += (_, _) => { if (_dragPending) EndDrag(); };
    }

    private bool EndDrag()
    {
        if (!_dragPending) return false;
        bool dragged = _dragging;
        _dragPending = false;
        _dragging = false;
        Shape.ClearValue(CursorProperty);
        if (Shape.IsMouseCaptured) Shape.ReleaseMouseCapture();
        if (!dragged) return false;

        _settings.DragX = Left + Width / 2 + _edgeShift;
        _settings.DragY = Top;
        _settings.Dragged = true;
        PositionWindow();
        return true;
    }
}
