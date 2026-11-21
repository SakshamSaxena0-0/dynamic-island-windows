using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Settings;

namespace DynamicIsland.UI;

public sealed class IslandMorph
{
    private readonly Border _shape;
    private readonly UIElement _content;
    private readonly RectangleGeometry _clip = new();
    private readonly Spring _width, _height, _radius;
    private double _stiffness = 420;
    private double _damping = 34;
    private bool _instant;
    private bool _running;
    private TimeSpan _lastFrame;

    public IslandMorph(Border shape, UIElement content)
    {
        _shape = shape;
        _content = content;
        _width = new Spring(shape.Width);
        _height = new Spring(shape.Height);
        _radius = new Spring(shape.CornerRadius.TopLeft);
        _content.Clip = _clip;
        Apply();
    }

    public void SetMotion(MotionStyle style)
    {
        (_stiffness, _damping, _instant) = style switch
        {
            MotionStyle.Snappy => (750.0, 52.0, false),
            MotionStyle.Bouncy => (400.0, 17.0, false),
            MotionStyle.Gentle => (160.0, 24.0, false),
            MotionStyle.Off => (0.0, 0.0, true),
            _ => (420.0, 34.0, false),
        };
    }

    public void MorphTo(double width, double height, double radius)
    {
        _width.Target = width;
        _height.Target = height;
        _radius.Target = radius;

        if (_instant)
        {
            _width.Snap();
            _height.Snap();
            _radius.Snap();
            Stop();
            Apply();
            return;
        }
        if (_running) return;
        _running = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Refresh() => Apply();

    private void Stop()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnRendering;
        _running = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        dt = Math.Clamp(dt, 0.001, 1.0 / 30);

        int steps = (int)Math.Ceiling(dt * 480);
        double h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            _width.Step(h, _stiffness, _damping);
            _height.Step(h, _stiffness, _damping);
            _radius.Step(h, _stiffness, _damping);
        }

        if (_width.Settled && _height.Settled && _radius.Settled)
        {
            _width.Snap();
            _height.Snap();
            _radius.Snap();
            Stop();
        }
        Apply();
    }

    private void Apply()
    {
        double w = Math.Max(1, _width.Value);
        double h = Math.Max(1, _height.Value);
        double r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);
        _shape.Width = w;
        _shape.Height = h;
        _shape.CornerRadius = new CornerRadius(r);

        double t = _shape.BorderThickness.Left;
        _clip.Rect = new Rect(0, 0, Math.Max(0, w - 2 * t), Math.Max(0, h - 2 * t));
        _clip.RadiusX = _clip.RadiusY = Math.Max(0, r - t);
    }

    private sealed class Spring
    {
        public double Value, Velocity, Target;

        public Spring(double value) => Value = Target = value;

        public bool Settled => Math.Abs(Value - Target) < 0.25 && Math.Abs(Velocity) < 2;

        public void Step(double dt, double stiffness, double damping)
        {
            double acceleration = -stiffness * (Value - Target) - damping * Velocity;
            Velocity += acceleration * dt;
            Value += Velocity * dt;
        }

        public void Snap()
        {
            Value = Target;
            Velocity = 0;
        }
    }
}
