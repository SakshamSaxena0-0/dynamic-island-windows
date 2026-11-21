using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland;

public partial class IslandWindow
{
    private readonly GpuUsageService _gpu = new();
    private readonly DiskSpeedService _disk = new();
    private readonly CpuSensorService _cpuSensors = new();
    private readonly DispatcherTimer _clipHintTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private ImageFeedService? _images;
    private double? _gpuPercent;
    private int _folderCheckTicks;

    private void StartImageFeed()
    {
        try
        {
            _images = new ImageFeedService(Dispatcher, HwndSource.FromHwnd(_hwnd));
            _images.Changed += OnImagesChanged;
        }
        catch (Exception ex)
        {
            Log.Error("images.start", ex);
        }
        _clipHintTimer.Tick += (_, _) => { _clipHintTimer.Stop(); ClipHint.Text = "Click a picture to copy it"; };
        RebuildClipStrip();
    }

    private void OnImagesChanged(bool added)
    {
        RebuildClipStrip();
        if (added && _settings.ImagePopup) ShowPopup(View.Clipboard, PopupMs(2500));
    }

    private void RebuildClipStrip()
    {
        ClipStrip.Children.Clear();
        var items = _images?.Items;
        ClipEmpty.Visibility = items is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
        if (items == null) return;

        foreach (ClipItem item in items)
        {
            var brush = new ImageBrush(item.Thumb) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            var tile = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(11),
                Margin = new Thickness(0, 0, 10, 0),
                Background = brush,
                Cursor = Cursors.Hand,
                ToolTip = $"{item.Source} · {item.When:t}",
                Tag = item,
            };
            if (item.IsVideo)
            {
                tile.Child = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    Margin = new Thickness(0, 0, 4, 4),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                    Child = new TextBlock
                    {
                        Text = Glyph.Play,
                        FontFamily = (FontFamily)FindResource("IconFont"),
                        FontSize = 8,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
            }
            tile.MouseLeftButtonDown += ClipTile_Down;
            tile.MouseMove += ClipTile_Move;
            tile.MouseLeftButtonUp += ClipTile_Click;
            ClipStrip.Children.Add(tile);
        }
    }

    private Point? _clipPress;
    private bool _clipDragging;

    private void ClipTile_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _clipPress = e.GetPosition(this);
        ((UIElement)sender).CaptureMouse();
    }

    private void ClipTile_Move(object sender, MouseEventArgs e)
    {
        if (_clipPress is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        Vector moved = e.GetPosition(this) - start;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _clipPress = null;
        ((UIElement)sender).ReleaseMouseCapture();
        if (sender is not Border { Tag: ClipItem item } tile || _images == null) return;

        string? file = _images.DragFile(item);
        if (file == null)
        {
            ClipHint.Text = "Couldn't drag that one";
            _clipHintTimer.Stop();
            _clipHintTimer.Start();
            return;
        }
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { file });
        _clipDragging = true;
        _hoverOut.Stop();
        _popupTimer.Stop();
        try
        {
            DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Log.Error("images.dragdrop", ex);
        }
        finally
        {
            _clipDragging = false;
        }

        if (!Shape.IsMouseOver)
        {
            if (_popup == View.Clipboard) { _popupTimer.Interval = TimeSpan.FromMilliseconds(500); _popupTimer.Start(); }
            else if (_expanded && !_pinned) _hoverOut.Start();
        }
    }

    private void ClipTile_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        bool wasPress = _clipPress != null;
        _clipPress = null;
        ((UIElement)sender).ReleaseMouseCapture();
        if (!wasPress) return;
        if (sender is not Border { Tag: ClipItem item } || _images == null) return;
        ClipHint.Text = _images.Copy(item) ? "Copied. Paste it anywhere" : "Couldn't copy that one";
        _clipHintTimer.Stop();
        _clipHintTimer.Start();
    }

    private void ResetBench()
    {
        _gpu.Reset();
        _disk.Reset();
        _gpuPercent = null;
        BenchGpuLine.Clear();
        BenchNvidiaLine.Clear();
        BenchNvidiaValue.Text = _gpu.Available ? "…" : "n/a";
        BenchDiskLine.Clear();
        BenchGpuLabel.Text = _gpu.Label;
        BenchGpuValue.Text = _gpu.Available ? "…" : "n/a";
        BenchDiskValue.Text = _disk.Available ? "…" : "n/a";
    }

    private void ApplyBench(SystemStats s)
    {
        _benchOpen = true;
        BenchCpuValue.Text = $"{s.CpuPercent:0}";
        BenchRamValue.Text = $"{s.RamPercent:0}";
        BenchCpuRing.Animate(s.CpuPercent);
        BenchRamRing.Animate(s.RamPercent);
        BenchRamRing.ToolTip = BenchRamValue.ToolTip = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB";
        SampleGpu();
        SampleDisk();
        SampleCpuSensors();
    }

    private async void SampleCpuSensors()
    {
        CpuSensors? value = await _cpuSensors.SampleAsync();
        if (_view != View.Cp || value is not CpuSensors s) return;
        BenchTempValue.Text = s.TempC is double t ? $"{t:0}°" : "n/a";
        BenchTempRing.Animate(s.TempC ?? 0);
        if (s.Ghz is double ghz)
        {
            BenchFreqValue.Text = $"{ghz:0.0}";
            BenchFreqRing.Animate(100 * ghz / _cpuSensors.TopGhz);
        }
    }

    private async void SampleGpu()
    {
        GpuReading? value = await _gpu.SampleAsync();
        if (_view != View.Cp) return;
        BenchGpuLabel.Text = _gpu.Label;
        if (!_gpu.Available) { BenchGpuValue.Text = BenchNvidiaValue.Text = "n/a"; return; }
        if (value is not GpuReading g) return;
        _gpuPercent = g.Main;
        BenchGpuValue.Text = $"{g.Main:0}%";
        BenchGpuLine.Push(g.Main);
        BenchNvidiaValue.Text = g.Nvidia is double n ? $"{n:0}%" : "n/a";
        if (g.Nvidia is double load) BenchNvidiaLine.Push(load);
    }

    private async void SampleDisk()
    {
        double? value = await _disk.SampleAsync();
        if (_view != View.Cp) return;
        if (!_disk.Available) { BenchDiskValue.Text = "n/a"; return; }
        if (value is not double bytes) return;
        double mb = bytes / (1024 * 1024);
        BenchDiskValue.Text = mb >= 100 ? $"{mb:0} MB/s" : mb >= 1 ? $"{mb:0.0} MB/s" : $"{bytes / 1024:0} KB/s";
        BenchDiskLine.Push(mb);
    }

    private bool _benchOpen;

    private void ReleaseBench()
    {
        if (!_benchOpen || _view == View.Cp || _gpu.Busy || _disk.Busy || _cpuSensors.Busy) return;
        _benchOpen = false;
        _gpu.Reset();
        _disk.Reset();
        _cpuSensors.Release();
        System.Diagnostics.PerformanceCounter.CloseSharedResources();
    }

    private void TickModes()
    {
        if (_view == View.Cp) ApplyBench(_lastStats);
        else ReleaseBench();

        if (++_folderCheckTicks >= 30)
        {
            _folderCheckTicks = 0;
            _images?.WatchFolders();
        }
    }
}
