using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DynamicIsland.Services;

public readonly record struct GpuReading(double Main, double? Nvidia);

public sealed class GpuUsageService
{
    private Dictionary<string, CounterSample>? _previous;
    private bool _busy;
    private string? _amd;
    private string? _nvidia;

    public bool Available { get; private set; } = true;

    public string Label => string.IsNullOrEmpty(_amd) ? "GPU" : "AMD GPU";

    public async Task<GpuReading?> SampleAsync()
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
            Log.Error("gpu", ex);
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    public bool Busy => _busy;

    public void Reset() { if (!_busy) _previous = null; }

    private GpuReading? Read()
    {
        InstanceDataCollectionCollection data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
        if (!data.Contains("Utilization Percentage"))
        {
            Available = false;
            return null;
        }

        _amd ??= FindAdapter(0x1002);
        _nvidia ??= FindAdapter(0x10DE);
        var current = new Dictionary<string, CounterSample>();
        var perEngine = new Dictionary<string, double>();
        foreach (InstanceData instance in data["Utilization Percentage"]!.Values)
        {
            string name = instance.InstanceName;
            current[name] = instance.Sample;
            if (_previous == null || !_previous.TryGetValue(name, out CounterSample old)) continue;

            float value = CounterSample.Calculate(old, instance.Sample);
            if (!float.IsFinite(value) || value <= 0) continue;
            int cut = name.IndexOf("luid_", StringComparison.Ordinal);
            string engine = cut >= 0 ? name[cut..] : name;
            perEngine[engine] = perEngine.GetValueOrDefault(engine) + value;
        }

        bool warmedUp = _previous != null;
        _previous = current;
        if (!warmedUp) return null;

        double Busiest(string adapter) => Math.Min(100, perEngine
            .Where(e => e.Key.Contains(adapter, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value).DefaultIfEmpty(0).Max());

        double main = _amd.Length > 0 || _nvidia.Length == 0 ? Busiest(_amd) : Math.Min(100, perEngine
            .Where(e => !e.Key.Contains(_nvidia, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value).DefaultIfEmpty(0).Max());
        return new GpuReading(main, _nvidia.Length > 0 ? Busiest(_nvidia) : null);
    }

    private static string FindAdapter(int vendorId)
    {
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
            if (root == null) return "";
            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? key = root.OpenSubKey(name);
                if (key?.GetValue("VendorId") is not int vendor || vendor != vendorId) continue;
                if (key.GetValue("AdapterLuid") is not long luid) continue;
                return $"luid_0x{(uint)(luid >> 32):X8}_0x{(uint)luid:X8}";
            }
        }
        catch (Exception ex)
        {
            Log.Error("gpu.adapter", ex);
        }
        return "";
    }
}
