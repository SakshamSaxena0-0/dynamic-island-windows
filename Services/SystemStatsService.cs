using System;
using System.Runtime.InteropServices;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

public readonly record struct PowerInfo(bool HasBattery, int Percent, bool PluggedIn, bool Charging, TimeSpan? Remaining);

public readonly record struct SystemStats(double CpuPercent, double RamPercent, double RamUsedGb, double RamTotalGb, PowerInfo Power);

public sealed class SystemStatsService
{
    private const double BytesPerGb = 1024d * 1024 * 1024;
    private long _lastIdle, _lastKernel, _lastUser;
    private double _cpu;

    public SystemStatsService()
    {
        NativeMethods.GetSystemTimes(out _lastIdle, out _lastKernel, out _lastUser);
    }

    public SystemStats Sample()
    {
        if (NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            long idleDelta = idle - _lastIdle;
            long busyAndIdle = (kernel - _lastKernel) + (user - _lastUser);
            if (busyAndIdle > 0) _cpu = Math.Clamp(100.0 * (busyAndIdle - idleDelta) / busyAndIdle, 0, 100);
            _lastIdle = idle;
            _lastKernel = kernel;
            _lastUser = user;
        }

        double total = 0, used = 0, percent = 0;
        var memory = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref memory) && memory.ullTotalPhys > 0)
        {
            total = memory.ullTotalPhys / BytesPerGb;
            used = (memory.ullTotalPhys - memory.ullAvailPhys) / BytesPerGb;
            percent = used / total * 100;
        }

        return new SystemStats(_cpu, percent, used, total, ReadPower());
    }

    public static PowerInfo ReadPower()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var s)) return new PowerInfo(false, 0, true, false, null);

        bool hasBattery = s.BatteryFlag != 255 && (s.BatteryFlag & 128) == 0 && s.BatteryLifePercent <= 100;
        TimeSpan? remaining = s.BatteryLifeTime == uint.MaxValue ? null : TimeSpan.FromSeconds(s.BatteryLifeTime);
        return new PowerInfo(
            HasBattery: hasBattery,
            Percent: hasBattery ? s.BatteryLifePercent : 0,
            PluggedIn: s.ACLineStatus == 1,
            Charging: (s.BatteryFlag & 8) != 0,
            Remaining: remaining);
    }
}
