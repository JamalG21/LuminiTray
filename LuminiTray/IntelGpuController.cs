using System;

namespace LuminiTray;

/// <summary>
/// Controlador avançado para GPUs Intel Arc e Intel Iris Xe (igcl.dll).
/// </summary>
public sealed class IntelGpuController : IGpuController
{
    public GpuVendor Vendor => GpuVendor.Intel;
    public string Name { get; }
    public bool IsSupported { get; private set; } = true;

    public int CoreOffsetMhz { get; private set; } = 0;
    public int MemoryOffsetMhz { get; private set; } = 0;
    public int PowerLimitPercent { get; private set; } = 100;
    public int TempLimitC { get; private set; } = 85;
    public bool CanLockVfp => false;

    private readonly int _deviceIndex = 0;

    public IntelGpuController(string gpuName)
    {
        Name = gpuName;
    }

    public bool SetClocks(int coreOffsetMhz, int memOffsetMhz)
    {
        CoreOffsetMhz = coreOffsetMhz;
        MemoryOffsetMhz = memOffsetMhz;

        if (IgclNative.IsAvailable)
        {
            return IgclNative.SetOverclock(_deviceIndex, CoreOffsetMhz, 0, 0);
        }
        return false;
    }

    public bool SetPowerAndTempLimits(int powerPercent, int tempLimitC)
    {
        PowerLimitPercent = powerPercent;
        TempLimitC = tempLimitC;

        if (IgclNative.IsAvailable)
        {
            double watts = (double)PowerLimitPercent * 1.5; // Estimativa de base
            return IgclNative.SetOverclock(_deviceIndex, 0, 0, watts);
        }
        return false;
    }

    public bool SetVfpLock(int targetMhz, int targetMv) => false;
    public bool ResetVfpLock() => true;

    public bool ResetToDefaults()
    {
        CoreOffsetMhz = 0;
        MemoryOffsetMhz = 0;
        PowerLimitPercent = 100;
        if (IgclNative.IsAvailable)
        {
            IgclNative.SetOverclock(_deviceIndex, 0, 0, 0);
        }
        return true;
    }
}
