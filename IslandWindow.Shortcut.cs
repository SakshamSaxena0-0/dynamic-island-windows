using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DynamicIsland;

public partial class IslandWindow
{
    private const int ShortcutId = 0x1517;
    private const int WM_HOTKEY = 0x0312;

    private bool _pinned;
    private string? _shortcutSet;
    private bool _shortcutHooked;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private void ApplyShortcut()
    {
        if (_hwnd == IntPtr.Zero || _shortcutSet == _settings.Shortcut) return;
        if (!_shortcutHooked)
        {
            HwndSource.FromHwnd(_hwnd)?.AddHook(ShortcutHook);
            _shortcutHooked = true;
        }
        UnregisterHotKey(_hwnd, ShortcutId);
        _shortcutSet = _settings.Shortcut;
        if (!TryParseShortcut(_shortcutSet, out uint modifiers, out uint key)) return;
        const uint NoRepeat = 0x4000;
        if (!RegisterHotKey(_hwnd, ShortcutId, modifiers | NoRepeat, key))
            Log.Error("shortcut", new InvalidOperationException($"'{_shortcutSet}' is taken by another program (error {Marshal.GetLastWin32Error()})."));
    }

    private IntPtr ShortcutHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_HOTKEY && wParam.ToInt32() == ShortcutId)
        {
            handled = true;
            ToggleByShortcut();
        }
        return IntPtr.Zero;
    }

    private void ToggleByShortcut()
    {
        if (_pinned)
        {
            SetExpanded(false);
            UpdateHidden();
            return;
        }
        _pinned = true;
        UpdateHidden();
        _hoverIn.Stop();
        _hoverOut.Stop();
        SetExpanded(true);
        KeepOnTop();
    }

    private static bool TryParseShortcut(string? text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= 0x0002; continue;
                case "alt": modifiers |= 0x0001; continue;
                case "shift": modifiers |= 0x0004; continue;
                case "win" or "windows": modifiers |= 0x0008; continue;
            }
            if (key != 0) return false;
            string name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
            if (!Enum.TryParse(name, ignoreCase: true, out Key parsed)) return false;
            key = (uint)KeyInterop.VirtualKeyFromKey(parsed);
        }
        return key != 0;
    }
}
