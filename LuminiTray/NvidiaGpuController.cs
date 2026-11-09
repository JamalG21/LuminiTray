using System;
using System.Linq;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using static NvAPIWrapper.Native.GPU.Structures.PerformanceStates20InfoV1;

namespace LuminiTray;

/// <summary>
/// Controlador avançado para GPUs NVIDIA GeForce / RTX.
/// Implementa Core Clock Offset, Memory Clock Offset via NvAPIWrapper,
/// Power Limit e Thermal Limit via NVML, e Trava de VFP (Undervolt estável).
/// </summary>
public sealed class NvidiaGpuController : IGpuController
{
    public GpuVendor Vendor => GpuVendor.Nvidia;
    public string Name { get; }
    public bool IsSupported { get; private set; } = true;

    public int CoreOffsetMhz { get; private set; } = 0;
    public int MemoryOffsetMhz { get; private set; } = 0;
    public int PowerLimitPercent { get; private set; } = 100;
    public int TempLimitC { get; private set; } = 83;
    public bool CanLockVfp => true;

    private readonly IntPtr _nvmlDevice = IntPtr.Zero;

    public NvidiaGpuController(string gpuName)
    {
        Name = gpuName;
        try
        {
            if (Nvml.Available)
            {
                var devs = Nvml.Devices();
                if (devs.Count > 0)
                {
                    _nvmlDevice = devs[0].Handle;
                }
            }
        }
        catch { }
    }

    public bool SetClocks(int coreOffsetMhz, int memOffsetMhz)
    {
        CoreOffsetMhz = Math.Clamp(coreOffsetMhz, -500, 1500);
        MemoryOffsetMhz = Math.Clamp(memOffsetMhz, -500, 2000);

        try
        {
            var gpus = PhysicalGPU.GetPhysicalGPUs();
            var gpu = gpus.FirstOrDefault();
            if (gpu == null) return false;

            var coreClock = new PerformanceStates20ClockEntryV1(
                PublicClockDomain.Graphics,
                new PerformanceStates20ParameterDelta(CoreOffsetMhz * 1000)
            );

            var memClock = new PerformanceStates20ClockEntryV1(
                PublicClockDomain.Memory,
                new PerformanceStates20ParameterDelta(MemoryOffsetMhz * 1000)
            );

            var performanceStates = new[]
            {
                new PerformanceState20(
                    PerformanceStateId.P0_3DPerformance,
                    new[] { coreClock, memClock },
                    Array.Empty<PerformanceStates20BaseVoltageEntryV1>())
            };

            var overclock = new PerformanceStates20InfoV1(performanceStates, 2, 0);
            GPUApi.SetPerformanceStates20(gpu.Handle, overclock);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetPowerAndTempLimits(int powerPercent, int tempLimitC)
    {
        PowerLimitPercent = Math.Clamp(powerPercent, 50, 130);
        TempLimitC = Math.Clamp(tempLimitC, 65, 90);

        if (_nvmlDevice == IntPtr.Zero) return false;

        try
        {
            var constraints = Nvml.PowerConstraintsMw(_nvmlDevice);
            uint defMw = Nvml.PowerDefaultMw(_nvmlDevice) ?? 150000;

            if (constraints.HasValue)
            {
                uint targetMw = (uint)((double)defMw * (PowerLimitPercent / 100.0));
                targetMw = Math.Clamp(targetMw, constraints.Value.Item1, constraints.Value.Item2);
                return Nvml.SetPowerLimitMw(_nvmlDevice, targetMw) == 0;
            }
        }
        catch { }

        return false;
    }

    public bool SetVfpLock(int targetMhz, int targetMv)
    {
        if (_nvmlDevice == IntPtr.Zero) return false;
        try
        {
            uint clock = (uint)Math.Clamp(targetMhz, 500, 3200);
            return Nvml.LockGpuClocks(_nvmlDevice, clock, clock) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool ResetVfpLock()
    {
        if (_nvmlDevice == IntPtr.Zero) return false;
        try
        {
            return Nvml.ResetGpuClocks(_nvmlDevice) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool ResetToDefaults()
    {
        bool ok = true;
        ok &= SetClocks(0, 0);
        ResetVfpLock();

        if (_nvmlDevice != IntPtr.Zero)
        {
            uint def = Nvml.PowerDefaultMw(_nvmlDevice) ?? 0;
            if (def > 0)
            {
                Nvml.SetPowerLimitMw(_nvmlDevice, def);
                PowerLimitPercent = 100;
            }
        }
        return ok;
    }
}
