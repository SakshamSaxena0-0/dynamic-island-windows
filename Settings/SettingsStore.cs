using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace DynamicIsland.Settings;

public sealed class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _dirty;

    public IslandSettings Settings { get; }

    private SettingsStore(IslandSettings settings)
    {
        Settings = settings;
        _saveTimer.Tick += (_, _) => Flush();
        settings.PropertyChanged += (_, _) =>
        {
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        };
    }

    public static SettingsStore Load()
    {
        IslandSettings settings = new();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize(
                    File.ReadAllText(FilePath).Replace("\"BenchM\"", "\"CP\""), SettingsJson.Default.IslandSettings) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("settings.load", ex);
            settings = new();
        }
        settings.Normalize();
        return new SettingsStore(settings);
    }

    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Settings, SettingsJson.Default.IslandSettings));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("settings.save", ex);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(IslandSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
