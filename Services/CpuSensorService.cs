using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DynamicIsland.Services;

public readonly record struct CpuSensors(double? TempC, double? Ghz);

public sealed class CpuSensorService
{
    private PerformanceCounter[]? _zones;
    private PerformanceCounter? _performance;
    private double _baseGhz;
    private bool _busy;

    public double TopGhz { get; private set; } = 4;

    public bool Busy => _busy;

    public void Release()
    {
        if (_busy || _zones == null) return;
        foreach (PerformanceCounter zone in _zones) zone.Dispose();
        _performance?.Dispose();
        _zones = null;
        _performance = null;
    }

    public async Task<CpuSensors?> SampleAsync()
    {
        if (_busy) return null;
        _busy = true;
        try
        {
            return await Task.Run(Read);
        }
        catch (Exception ex)
        {
            Log.Error("cpu.sensors", ex);
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    private CpuSensors Read()
    {
        if (_zones == null) Open();

        double? temp = null;
        foreach (PerformanceCounter zone in _zones!)
        {
            try
            {
                double celsius = zone.NextValue() / 10 - 273.15;
                if (celsius is > 0 and < 150 && (temp == null || celsius > temp)) temp = celsius;
            }
            catch (InvalidOperationException) {   }
        }

        double? ghz = null;
        if (_performance != null && _baseGhz > 0)
        {
            double percent = _performance.NextValue();
            if (percent > 0)
            {
                ghz = _baseGhz * percent / 100;
                if (ghz > TopGhz) TopGhz = ghz.Value;
            }
        }
        return new CpuSensors(temp, ghz);
    }

    private void Open()
    {
        try
        {
            var category = new PerformanceCounterCategory("Thermal Zone Information");
            _zones = category.GetInstanceNames()
                .Select(name => new PerformanceCounter("Thermal Zone Information", "High Precision Temperature", name, readOnly: true))
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Error("cpu.temp", ex);
            _zones = Array.Empty<PerformanceCounter>();
        }

        try
        {
            if (_baseGhz == 0)
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key?.GetValue("~MHz") is int mhz) _baseGhz = mhz / 1000.0;
                TopGhz = Math.Max(1, _baseGhz * 1.3);
            }
            _performance = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", readOnly: true);
            _performance.NextValue();
        }
        catch (Exception ex)
        {
            Log.Error("cpu.speed", ex);
            _performance = null;
        }
    }
}
