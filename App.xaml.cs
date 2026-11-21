using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using DynamicIsland.UI;

namespace DynamicIsland;

public partial class App : Application
{
    private const string InstanceName = @"Local\DynamicIsland.Instance";
    private const string OpenSettingsSignalName = @"Local\DynamicIsland.OpenSettings";

    private Mutex? _instanceLock;
    private bool _ownsLock;
    private EventWaitHandle? _openSettingsSignal;
    private RegisteredWaitHandle? _openSettingsWait;
    private SettingsStore? _store;
    private IslandWindow? _island;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceLock = new Mutex(true, InstanceName, out _ownsLock);
        if (!_ownsLock)
        {
            AskRunningIslandToOpenSettings();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) => { Log.Error("ui", args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) Log.Error("fatal", ex); };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("task", args.Exception); args.SetObserved(); };

        base.OnStartup(e);
        _store = SettingsStore.Load();
        _island = new IslandWindow(_store.Settings);
        _island.CustomizeRequested += ShowSettings;
        _island.Show();
        _tray = new TrayIcon(customize: ShowSettings, exit: Shutdown);

        _openSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsSignalName);
        _openSettingsWait = ThreadPool.RegisterWaitForSingleObject(_openSettingsSignal,
            (_, _) => Dispatcher.InvokeAsync(ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);

        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
            ShowSettings();
    }

    private static void AskRunningIslandToOpenSettings()
    {
        try
        {
            int self = Environment.ProcessId;
            foreach (Process p in Process.GetProcessesByName("DynamicIsland"))
            {
                using (p)
                    if (p.Id != self) NativeMethods.AllowSetForegroundWindow(p.Id);
            }
            if (EventWaitHandle.TryOpenExisting(OpenSettingsSignalName, out EventWaitHandle? signal))
                using (signal) signal.Set();
        }
        catch (Exception ex)
        {
            Log.Error("signal", ex);
        }
    }

    private void ShowSettings()
    {
        if (_island == null || _store == null) return;
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_store, _island);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _openSettingsWait?.Unregister(null);
        _openSettingsSignal?.Dispose();
        _settingsWindow?.Close();
        _store?.Flush();
        _tray?.Dispose();
        _island?.StopServices();
        if (_ownsLock) _instanceLock?.ReleaseMutex();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
