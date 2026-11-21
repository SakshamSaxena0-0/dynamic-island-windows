using System;
using System.IO;

namespace DynamicIsland;

internal static class Log
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
    private static readonly object Gate = new();

    public static void Error(string where, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
