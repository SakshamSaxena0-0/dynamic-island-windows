using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.UI;

public sealed class RingMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(RingMeter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(RingMeter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingMeter), new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public void Animate(double to) =>
        BeginAnimation(ValueProperty, new DoubleAnimation(Math.Clamp(to, 0, 100), TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

    protected override void OnRender(DrawingContext dc)
    {
        double t = Thickness;
        double r = (Math.Min(ActualWidth, ActualHeight) - t) / 2;
        if (r <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        if (Track != null) dc.DrawEllipse(null, new Pen(Track, t * 0.55), c, r, r);

        double share = Math.Clamp(Value, 0, 100) / 100;
        if (Fill == null || share <= 0.004) return;
        var pen = new Pen(Fill, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (share >= 0.999)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }
        double angle = share * 2 * Math.PI;
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var arc = new StreamGeometry();
        using (StreamGeometryContext g = arc.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - r), false, false);
            g.ArcTo(end, new Size(r, r), 0, share > 0.5, SweepDirection.Clockwise, true, true);
        }
        arc.Freeze();
        dc.DrawGeometry(null, pen, arc);
    }
}

public sealed class Sparkline : FrameworkElement
{
    private const int Capacity = 30;
    private readonly List<double> _values = new();

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    public double Max { get; set; }

    public double Floor { get; set; } = 1;

    public void Push(double value)
    {
        _values.Add(Math.Max(0, value));
        if (_values.Count > Capacity) _values.RemoveAt(0);
        InvalidateVisual();
    }

    public void Clear()
    {
        _values.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (Stroke == null || w <= 0 || h <= 0) return;

        const double pad = 1.5;
        double baseline = h - pad;
        var faint = new Pen(Stroke, 1) { DashStyle = new DashStyle(new double[] { 1, 4 }, 0) };
        dc.PushOpacity(0.35);
        dc.DrawLine(faint, new Point(0, baseline), new Point(w, baseline));
        dc.Pop();
        if (_values.Count < 2) return;

        double top = Max > 0 ? Max : Math.Max(Floor, _values.Max());
        double step = w / (Capacity - 1);
        double x0 = w - step * (_values.Count - 1);
        Point At(int i) => new(x0 + step * i, baseline - Math.Min(1, _values[i] / top) * (h - 2 * pad));

        var line = new StreamGeometry();
        using (StreamGeometryContext g = line.Open())
        {
            g.BeginFigure(At(0), false, false);
            for (int i = 1; i < _values.Count; i++) g.LineTo(At(i), true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 1.6)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        }, line);
    }
}
