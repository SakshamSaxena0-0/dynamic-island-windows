# Dynamic Island for Windows

A small always-on-top "island" at the top of the screen for Windows 10/11: music, quick switches, live system load, a screenshot tray and your notifications, all in one pill that opens when you hover or click it.

<p align="center">
  <a href="https://github.com/SakshamSaxena0-0/dynamic-island-windows/releases/latest/download/DynamicIsland.zip">
    <img src="https://img.shields.io/badge/Download-Dynamic%20Island%20for%20Windows-0A84FF?style=for-the-badge&logo=windows&logoColor=white" alt="Download Dynamic Island for Windows" height="48">
  </a>
</p>

## Install

1. Install the **.NET 10 Desktop Runtime (x64)** from [Microsoft](https://dotnet.microsoft.com/download/dotnet/10.0) if you don't have it (the app tells you if it's missing).
2. [Download `DynamicIsland.zip`](https://github.com/SakshamSaxena0-0/dynamic-island-windows/releases/latest/download/DynamicIsland.zip) and unzip it anywhere (for example `C:\Dynamic Island`).
3. Run `DynamicIsland.exe`. Right-click the island (or double-click the tray icon) to customize it; the tray menu has *Start with Windows*.

Windows SmartScreen may warn about an unsigned app the first time: choose *More info > Run anyway*.

## What it does

- **Idle:** a small pill showing the time. On the Live tab with music playing it shows the song name instead. The island returns to this half a second after the mouse leaves.
- **Music playing** (Spotify, YouTube in a browser, Media Player, VLC, or anything that shows up in Windows' media controls): album art and a bouncing equalizer tinted with the cover's colour.
- **Hover or click:** opens to show title, artist, progress, previous/play/pause/next, and CPU, RAM and battery.
- **Nothing playing:** the music tab disappears; scrolling moves between Clipboard, CP and Notifications only, and it comes back when music starts.
- **Pop-ups:** volume, brightness, charger plugged in/unplugged, low battery (20% and 10%), and a short peek when the song changes.
- **Opening:** the open island sits a little lower than the resting pill, and stays open while the cursor is on it or in the gap above it.
- **Phone Link button (CP tab):** off flips the Windows Phone Link switch and stops its background program; on flips it back and restarts it (its window may flash for a moment).
- **CPU temperature and speed (right side of the CP tab):** two more rings; temperature is full at 100 °C, speed is full at the highest boost seen.
- **Shortcut (default Ctrl+Alt+F12, change it in Customize):** opens the island and keeps it open until pressed again, even over a borderless-fullscreen game. "Show only with the shortcut" keeps the island off the screen the rest of the time.
- **Open by Hover or Click (Customize > Behaviour):** with Click, press, hold and move the mouse to drag the island anywhere; Customize > Position > Centre puts it back at the top. Near a screen edge the island slides inward, so the pill and every opened panel stay fully on screen.
- **Scroll over the island** to switch between Live, Clipboard, CP and Notifications; hover to open the current one.
- Hides itself while a fullscreen game or video is in front.
- **Clipboard mode** ("Show on island" in Customize): a tray of your 5 newest screenshots, photos and screen recordings; click one to copy it (a recording is copied as a file), or drag one out of the island and drop it into a folder, chat, mail or upload box. In Live mode the tray pops open briefly when a new picture arrives.
- **Live load (left side of the CP tab):** CPU and RAM as circular meters, disk speed, the integrated AMD GPU's load and the NVIDIA card's load as zig-zag lines, refreshed every second.
- **Notifications mode:** the notifications waiting in the Windows notification centre (the Win+N list), newest first, with the app's icon, title, text and age. Click one to open the app it came from. **Clear all** empties the list; the × on one dismisses only that one. Scrolling over a long list scrolls it; scrolling over the title switches tabs. If Windows' "Let apps access your notifications" privacy switch is off, the tab says so.
- **CP mode:** quick switches for Wi-Fi, Bluetooth, Energy saver, Airplane mode and Nearby sharing. The arrows open a list inside the island: Wi-Fi networks (click one to connect or disconnect; a brand-new password-protected network has to be joined once in Windows) and paired Bluetooth devices. Nothing opens Windows Settings. Energy saver, Airplane mode and Nearby sharing use the internal signals that Windows uses for its own buttons, so a big Windows update could break them.
- Tray icon: *Customize…*, *Start with Windows*, *Exit*.

## Customize

Open the **Customize** window by right-clicking the island, double-clicking the tray icon, or
choosing **Dynamic Island** in the Start menu. Themes, colours, glow, size, roundness, font,
position, animation style, hover behaviour and pop-ups can all be changed; changes apply
instantly and are saved to `%LOCALAPPDATA%\DynamicIsland\settings.json`.
Delete that file (with the island closed) to go back to defaults.

## Run

Run `DynamicIsland.exe`. Starting it while it is already running opens the Customize window.

## Build from source

Needs the .NET 10 SDK. Close the running island first (tray icon → Exit), then:

```powershell
dotnet publish -c Release -o app
```

## Where things are

| File | What it does |
|---|---|
| `IslandWindow.xaml` | Layout of every island state (sizes, text, bars) |
| `IslandWindow.xaml.cs` | Which state shows when, hover, pop-ups, fullscreen hiding |
| `IslandWindow.Appearance.cs` | Applies the settings to the island; preview modes |
| `Settings/IslandSettings.cs` | Every customizable setting and its default |
| `Settings/ThemePreset.cs` | The ready-made themes (add your own here) |
| `UI/SettingsWindow.xaml` | The Customize window |
| `UI/Themes/SettingsLiquidGlass.xaml` | The Customize window's look (always Liquid Glass, whatever the island's theme) |
| `UI/GlassBrushes.cs` | Liquid Glass paint for the island (glass, rim, steel, bone) |
| `Assets/Fonts/` | Bundled Manrope and JetBrains Mono (SIL Open Font License, licence texts included) |
| `UI/IslandMorph.cs` | The spring animation of the shape (motion styles) |
| `IslandWindow.Modes.cs` | Clipboard mode and the live load meters |
| `Services/ImageFeedService.cs` | Watches the clipboard and picture folders for new images |
| `IslandWindow.Cp.cs` | CP mode (the quick switch buttons) |
| `Services/QuickSettingsService.cs` | Reads and flips Wi-Fi, Bluetooth, Energy saver, Airplane mode, Nearby sharing |
| `Services/GpuUsageService.cs` | GPU load (Windows "GPU Engine" counters) |
| `Services/MediaService.cs` | Now playing + media controls |
| `Services/SystemStatsService.cs` | CPU / RAM / battery |
| `Services/AudioVolumeService.cs` | Volume changes (for the volume pop-up) |
| `Services/BrightnessService.cs` | Laptop screen brightness changes |
| `UI/TrayIcon.cs` | Tray menu |

Errors (if any) are written to `%LOCALAPPDATA%\DynamicIsland\errors.log`.

## License

[MIT](LICENSE). The bundled Manrope and JetBrains Mono fonts are under the SIL Open Font License (texts in `Assets/Fonts`).
