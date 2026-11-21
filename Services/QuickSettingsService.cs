using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace DynamicIsland.Services;

internal sealed record QuickState(
    bool HasWifi, bool WifiOn, string WifiText,
    bool HasBluetooth, bool BluetoothOn, string BluetoothText,
    bool EnergySaver, bool Airplane, bool NearbyShare, bool PhoneLink);

internal sealed record MenuRow(string Id, string Name, string Status, bool Active, bool Locked = false);

internal sealed record MenuList(IReadOnlyList<MenuRow> Rows, string? Note);

internal static class QuickSettingsService
{
    private const string CdpKey = @"Software\Microsoft\Windows\CurrentVersion\CDP";

    private const ulong EnergySaverOverride = 0x41C6013DA3BC3075;
    private const ulong EnergySaverSetting = 0x41C6013DA3BC2875;
    private const ulong NearShareSetting = 0x41960A2EA3BC5035;

    private static WiFiAdapter? _wifiAdapter;

    public static async Task<QuickState> ReadAsync()
    {
        Radio? wifi = null, bluetooth = null;
        try
        {
            var radios = await Radio.GetRadiosAsync();
            wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            bluetooth = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        }
        catch (Exception ex) { Log.Error("quick.radios", ex); }

        bool wifiOn = wifi?.State == RadioState.On;
        bool btOn = bluetooth?.State == RadioState.On;
        string wifiText = wifi == null ? "No Wi-Fi" : !wifiOn ? "Off" : WifiName() ?? "Not connected";
        string btText = bluetooth == null ? "No Bluetooth" : !btOn ? "Off" : await BluetoothNameAsync() ?? "Not connected";

        return new QuickState(wifi != null, wifiOn, wifiText, bluetooth != null, btOn, btText,
            EnergySaverOn(), AirplaneOn(), NearbyShareOn(), PhoneLinkOn());
    }

    private static string? WifiName()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is { IsWlanConnectionProfile: true }) return profile.ProfileName;
            return NetworkInformation.GetConnectionProfiles()
                .FirstOrDefault(p => p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)
                ?.ProfileName;
        }
        catch { return null; }
    }

    private static async Task<string?> BluetoothNameAsync() =>
        (await BluetoothDevicesAsync()).FirstOrDefault(d => d.Active)?.Name;

    private static bool EnergySaverOn()
    {
        if (ReadNumber(EnergySaverSetting) is uint setting) return setting == 1;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return key?.GetValue("EnergySaverState") is int v && v == 1;
        }
        catch { return false; }
    }

    private static bool AirplaneOn()
    {
        try
        {
            if (CreateRadioManager() is { } manager)
            {
                try
                {
                    if (manager.GetSystemRadioState(out int radiosEnabled, out _, out _) == 0) return radiosEnabled == 0;
                }
                finally { Marshal.ReleaseComObject(manager); }
            }
        }
        catch {   }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState");
            return key?.GetValue(null) is int v && v != 0;
        }
        catch { return false; }
    }

    private const string MobilityKey = @"Software\Microsoft\Windows\CurrentVersion\Mobility";
    private const string PhoneLinkApp = @"shell:AppsFolder\Microsoft.YourPhone_8wekyb3d8bbwe!App";

    private static bool PhoneLinkOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MobilityKey);
            return key?.GetValue("PhoneLinkEnabled") is not int v || v != 0;
        }
        catch { return true; }
    }

    private static bool PhoneLinkRunning() => System.Diagnostics.Process.GetProcessesByName("PhoneExperienceHost").Length > 0;

    public static async Task TogglePhoneLinkAsync()
    {
        try
        {
            bool on = !PhoneLinkOn();
            using (var key = Registry.CurrentUser.CreateSubKey(MobilityKey))
                key.SetValue("PhoneLinkEnabled", on ? 1 : 0, RegistryValueKind.DWord);
            if (!on)
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("PhoneExperienceHost"))
                {
                    try { process.Kill(); } catch {   }
                    process.Dispose();
                }
                return;
            }
            for (int i = 0; i < 6 && !PhoneLinkRunning(); i++) await Task.Delay(500);
            if (!PhoneLinkRunning())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", PhoneLinkApp) { UseShellExecute = true })?.Dispose();

            bool seen = false;
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(300);
                IntPtr window = FindWindow(null, "Phone Link");
                if (window == IntPtr.Zero || !IsWindowVisible(window))
                {
                    if (seen) break;
                    continue;
                }
                if (!seen)
                {
                    seen = true;
                    await Task.Delay(900);
                }
                PostMessage(window, 0x0010  , IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception ex) { Log.Error("quick.phonelink", ex); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string title);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static bool NearbyShareOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(CdpKey);
            return key?.GetValue("NearShareChannelUserAuthzPolicy") is int v && v != 0;
        }
        catch { return false; }
    }

    public static Task ToggleWifiAsync() => ToggleRadioAsync(RadioKind.WiFi);
    public static Task ToggleBluetoothAsync() => ToggleRadioAsync(RadioKind.Bluetooth);

    private static async Task ToggleRadioAsync(RadioKind kind)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return;
            var radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == kind);
            if (radio != null) await radio.SetStateAsync(radio.State == RadioState.On ? RadioState.Off : RadioState.On);
        }
        catch (Exception ex) { Log.Error("quick.radio", ex); }
    }

    public static async Task ToggleEnergySaverAsync()
    {
        try { Publish(EnergySaverOverride, EnergySaverOn() ? 2u : 1u); }
        catch (Exception ex) { Log.Error("quick.energy", ex); }
        await Task.Delay(500);
    }

    public static async Task ToggleAirplaneAsync()
    {
        bool want = !AirplaneOn();
        try
        {
            if (CreateRadioManager() is { } manager)
            {
                try { manager.SetSystemRadioState(want ? 0 : 1); }
                finally { Marshal.ReleaseComObject(manager); }
            }
        }
        catch (Exception ex) { Log.Error("quick.airplane", ex); }
        await Task.Delay(900);
    }

    public static Task ToggleNearbyShareAsync()
    {
        try
        {
            uint value = NearbyShareOn() ? 0u : 2u;
            using (var key = Registry.CurrentUser.CreateSubKey(CdpKey))
                key.SetValue("NearShareChannelUserAuthzPolicy", (int)value, RegistryValueKind.DWord);
            Publish(NearShareSetting, value);
        }
        catch (Exception ex) { Log.Error("quick.nearby", ex); }
        return Task.CompletedTask;
    }

    public static async Task<MenuList> WifiNetworksAsync(bool scan)
    {
        try
        {
            WiFiAdapter? adapter = await WifiAdapterAsync();
            if (adapter == null) return new MenuList(Array.Empty<MenuRow>(), "Windows Location is off, so networks can't be listed");
            if (scan) await adapter.ScanAsync();

            string? connected = (await adapter.NetworkAdapter.GetConnectedProfileAsync())?.ProfileName;
            var rows = adapter.NetworkReport.AvailableNetworks
                .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
                .GroupBy(n => n.Ssid)
                .Select(g => g.OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts).First())
                .OrderByDescending(n => n.Ssid == connected)
                .ThenByDescending(n => n.NetworkRssiInDecibelMilliwatts)
                .Take(12)
                .Select(n => new MenuRow(n.Ssid, n.Ssid,
                    n.Ssid == connected ? "Connected" : (IsOpen(n) ? "Open · " : "") + Bars(n.SignalBars),
                    n.Ssid == connected, Locked: !IsOpen(n)))
                .ToList();
            return new MenuList(rows, rows.Count == 0 ? (scan ? "No networks found" : "Searching…") : null);
        }
        catch (UnauthorizedAccessException)
        {
            return new MenuList(Array.Empty<MenuRow>(), "Windows Location is off, so networks can't be listed");
        }
        catch (Exception ex)
        {
            Log.Error("quick.wifi.list", ex);
            return new MenuList(Array.Empty<MenuRow>(), "Couldn't read the network list");
        }
    }

    public static async Task<string> WifiConnectAsync(string ssid)
    {
        try
        {
            WiFiAdapter? adapter = await WifiAdapterAsync();
            if (adapter == null) return "Wi-Fi isn't available";
            string? connected = (await adapter.NetworkAdapter.GetConnectedProfileAsync())?.ProfileName;
            if (connected == ssid)
            {
                adapter.Disconnect();
                await Task.Delay(700);
                return "Disconnected";
            }

            WiFiAvailableNetwork? network = adapter.NetworkReport.AvailableNetworks
                .Where(n => n.Ssid == ssid).OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts).FirstOrDefault();
            if (network == null) return "That network is out of range";

            if (RunNetsh($"wlan connect name=\"{ssid.Replace("\"", "")}\"") && await WaitConnectedAsync(adapter, ssid)) return "Connected";
            if (IsOpen(network))
            {
                var result = await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic, new PasswordCredential());
                if (result.ConnectionStatus == WiFiConnectionStatus.Success) return "Connected";
                return "Couldn't connect";
            }
            return "New network: join it once in Windows to save its password";
        }
        catch (Exception ex)
        {
            Log.Error("quick.wifi.connect", ex);
            return "Couldn't connect";
        }
    }

    private static async Task<WiFiAdapter?> WifiAdapterAsync()
    {
        if (_wifiAdapter != null) return _wifiAdapter;
        if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed) return null;
        return _wifiAdapter = (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
    }

    private static async Task<bool> WaitConnectedAsync(WiFiAdapter adapter, string ssid)
    {
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(500);
            if ((await adapter.NetworkAdapter.GetConnectedProfileAsync())?.ProfileName == ssid) return true;
        }
        return false;
    }

    private static bool IsOpen(WiFiAvailableNetwork n) =>
        n.SecuritySettings.NetworkAuthenticationType is NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.None;

    private static string Bars(byte bars) => new string('●', Math.Clamp((int)bars, 1, 4)) + new string('○', 4 - Math.Clamp((int)bars, 1, 4));

    private static bool RunNetsh(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netsh.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            if (process == null) return false;
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch { return false; }
    }

    public static async Task<MenuList> BluetoothMenuAsync()
    {
        var rows = await BluetoothDevicesAsync();
        return new MenuList(rows, rows.Count == 0 ? "No paired devices" : null);
    }

    private static async Task<List<MenuRow>> BluetoothDevicesAsync()
    {
        const string connectedKey = "System.Devices.Aep.IsConnected";
        var rows = new List<MenuRow>();
        try
        {
            foreach (string selector in new[]
                     {
                         BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                         BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                     })
            {
                foreach (DeviceInformation device in await DeviceInformation.FindAllAsync(selector, new[] { connectedKey }))
                {
                    if (string.IsNullOrWhiteSpace(device.Name)) continue;
                    bool connected = device.Properties.TryGetValue(connectedKey, out object? value) && value is true;
                    MenuRow? same = rows.FirstOrDefault(r => r.Name == device.Name);
                    if (same != null)
                    {
                        if (connected && !same.Active) rows[rows.IndexOf(same)] = same with { Active = true, Status = "Connected" };
                        continue;
                    }
                    rows.Add(new MenuRow(device.Id, device.Name, connected ? "Connected" : "Paired", connected));
                }
            }
        }
        catch {   }
        return rows.OrderByDescending(r => r.Active).ThenBy(r => r.Name).ToList();
    }

    private static void Publish(ulong name, uint value)
    {
        int status = RtlPublishWnfStateData(name, IntPtr.Zero, ref value, 4, IntPtr.Zero);
        if (status < 0) throw new InvalidOperationException($"Windows refused the switch (0x{status:X8})");
    }

    private static uint? ReadNumber(ulong name)
    {
        try
        {
            var buffer = new byte[64];
            uint size = (uint)buffer.Length;
            return NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, buffer, ref size) == 0 && size >= 4
                ? BitConverter.ToUInt32(buffer, 0)
                : null;
        }
        catch { return null; }
    }

    private static IRadioManager? CreateRadioManager()
    {
        Type? type = Type.GetTypeFromCLSID(new Guid("581333F6-28DB-41BE-BC7A-FF201F12F3F6"));
        return type == null ? null : Activator.CreateInstance(type) as IRadioManager;
    }

    [ComImport, Guid("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRadioManager
    {
        [PreserveSig] int IsRMSupported(out uint state);
        [PreserveSig] int GetUIRadioInstances(out IntPtr collection);
        [PreserveSig] int GetSystemRadioState(out int enabled, out int unused, out int changeReason);
        [PreserveSig] int SetSystemRadioState(int enabled);
        [PreserveSig] int Refresh();
        [PreserveSig] int OnHardwareSliderChange(int a, int b);
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlPublishWnfStateData(ulong name, IntPtr typeId, ref uint data, uint length, IntPtr scope);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(ref ulong name, IntPtr typeId, IntPtr scope, out uint stamp, byte[] buffer, ref uint size);
}
