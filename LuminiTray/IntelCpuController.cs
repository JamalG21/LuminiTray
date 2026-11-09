using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LuminiTray;

/// <summary>
/// Controlador avançado para processadores Intel Core (Gerações antigas e recentes).
/// Utiliza acesso Ring 0 via MSR 0x610 (PL1/PL2/Tau) e MSR 0x150 (FIVR Undervolt Core/Cache/iGPU).
/// </summary>
public sealed class IntelCpuController : ICpuController, IDisposable
{
    public CpuVendor Vendor => CpuVendor.Intel;
    public string Name { get; }
    public bool IsSupported { get; private set; } = true;
    public int CoreCount { get; private set; } = 8;
    public bool IsAm4X3d => false;

    public int PptLimitW => 0;
    public int TdcLimitA => 0;
    public int EdcLimitA => 0;
    public int TempLimitC => 0;
    public int StockPptW => 0;
    public int StockTdcA => 0;
    public int StockEdcA => 0;
    public int StockTempC => 0;
    public int AllCoreCurveOptimizer => 0;
    public int[] PerCoreCurveOptimizer => Array.Empty<int>();

    public int Pl1Watts { get; private set; } = 125;
    public int Pl2Watts { get; private set; } = 180;
    public int StockPl1Watts { get; private set; } = 125;
    public int StockPl2Watts { get; private set; } = 180;
    public int StockTauSeconds { get; private set; } = 28;
    public int CoreVoltageOffsetMv { get; private set; } = 0;
    public int CacheVoltageOffsetMv { get; private set; } = 0;
    public int IgpuVoltageOffsetMv { get; private set; } = 0;

    private SafeFileHandle? _pawnHandle;
    private bool _pawnLoaded;
    private readonly object _msrLock = new();
    private static Mutex? _msrMutex;

    public IntelCpuController(string cpuName)
    {
        Name = cpuName;
        CoreCount = Environment.ProcessorCount;
        TryInitializePawnIo();
        DetectIntelStockLimits(cpuName);
        Pl1Watts = StockPl1Watts;
        Pl2Watts = StockPl2Watts;
    }

    private void DetectIntelStockLimits(string cpuName)
    {
        // 1. Tenta ler direto do registrador MSR 0x610 (MSR_PKG_POWER_LIMIT) se o PawnIO estiver disponível
        if (ReadMsr(0x610, out ulong msr610) && msr610 != 0)
        {
            uint low = (uint)(msr610 & 0xFFFFFFFF);
            uint high = (uint)(msr610 >> 32);
            int rawPl1 = (int)(low & 0x7FFF) / 8;
            int rawPl2 = (int)(high & 0x7FFF) / 8;
            int rawTau = (int)((low >> 17) & 0x7F);

            if (rawPl1 >= 15 && rawPl1 <= 400 && rawPl2 >= 20 && rawPl2 <= 500)
            {
                StockPl1Watts = rawPl1;
                StockPl2Watts = rawPl2;
                if (rawTau > 0 && rawTau <= 128) StockTauSeconds = rawTau;
                return;
            }
        }

        // 2. Detecção inteligente baseada no SKU oficial da Intel
        string u = cpuName.ToUpperInvariant();
        if (u.Contains("ULTRA"))
        {
            if (u.Contains(" H")) { StockPl1Watts = 28; StockPl2Watts = 115; StockTauSeconds = 28; return; }
            if (u.Contains(" U")) { StockPl1Watts = 15; StockPl2Watts = 57; StockTauSeconds = 28; return; }
            if (u.Contains(" V")) { StockPl1Watts = 17; StockPl2Watts = 37; StockTauSeconds = 28; return; }
        }

        if (u.Contains("HX")) { StockPl1Watts = 55; StockPl2Watts = 157; StockTauSeconds = 28; return; }
        if (u.Contains("HK") || u.Contains("HQ") || (u.Contains(" H") && !u.Contains("CORE"))) { StockPl1Watts = 45; StockPl2Watts = 115; StockTauSeconds = 28; return; }
        if (u.Contains(" P")) { StockPl1Watts = 28; StockPl2Watts = 64; StockTauSeconds = 28; return; }
        if (u.Contains(" U")) { StockPl1Watts = 15; StockPl2Watts = 55; StockTauSeconds = 28; return; }
        if (u.Contains(" T")) { StockPl1Watts = 35; StockPl2Watts = 65; StockTauSeconds = 28; return; }

        // Desktop Série K / KF / KS
        if (u.Contains("K") || u.Contains("X"))
        {
            StockPl1Watts = 125;
            StockPl2Watts = (u.Contains("I9") || u.Contains("13900") || u.Contains("14900") || u.Contains("13700") || u.Contains("14700")) ? 253 : 181;
            StockTauSeconds = 56;
            return;
        }

        // Desktop Não-K (Série 12, 13, 14 ou anteriores)
        if (u.Contains("I3")) { StockPl1Watts = 60; StockPl2Watts = 89; StockTauSeconds = 28; return; }
        if (u.Contains("I5")) { StockPl1Watts = 65; StockPl2Watts = 154; StockTauSeconds = 28; return; }
        if (u.Contains("I7")) { StockPl1Watts = 65; StockPl2Watts = 219; StockTauSeconds = 28; return; }
        if (u.Contains("I9")) { StockPl1Watts = 65; StockPl2Watts = 219; StockTauSeconds = 28; return; }

        // Padrão seguro para outros modelos desktop
        StockPl1Watts = 65;
        StockPl2Watts = 125;
        StockTauSeconds = 28;
    }

    private void TryInitializePawnIo()
    {
        try
        {
            _msrMutex ??= new Mutex(false, "Global\\Access_MSR");

            string[] paths = { @"\\?\GLOBALROOT\Device\PawnIO", @"\\.\PawnIO" };
            foreach (var path in paths)
            {
                IntPtr raw = CreateFile(
                    path,
                    0xC0000000,
                    3,
                    IntPtr.Zero,
                    3,
                    0,
                    IntPtr.Zero);

                if (raw != IntPtr.Zero && raw.ToInt64() != -1)
                {
                    _pawnHandle = new SafeFileHandle(raw, true);

                    // Carrega módulo IntelMSR.bin
                    string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Intel", "PawnIO", "IntelMSR.bin");
                    if (File.Exists(binPath))
                    {
                        byte[] binBytes = File.ReadAllBytes(binPath);
                        DeviceIoControl(
                            raw,
                            (41394u << 16) | (0x821 << 2), // IoctlLoadBinary
                            binBytes,
                            (uint)binBytes.Length,
                            null,
                            0,
                            out _,
                            IntPtr.Zero);
                    }

                    _pawnLoaded = true;
                    break;
                }
            }
        }
        catch
        {
            _pawnLoaded = false;
        }
    }

    public bool ApplyIntelLimits(int pl1W, int pl2W, int tauSeconds)
    {
        Pl1Watts = pl1W;
        Pl2Watts = pl2W;

        if (!_pawnLoaded || _pawnHandle == null) return false;

        try
        {
            lock (_msrLock)
            {
                uint high = 0x00438000u | ((uint)(pl2W * 8) & 0xFFFu);
                uint low = 0x00DD8000u | ((uint)((pl1W - 1) * 8) & 0xFFFu);
                ulong msrVal = ((ulong)high << 32) | low;

                return WriteMsr(0x610, msrVal);
            }
        }
        catch
        {
            return false;
        }
    }

    public bool ApplyIntelVoltageOffset(int coreMv, int cacheMv, int igpuMv)
    {
        CoreVoltageOffsetMv = coreMv;
        CacheVoltageOffsetMv = cacheMv;
        IgpuVoltageOffsetMv = igpuMv;

        if (!_pawnLoaded || _pawnHandle == null) return false;

        bool ok = true;
        ok &= WriteVoltagePlane(0, coreMv);   // Core
        ok &= WriteVoltagePlane(2, cacheMv);  // Cache
        if (igpuMv != 0)
            ok &= WriteVoltagePlane(1, igpuMv); // iGPU
        return ok;
    }

    private bool WriteVoltagePlane(int plane, int millivolts)
    {
        ulong cmd = plane switch
        {
            0 => 0x80000011UL, // Core
            1 => 0x80000111UL, // iGPU
            2 => 0x80000211UL, // Cache
            3 => 0x80000411UL, // System Agent
            _ => 0UL
        };
        if (cmd == 0) return false;

        // Converte milivolts para formato em complemento de dois da FIVR Intel (1.024 * mV shift 21)
        ulong data = unchecked((uint)((int)Math.Round(millivolts * 1.024) << 21));
        ulong msrValue = (cmd << 32) | data;

        return WriteMsr(0x150, msrValue);
    }

    private bool ReadMsr(uint msr, out ulong value)
    {
        value = 0;
        if (_pawnHandle == null) return false;
        bool hasMutex = false;
        try
        {
            hasMutex = _msrMutex?.WaitOne(50) ?? false;
            long[] inBuf = { unchecked((long)msr) };
            long[] outBuf = new long[1];
            if (ExecutePawnIoFunction("ioctl_read_msr", inBuf, outBuf))
            {
                value = unchecked((ulong)outBuf[0]);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hasMutex)
            {
                try { _msrMutex?.ReleaseMutex(); } catch { }
            }
        }
    }

    private bool WriteMsr(uint msr, ulong value)
    {
        if (_pawnHandle == null) return false;
        bool hasMutex = false;
        try
        {
            hasMutex = _msrMutex?.WaitOne(50) ?? false;

            long[] inBuf = { unchecked((long)msr), unchecked((long)value) };
            return ExecutePawnIoFunction("ioctl_write_msr", inBuf, Array.Empty<long>());
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hasMutex)
            {
                try { _msrMutex?.ReleaseMutex(); } catch { }
            }
        }
    }

    public bool ApplyPreset(CpuPreset preset)
    {
        return preset switch
        {
            // Eco: ~60% do PL1 de fábrica com undervolt leve
            CpuPreset.Eco => ApplyIntelLimits((int)Math.Max(15, Math.Round(StockPl1Watts * 0.6)),
                                             (int)Math.Max(25, Math.Round(StockPl2Watts * 0.6)),
                                             StockTauSeconds) && ApplyIntelVoltageOffset(-50, -50, 0),

            // Balanced: Padrão exato de fábrica do processador
            CpuPreset.Balanced => ResetToDefaults(),

            // Performance: Potência máxima PL2 mantida como limite contínuo com undervolt agressivo
            CpuPreset.Performance => ApplyIntelLimits(StockPl2Watts, StockPl2Watts, Math.Max(56, StockTauSeconds)) && ApplyIntelVoltageOffset(-75, -75, 0),

            _ => false
        };
    }

    public bool ResetToDefaults()
    {
        // Restaura os limites de fábrica específicos detectados para este modelo de processador Intel
        return ApplyIntelLimits(StockPl1Watts, StockPl2Watts, StockTauSeconds) && ApplyIntelVoltageOffset(0, 0, 0);
    }

    public bool ApplyAmdLimits(int pptW, int tdcA, int edcA, int tempLimitC) => false;
    public bool ApplyCurveOptimizer(int allCoreOffset, int[]? perCoreOffsets) => false;

    private bool ExecutePawnIoFunction(string fnName, long[] inBuf, long[] outBuf)
    {
        try
        {
            byte[] req = new byte[32 + inBuf.Length * 8];
            byte[] nameBytes = Encoding.ASCII.GetBytes(fnName);
            Buffer.BlockCopy(nameBytes, 0, req, 0, Math.Min(31, nameBytes.Length));
            for (int i = 0; i < inBuf.Length; i++)
            {
                byte[] b = BitConverter.GetBytes(inBuf[i]);
                Buffer.BlockCopy(b, 0, req, 32 + i * 8, 8);
            }

            byte[] resp = new byte[Math.Max(8, outBuf.Length * 8)];
            bool ok = DeviceIoControl(
                _pawnHandle!.DangerousGetHandle(),
                (41394u << 16) | (0x841 << 2),
                req,
                (uint)req.Length,
                resp,
                (uint)resp.Length,
                out uint retBytes,
                IntPtr.Zero);

            if (ok && outBuf.Length > 0)
            {
                for (int i = 0; i < outBuf.Length; i++)
                {
                    outBuf[i] = BitConverter.ToInt64(resp, i * 8);
                }
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        byte[]? lpInBuffer,
        uint nInBufferSize,
        byte[]? lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    public void Dispose()
    {
        _pawnHandle?.Dispose();
    }
}
