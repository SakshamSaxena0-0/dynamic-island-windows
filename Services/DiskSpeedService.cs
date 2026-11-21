using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DynamicIsland.Services;

public sealed class DiskSpeedService
{
    private PerformanceCounter? _counter;
    private bool _busy;

    public bool Available { get; private set; } = true;

    public async Task<double?> SampleAsync()
    {
        if (_busy || !Available) return null;
        _busy = true;
        try
        {
            return await Task.Run(Read);
        }
        catch (Exception ex)
        {
            Available = false;
            Log.Error("disk", ex);
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    public bool Busy => _busy;

    public void Reset()
    {
        if (_busy) return;
        _counter?.Dispose();
        _counter = null;
    }

    private double? Read()
    {
        if (_counter == null)
        {
            _counter = new PerformanceCounter("PhysicalDisk", "Disk Bytes/sec", "_Total", readOnly: true);
            _counter.NextValue();
            return null;
        }
        return Math.Max(0, _counter.NextValue());
    }
}
