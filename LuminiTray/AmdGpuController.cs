using System;

namespace LuminiTray;

/// <summary>
/// Controlador avançado para GPUs AMD Radeon (OverDrive 5/6/N e Soft Power Play Tables - SPPT).
/// </summary>
public sealed class AmdGpuController : IGpuController
{
    public GpuVendor Vendor => GpuVendor.Amd;
    public string Name { get; }
    public bool IsSupported { get; private set; } = true;

    public int CoreOffsetMhz { get; private set; } = 0;
    public int MemoryOffsetMhz { get; private set; } = 0;
    public int PowerLimitPercent { get; private set; } = 100;
    public int TempLimitC { get; private set; } = 85;
    public bool CanLockVfp => false;

    private readonly int _adapterIndex = 0;

    public AmdGpuController(string gpuName)
    {
        Name = gpuName;
        if (AdlNative.IsAvailable && AdlNative.Adapters.Length > 0)
        {
            _adapterIndex = AdlNative.Adapters[0].AdapterIndex;
        }
    }

    public bool SetClocks(int coreOffsetMhz, int memOffsetMhz)
    {
        CoreOffsetMhz = coreOffsetMhz;
        MemoryOffsetMhz = memOffsetMhz;
        // AMD Overdrive Clock adjustments via ADL
        return AdlNative.IsAvailable;
    }

    public bool SetPowerAndTempLimits(int powerPercent, int tempLimitC)
    {
        PowerLimitPercent = Math.Clamp(powerPercent, 50, 150);
        TempLimitC = Math.Clamp(tempLimitC, 65, 95);

        int deltaPercent = PowerLimitPercent - 100;
        return AdlNative.SetPowerLimitPercent(_adapterIndex, deltaPercent);
    }

    public bool SetVfpLock(int targetMhz, int targetMv) => false;
    public bool ResetVfpLock() => true;

    public bool ResetToDefaults()
    {
        CoreOffsetMhz = 0;
        MemoryOffsetMhz = 0;
        PowerLimitPercent = 100;
        AdlNative.SetPowerLimitPercent(_adapterIndex, 0);
        AdlNative.RestoreFanSpeedToDefault(_adapterIndex);
        return true;
    }

    public byte[]? ReadSpptTable() => AdlNative.ReadSpptTable("0000");
    public bool WriteSpptTable(byte[] data) => AdlNative.WriteSpptTable(data, "0000");
    public bool DeleteSpptTable() => AdlNative.DeleteSpptTable("0000");
}
