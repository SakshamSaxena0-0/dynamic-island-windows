using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;
using DynamicIsland.UI;

namespace DynamicIsland;

public partial class IslandWindow
{
    private enum CpMenu { None, Wifi, Bluetooth }

    private QuickState? _cpState;
    private bool _cpReading;
    private bool _cpBusy;
    private CpMenu _cpMenu;
    private int _cpMenuLoad;

    private async void RefreshCp()
    {
        if (_cpReading || _cpBusy) return;
        _cpReading = true;
        try
        {
            QuickState state = await Task.Run(QuickSettingsService.ReadAsync);
            if (_cpBusy) return;
            _cpState = state;
            ApplyCp(state);
        }
        catch (Exception ex)
        {
            Log.Error("cp.read", ex);
        }
        finally
        {
            _cpReading = false;
        }
    }

    private void ApplyCp(QuickState s)
    {
        CpWifiStatus.Text = s.WifiText;
        CpBtStatus.Text = s.BluetoothText;
        CpWifiTile.IsEnabled = s.HasWifi;
        CpBtTile.IsEnabled = s.HasBluetooth;
        CpWifiMore.IsEnabled = s.HasWifi && s.WifiOn;
        CpBtMore.IsEnabled = s.HasBluetooth && s.BluetoothOn;

        Paint(CpWifiTile, s.WifiOn);
        Paint(CpBtTile, s.BluetoothOn);
        Paint(CpEnergyTile, s.EnergySaver);
        Paint(CpAirplaneTile, s.Airplane);
        Paint(CpNearbyTile, s.NearbyShare);
        Paint(CpPhoneTile, s.PhoneLink);
    }

    private void Paint(Button tile, bool on)
    {
        if (!on)
        {
            tile.ClearValue(BackgroundProperty);
            tile.ClearValue(ForegroundProperty);
            return;
        }
        Color fill = _accentTarget;
        tile.Background = Glass ? GlassBrushes.Solid : new SolidColorBrush(fill);
        double luminance = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255;
        tile.Foreground = Glass || luminance > 0.6 ? Brushes.Black : Brushes.White;
    }

    private async void FlipCp(Func<Task> flip, Func<QuickState, QuickState> expected)
    {
        if (_cpBusy) return;
        _cpBusy = true;
        try
        {
            if (_cpState != null) ApplyCp(_cpState = expected(_cpState));
            await Task.Run(flip);
            await Task.Delay(350);
        }
        catch (Exception ex)
        {
            Log.Error("cp.flip", ex);
        }
        finally
        {
            _cpBusy = false;
        }
        RefreshCp();
    }

    private void CpWifi_Click(object sender, RoutedEventArgs e) =>
        FlipCp(QuickSettingsService.ToggleWifiAsync, s => s with { WifiOn = !s.WifiOn, WifiText = s.WifiOn ? "Off" : "Turning on…" });

    private void CpBt_Click(object sender, RoutedEventArgs e) =>
        FlipCp(QuickSettingsService.ToggleBluetoothAsync, s => s with { BluetoothOn = !s.BluetoothOn, BluetoothText = s.BluetoothOn ? "Off" : "Turning on…" });

    private void CpEnergy_Click(object sender, RoutedEventArgs e) =>
        FlipCp(QuickSettingsService.ToggleEnergySaverAsync, s => s with { EnergySaver = !s.EnergySaver });

    private void CpPhone_Click(object sender, RoutedEventArgs e) =>
        FlipCp(QuickSettingsService.TogglePhoneLinkAsync, s => s with { PhoneLink = !s.PhoneLink });

    private void CpAirplane_Click(object sender, RoutedEventArgs e) =>
        FlipCp(QuickSettingsService.ToggleAirplaneAsync, s => s with { Airplane = !s.Airplane });

    private void CpNearby_Click(object sender, RoutedEventArgs e) =>
        FlipCp(QuickSettingsService.ToggleNearbyShareAsync, s => s with { NearbyShare = !s.NearbyShare });

    private void CpWifiMore_Click(object sender, RoutedEventArgs e) => OpenCpMenu(CpMenu.Wifi);

    private void CpBtMore_Click(object sender, RoutedEventArgs e) => OpenCpMenu(CpMenu.Bluetooth);

    private void CpMenuBack_Click(object sender, RoutedEventArgs e) => CloseCpMenu();

    private void OpenCpMenu(CpMenu menu)
    {
        _cpMenu = menu;
        CpMenuTitle.Text = menu == CpMenu.Wifi ? "Wi-Fi" : "Bluetooth";
        CpMenuList.Children.Clear();
        CpMenuScroll.ScrollToTop();
        ShowCpNote(menu == CpMenu.Wifi ? "Searching…" : "Loading…");
        UpdateView();
        LoadCpMenu(scan: true);
    }

    private void CloseCpMenu()
    {
        if (_cpMenu == CpMenu.None) return;
        _cpMenu = CpMenu.None;
        _cpMenuLoad++;
        UpdateView();
    }

    private async void LoadCpMenu(bool scan)
    {
        int load = ++_cpMenuLoad;
        CpMenu menu = _cpMenu;
        try
        {
            if (menu == CpMenu.Wifi)
            {
                MenuList known = await Task.Run(() => QuickSettingsService.WifiNetworksAsync(scan: false));
                if (load != _cpMenuLoad) return;
                ShowCpRows(known);
                if (!scan) return;
                MenuList fresh = await Task.Run(() => QuickSettingsService.WifiNetworksAsync(scan: true));
                if (load != _cpMenuLoad) return;
                ShowCpRows(fresh);
            }
            else if (menu == CpMenu.Bluetooth)
            {
                MenuList devices = await Task.Run(QuickSettingsService.BluetoothMenuAsync);
                if (load != _cpMenuLoad) return;
                ShowCpRows(devices);
            }
        }
        catch (Exception ex)
        {
            Log.Error("cp.menu", ex);
        }
    }

    private double _cpScrollTarget;
    private bool _cpScrolling;
    private TimeSpan _cpScrollLast;
    private ScrollViewer _scrollBox = null!;
    private View _scrollView;

    private void ScrollList(ScrollViewer box, int wheelDelta)
    {
        if (_cpScrolling && box != _scrollBox) _cpScrollTarget = box.VerticalOffset;
        bool continuing = _cpScrolling && box == _scrollBox;
        _scrollBox = box;
        _scrollView = _view;
        double from = continuing ? _cpScrollTarget : box.VerticalOffset;
        _cpScrollTarget = Math.Clamp(from - wheelDelta * 0.6, 0, box.ScrollableHeight);
        if (_cpScrolling) return;
        _cpScrolling = true;
        _cpScrollLast = TimeSpan.Zero;
        CompositionTarget.Rendering += CpScrollFrame;
    }

    private void CpScrollFrame(object? sender, EventArgs e)
    {
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _cpScrollLast == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (now - _cpScrollLast).TotalSeconds);
        _cpScrollLast = now;

        ScrollViewer box = _scrollBox;
        _cpScrollTarget = Math.Clamp(_cpScrollTarget, 0, box.ScrollableHeight);
        double offset = box.VerticalOffset;
        double gap = _cpScrollTarget - offset;
        bool done = Math.Abs(gap) < 0.4 || _view != _scrollView;

        box.ScrollToVerticalOffset(done ? _cpScrollTarget : offset + gap * (1 - Math.Exp(-14 * dt)));
        if (!done) return;
        CompositionTarget.Rendering -= CpScrollFrame;
        _cpScrolling = false;
    }

    private void ShowCpNote(string? note)
    {
        CpMenuNote.Text = note ?? "";
        CpMenuNote.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowCpRows(MenuList list)
    {
        CpMenuList.Children.Clear();
        ShowCpNote(list.Note);
        bool wifi = _cpMenu == CpMenu.Wifi;
        foreach (MenuRow row in list.Rows)
        {
            var status = new TextBlock
            {
                Text = row.Status,
                FontSize = 11.5,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            DockPanel.SetDock(status, Dock.Right);
            var line = new DockPanel { Margin = new Thickness(12, 0, 12, 0) };
            line.Children.Add(status);
            if (row.Locked)
            {
                var padlock = new TextBlock
                {
                    Text = "\uE72E",
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 0),
                    ToolTip = "Password protected",
                };
                DockPanel.SetDock(padlock, Dock.Left);
                line.Children.Add(padlock);
            }
            line.Children.Add(new TextBlock
            {
                Text = row.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new Button
            {
                Style = (Style)FindResource("CpTile"),
                Height = 30,
                Margin = new Thickness(0, 0, 0, 4),
                Content = line,
                Tag = row,
            };
            if (wifi)
            {
                button.ToolTip = row.Active ? "Click to disconnect" : "Click to connect";
                button.Click += CpWifiRow_Click;
            }
            else
            {
                button.Cursor = null;
            }
            Paint(button, row.Active);
            CpMenuList.Children.Add(button);
        }
    }

    private async void CpWifiRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MenuRow row }) return;
        int load = ++_cpMenuLoad;
        ShowCpNote(row.Active ? "Disconnecting…" : $"Connecting to {row.Name}…");
        string result;
        try
        {
            result = await Task.Run(() => QuickSettingsService.WifiConnectAsync(row.Id));
        }
        catch (Exception ex)
        {
            Log.Error("cp.wifi", ex);
            result = "Couldn't connect";
        }
        if (load != _cpMenuLoad || _cpMenu != CpMenu.Wifi) return;

        MenuList list = await Task.Run(() => QuickSettingsService.WifiNetworksAsync(scan: false));
        if (load != _cpMenuLoad || _cpMenu != CpMenu.Wifi) return;
        ShowCpRows(list);
        ShowCpNote(result);
    }
}
