using System;
using System.Linq;
using System.Management;

namespace LuminiTray;

public enum CpuVendor
{
    Amd,
    Intel,
    Unknown
}

public enum GpuVendor
{
    Nvidia,
    Amd,
    Intel,
    Unknown
}

public enum CpuPreset
{
    Eco,
    Balanced,
    Performance
}

public interface ICpuController
{
    CpuVendor Vendor { get; }
    string Name { get; }
    bool IsSupported { get; }
    int CoreCount { get; }
    bool IsAm4X3d { get; }

    // AMD specific
    int PptLimitW { get; }
    int TdcLimitA { get; }
    int EdcLimitA { get; }
    int TempLimitC { get; }
    int StockPptW { get; }
    int StockTdcA { get; }
    int StockEdcA { get; }
    int StockTempC { get; }
    int AllCoreCurveOptimizer { get; }
    int[] PerCoreCurveOptimizer { get; }

    // Intel specific
    int Pl1Watts { get; }
    int Pl2Watts { get; }
    int StockPl1Watts { get; }
    int StockPl2Watts { get; }
    int StockTauSeconds { get; }
    int CoreVoltageOffsetMv { get; }
    int CacheVoltageOffsetMv { get; }
    int IgpuVoltageOffsetMv { get; }

    bool ApplyAmdLimits(int pptW, int tdcA, int edcA, int tempLimitC);
    bool ApplyCurveOptimizer(int allCoreOffset, int[]? perCoreOffsets);
    bool ApplyIntelLimits(int pl1W, int pl2W, int tauSeconds);
    bool ApplyIntelVoltageOffset(int coreMv, int cacheMv, int igpuMv);
    bool ApplyPreset(CpuPreset preset);
    bool ResetToDefaults();
}

public interface IGpuController
{
    GpuVendor Vendor { get; }
    string Name { get; }
    bool IsSupported { get; }
    int CoreOffsetMhz { get; }
    int MemoryOffsetMhz { get; }
    int PowerLimitPercent { get; }
    int TempLimitC { get; }
    bool CanLockVfp { get; }

    bool SetClocks(int coreOffsetMhz, int memOffsetMhz);
    bool SetPowerAndTempLimits(int powerPercent, int tempLimitC);
    bool SetVfpLock(int targetMhz, int targetMv);
    bool ResetVfpLock();
    bool ResetToDefaults();
}

/// <summary>
/// Fábrica central: detecta o hardware via WMI/DXGI e instancia apenas os controladores
/// dedicados correspondentes para isolar cada fabricante.
/// </summary>
public static class HardwareControllerFactory
{
    private static ICpuController? _cpuController;
    private static IGpuController? _gpuController;
    private static readonly object _lock = new();

    public static ICpuController GetCpuController()
    {
        lock (_lock)
        {
            if (_cpuController != null) return _cpuController;

            string cpuName = GetCpuNameFromWmi();
            if (cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
            {
                _cpuController = new AmdCpuController(cpuName);
            }
            else if (cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                     cpuName.Contains("Core", StringComparison.OrdinalIgnoreCase))
            {
                _cpuController = new IntelCpuController(cpuName);
            }
            else
            {
                _cpuController = new AmdCpuController(cpuName); // Fallback padrão
            }

            return _cpuController;
        }
    }

    public static IGpuController GetGpuController()
    {
        lock (_lock)
        {
            if (_gpuController != null) return _gpuController;

            string gpuName = GetGpuNameFromWmi();
            if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                gpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase))
            {
                _gpuController = new NvidiaGpuController(gpuName);
            }
            else if (gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                     gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            {
                _gpuController = new AmdGpuController(gpuName);
            }
            else if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                     gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase))
            {
                _gpuController = new IntelGpuController(gpuName);
            }
            else
            {
                _gpuController = new NvidiaGpuController(gpuName); // Fallback padrão
            }

            return _gpuController;
        }
    }

    private static string GetCpuNameFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
        }
        catch { }
        return "Processador";
    }

    private static string GetGpuNameFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                    return name.Trim();
            }
        }
        catch { }
        return "Placa de Vídeo";
    }
}
