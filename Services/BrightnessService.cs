using System;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DynamicIsland.Services;

public sealed class BrightnessService : IDisposable
{
    private readonly Dispatcher _ui;
    private ManagementEventWatcher? _watcher;

    public event Action<int>? Changed;

    public BrightnessService(Dispatcher ui) => _ui = ui;

    public void Start()
    {
        Task.Run(() =>
        {
            try
            {
                var watcher = new ManagementEventWatcher(
                    new ManagementScope(@"root\WMI"),
                    new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                watcher.EventArrived += (_, e) =>
                {
                    try
                    {
                        int value = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
                        _ui.BeginInvoke(() => Changed?.Invoke(value));
                    }
                    catch (Exception ex)
                    {
                        Log.Error("brightness.event", ex);
                    }
                };
                watcher.Start();
                _watcher = watcher;
            }
            catch (Exception ex)
            {
                Log.Error("brightness.start", ex);
            }
        });
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch { }
        _watcher?.Dispose();
    }
}
