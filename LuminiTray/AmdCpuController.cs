using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LuminiTray;

/// <summary>
/// Controlador avançado para processadores AMD Ryzen (Zen 2, Zen 3, Zen 4, Zen 5 e linha X3D).
/// Implementa comunicação direta com a SMU via Mailbox do PCI Config Space / PawnIO,
/// limites de energia (PPT, TDC, EDC, Temp) e Curve Optimizer (All-Core e Per-Core de -30 a +30).
/// </summary>
public sealed class AmdCpuController : ICpuController, IDisposable
{
    public CpuVendor Vendor => CpuVendor.Amd;
    public string Name { get; }
    public bool IsSupported { get; private set; } = true;
    public int CoreCount { get; private set; } = 8;
    public bool IsAm4X3d { get; private set; }

    public int PptLimitW { get; private set; } = 105;
    public int TdcLimitA { get; private set; } = 75;
    public int EdcLimitA { get; private set; } = 115;
    public int TempLimitC { get; private set; } = 90;

    public int StockPptW { get; private set; } = 105;
    public int StockTdcA { get; private set; } = 75;
    public int StockEdcA { get; private set; } = 115;
    public int StockTempC { get; private set; } = 90;

    public int AllCoreCurveOptimizer { get; private set; } = 0;
    public int[] PerCoreCurveOptimizer { get; private set; }

    public int Pl1Watts => 0;
    public int Pl2Watts => 0;
    public int StockPl1Watts => 0;
    public int StockPl2Watts => 0;
    public int StockTauSeconds => 0;
    public int CoreVoltageOffsetMv => 0;
    public int CacheVoltageOffsetMv => 0;
    public int IgpuVoltageOffsetMv => 0;

    // Mailbox SMU AM4_V2 (Matisse/Vermeer/5000/X3D) e AM5
    private uint _mp1AddrMsg = 0x3B10530;
    private uint _mp1AddrRsp = 0x3B1057C;
    private uint _mp1AddrArg = 0x3B109C4;
    private uint _psmuAddrMsg = 0x03B10524;
    private uint _psmuAddrRsp = 0x03B10570;
    private uint _psmuAddrArg = 0x03B10A40;

    private const uint CMD_COALL_MP1 = 0x36;
    private const uint CMD_COALL_RSMU = 0x0B;
    private const uint CMD_COPER_MP1 = 0x35;
    private const uint CMD_COPER_RSMU = 0x0A;
    private const uint CMD_PPT_MP1 = 0x3D;
    private const uint CMD_PPT_RSMU = 0x53;
    private const uint CMD_TDC_MP1 = 0x3B;
    private const uint CMD_TDC_RSMU = 0x54;
    private const uint CMD_EDC_MP1 = 0x3C;
    private const uint CMD_EDC_RSMU = 0x55;
    private const uint CMD_TCTL_MP1 = 0x3E;
    private const uint CMD_TCTL_RSMU = 0x56;

    // PawnIO Low-Level Handle
    private SafeFileHandle? _pawnHandle;
    private bool _pawnLoaded;
    private readonly object _smuLock = new();
    private static Mutex? _pciMutex;

    public AmdCpuController(string cpuName)
    {
        Name = cpuName;
        CoreCount = Environment.ProcessorCount;
        PerCoreCurveOptimizer = new int[CoreCount];

        DetectAmdStockLimits(cpuName);
        PptLimitW = StockPptW;
        TdcLimitA = StockTdcA;
        EdcLimitA = StockEdcA;
        TempLimitC = StockTempC;

        ConfigureFamilyAddresses(cpuName);
        TryInitializePawnIo();
    }

    private void DetectAmdStockLimits(string cpuName)
    {
        string u = cpuName.ToUpperInvariant();

        // 1. AMD Threadripper (ex: 3960X, 3970X, 5965WX, 7960X, 7980X)
        if (u.Contains("THREADRIPPER") || u.Contains("PRO 59") || u.Contains("PRO 79"))
        {
            StockPptW = 280;
            StockTdcA = 215;
            StockEdcA = 300;
            StockTempC = 95;
            return;
        }

        // 2. Linha Mobile / Laptops (HX, H, HS, U)
        if (u.Contains("MOBILE") || u.Contains("HX") || u.Contains("HS") || (u.Contains(" H") && !u.Contains("THREADRIPPER")) || u.Contains(" U"))
        {
            if (u.Contains("HX"))
            {
                StockPptW = 85;
                StockTdcA = 70;
                StockEdcA = 100;
                StockTempC = 95;
            }
            else if (u.Contains("HS") || u.Contains(" H"))
            {
                StockPptW = 54;
                StockTdcA = 45;
                StockEdcA = 65;
                StockTempC = 95;
            }
            else
            {
                StockPptW = 28;
                StockTdcA = 25;
                StockEdcA = 40;
                StockTempC = 95;
            }
            return;
        }

        // 3. AM5 Desktop (Zen 4 e Zen 5 - Ryzen 7000 / 8000 / 9000)
        bool isAm5 = (u.Contains("7") || u.Contains("8") || u.Contains("9")) &&
                     (u.Contains("7600") || u.Contains("7700") || u.Contains("7800") || u.Contains("7900") || u.Contains("7950") ||
                      u.Contains("8600") || u.Contains("8700") ||
                      u.Contains("9600") || u.Contains("9700") || u.Contains("9900") || u.Contains("9950"));
        if (isAm5)
        {
            // AM5 X3D (7800X3D, 7900X3D, 7950X3D, 9800X3D, 9950X3D) - TDP Oficial 120W
            if (u.Contains("X3D"))
            {
                StockPptW = 162;
                StockTdcA = 120;
                StockEdcA = 180;
                StockTempC = 89;
                return;
            }

            // AM5 170W TDP Oficial (7950X, 7900X, 7700X, 9950X, 9900X)
            if (u.Contains("7950X") || u.Contains("7900X") || u.Contains("7700X") ||
                u.Contains("9950X") || u.Contains("9900X"))
            {
                StockPptW = 230;
                StockTdcA = 160;
                StockEdcA = 225;
                StockTempC = 95;
                return;
            }

            // AM5 65W TDP Oficial (7600, 7700 não-X, 8600G, 8700G, 9600X, 9700X)
            StockPptW = 88;
            StockTdcA = 75;
            StockEdcA = 110;
            StockTempC = 95;
            return;
        }

        // 4. AM4 Desktop (Zen 2 e Zen 3 - Ryzen 3000 / 4000 / 5000)
        // AM4 X3D (5700X3D, 5800X3D) - TDP Oficial 105W (PBO Limits bloqueados na AGESA)
        if (u.Contains("5700X3D") || u.Contains("5800X3D") || (u.Contains("X3D") && !isAm5))
        {
            IsAm4X3d = true;
            StockPptW = 142;
            StockTdcA = 95;
            StockEdcA = 140;
            StockTempC = 90;
            return;
        }

        // AM4 105W TDP Oficial (5800X, 5900X, 5950X, 3800X, 3800XT, 3900X, 3900XT, 3950X)
        if (u.Contains("5800X") || u.Contains("5900X") || u.Contains("5950X") ||
            u.Contains("3800X") || u.Contains("3900X") || u.Contains("3950X"))
        {
            StockPptW = 142;
            StockTdcA = 95;
            StockEdcA = 140;
            StockTempC = 90;
            return;
        }

        // AM4 65W TDP Oficial (5600, 5600X, 5700X, 3600, 3600X, 3700X, 5500, 5600G, 5700G, 4600G, 3100, 3300X)
        StockPptW = 88;
        StockTdcA = 60;
        StockEdcA = 90;
        StockTempC = 95;
    }

    private void ConfigureFamilyAddresses(string cpuName)
    {
        // Se for AM5 (Ryzen 7000 / 8000 / 9000)
        if (cpuName.Contains("7") || cpuName.Contains("8") || cpuName.Contains("9"))
        {
            if (cpuName.Contains("7000") || cpuName.Contains("7600") || cpuName.Contains("7700") ||
                cpuName.Contains("7800") || cpuName.Contains("7900") || cpuName.Contains("7950") ||
                cpuName.Contains("9600") || cpuName.Contains("9700") || cpuName.Contains("9900") || cpuName.Contains("9950"))
            {
                _mp1AddrMsg = 0x3B10530;
                _mp1AddrRsp = 0x3B1057C;
                _mp1AddrArg = 0x3B109C4;
                _psmuAddrMsg = 0x03B10524;
                _psmuAddrRsp = 0x03B10570;
                _psmuAddrArg = 0x03B10A40;
            }
        }
    }

    private void TryInitializePawnIo()
    {
        try
        {
            _pciMutex ??= new Mutex(false, "Global\\Access_PCI");

            string[] paths = { @"\\?\GLOBALROOT\Device\PawnIO", @"\\.\PawnIO" };
            foreach (var path in paths)
            {
                IntPtr raw = CreateFile(
                    path,
                    0xC0000000, // GENERIC_READ | GENERIC_WRITE
                    3,          // FILE_SHARE_READ | FILE_SHARE_WRITE
                    IntPtr.Zero,
                    3,          // OPEN_EXISTING
                    0,
                    IntPtr.Zero);

                if (raw != IntPtr.Zero && raw.ToInt64() != -1)
                {
                    _pawnHandle = new SafeFileHandle(raw, true);

                    // Carrega módulo RyzenSMU.bin (testa caminhos no diretório do app ou no projeto)
                    string[] possibleBinPaths =
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AMD", "PawnIO", "RyzenSMU.bin"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RyzenSMU.bin"),
                        @"C:\Users\Willian\Desktop\LuminiTray\LuminiTray\Assets\AMD\PawnIO\RyzenSMU.bin"
                    };

                    foreach (var binPath in possibleBinPaths)
                    {
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
                            break;
                        }
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

    public bool ApplyAmdLimits(int pptW, int tdcA, int edcA, int tempLimitC)
    {
        PptLimitW = pptW;
        TdcLimitA = tdcA;
        EdcLimitA = edcA;
        TempLimitC = tempLimitC;

        // AMD SMU exige unidades em miliwatts (mW) para PPT e miliamperes (mA) para TDC e EDC:
        // Exemplo: 142 W = 142.000 mW (0x22AB0), 95 A = 95.000 mA, 140 A = 140.000 mA.
        // Se o valor fornecido for menor que 1000, multiplica por 1000.
        uint pptMw = (uint)(pptW < 1000 ? pptW * 1000 : pptW);
        uint tdcMa = (uint)(tdcA < 1000 ? tdcA * 1000 : tdcA);
        uint edcMa = (uint)(edcA < 1000 ? edcA * 1000 : edcA);
        uint temp = (uint)Math.Clamp(tempLimitC, 65, 105);

        // Processadores Ryzen 5000 X3D (5700X3D e 5800X3D) possuem travas de segurança na AGESA
        // para limites PBO de energia. Tentar enviar comandos arbitrários de corte de PPT
        // faz a SMU acionar corte de emergência PROCHOT (travando em 500 MHz / 0.50 GHz).
        // Se os valores forem os de fábrica (StockPptW >= 142), enviamos os limites stock em miliwatts/miliamperes
        // para desengasgar qualquer travamento anterior de 105mW / 0.11W.
        if (IsAm4X3d)
        {
            if (pptMw >= 142000)
            {
                SendCommandWithFallback(CMD_PPT_MP1, CMD_PPT_RSMU, pptMw);
                SendCommandWithFallback(CMD_TDC_MP1, CMD_TDC_RSMU, tdcMa);
                SendCommandWithFallback(CMD_EDC_MP1, CMD_EDC_RSMU, edcMa);
                SendCommandWithFallback(CMD_TCTL_MP1, CMD_TCTL_RSMU, temp);
            }
            return true;
        }

        bool ok = true;
        ok &= SendCommandWithFallback(CMD_PPT_MP1, CMD_PPT_RSMU, pptMw);
        ok &= SendCommandWithFallback(CMD_TDC_MP1, CMD_TDC_RSMU, tdcMa);
        ok &= SendCommandWithFallback(CMD_EDC_MP1, CMD_EDC_RSMU, edcMa);
        ok &= SendCommandWithFallback(CMD_TCTL_MP1, CMD_TCTL_RSMU, temp);
        return ok;
    }

    public bool ResetToDefaults()
    {
        // Restaura os limites de fábrica específicos detectados para este modelo de processador AMD
        return ApplyAmdLimits(StockPptW, StockTdcA, StockEdcA, StockTempC) && ApplyCurveOptimizer(0, null);
    }

    public bool ApplyCurveOptimizer(int allCoreOffset, int[]? perCoreOffsets)
    {
        AllCoreCurveOptimizer = Math.Clamp(allCoreOffset, -30, 30);

        if (perCoreOffsets != null && perCoreOffsets.Length > 0)
        {
            for (int i = 0; i < Math.Min(perCoreOffsets.Length, PerCoreCurveOptimizer.Length); i++)
            {
                PerCoreCurveOptimizer[i] = Math.Clamp(perCoreOffsets[i], -30, 30);
                uint val = EncodeCurveOptimizerValue(PerCoreCurveOptimizer[i]);
                uint arg = ((uint)i << 20) | (val & 0xFFFFF);
                SendCommandWithFallback(CMD_COPER_MP1, CMD_COPER_RSMU, arg);
            }
            return true;
        }

        uint encoded = EncodeCurveOptimizerValue(AllCoreCurveOptimizer);
        return SendCommandWithFallback(CMD_COALL_MP1, CMD_COALL_RSMU, encoded);
    }

    private static uint EncodeCurveOptimizerValue(int offset)
    {
        // Offsets negativos (undervolt): complemento de 2 / 0x100000 - Abs
        if (offset < 0)
        {
            return (uint)(0x100000 - Math.Abs(offset));
        }
        return (uint)offset;
    }

    public bool ApplyPreset(CpuPreset preset)
    {
        return preset switch
        {
            // Eco: ~65% do PPT de fábrica e offset leve -15
            CpuPreset.Eco => ApplyAmdLimits((int)Math.Max(35, Math.Round(StockPptW * 0.65)),
                                           (int)Math.Max(25, Math.Round(StockTdcA * 0.65)),
                                           (int)Math.Max(40, Math.Round(StockEdcA * 0.65)),
                                           Math.Min(StockTempC, 80)) && ApplyCurveOptimizer(-15, null),

            // Balanced: Padrão exato de fábrica do processador detectado
            CpuPreset.Balanced => ResetToDefaults(),

            // Performance: Limite de fábrica com undervolt agressivo (-30) para maximizar boost sem estrangular
            CpuPreset.Performance => ApplyAmdLimits(StockPptW, StockTdcA, StockEdcA, StockTempC) && ApplyCurveOptimizer(-30, null),

            _ => false
        };
    }

    public bool ApplyIntelLimits(int pl1W, int pl2W, int tauSeconds) => false;
    public bool ApplyIntelVoltageOffset(int coreMv, int cacheMv, int igpuMv) => false;

    private bool SendCommandWithFallback(uint mp1Cmd, uint rsmuCmd, uint argument)
    {
        if (!_pawnLoaded || _pawnHandle == null || _pawnHandle.IsInvalid)
            return false;

        lock (_smuLock)
        {
            bool hasMutex = false;
            try
            {
                hasMutex = _pciMutex?.WaitOne(50) ?? false;

                // Tenta MP1 Mailbox
                if (ExecuteMailbox(_mp1AddrMsg, _mp1AddrRsp, _mp1AddrArg, mp1Cmd, argument))
                    return true;

                // Tenta RSMU / PSMU Mailbox
                if (ExecuteMailbox(_psmuAddrMsg, _psmuAddrRsp, _psmuAddrArg, rsmuCmd, argument))
                    return true;

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
                    try { _pciMutex?.ReleaseMutex(); } catch { }
                }
            }
        }
    }

    private bool ExecuteMailbox(uint addrMsg, uint addrRsp, uint addrArg, uint cmd, uint arg)
    {
        if (_pawnHandle == null) return false;

        // Limpa RSP
        WriteRegister32(addrRsp, 0);

        // Escreve os 6 registradores de argumentos (ARG0 a ARG5) com 0 para evitar lixo no mailbox
        WriteRegister32(addrArg, arg);
        for (uint i = 1; i < 6; i++)
        {
            WriteRegister32(addrArg + (i * 4), 0);
        }

        // Envia comando MSG
        WriteRegister32(addrMsg, cmd);

        // Espera resposta (polling até 8192 iterações)
        for (int i = 0; i < 8192; i++)
        {
            if (ReadRegister32(addrRsp, out uint rsp) && rsp != 0)
            {
                return rsp == 1; // 1 = Status.OK
            }
            Thread.SpinWait(10);
        }
        return false;
    }

    private bool ReadRegister32(uint reg, out uint value)
    {
        value = 0;
        if (_pawnHandle == null) return false;

        long[] inBuf = { unchecked((long)reg) };
        long[] outBuf = new long[1];
        if (ExecutePawnIoFunction("ioctl_read_smu_register", inBuf, outBuf))
        {
            value = unchecked((uint)outBuf[0]);
            return true;
        }
        return false;
    }

    private bool WriteRegister32(uint reg, uint val)
    {
        if (_pawnHandle == null) return false;

        long[] inBuf = { unchecked((long)reg), unchecked((long)val) };
        return ExecutePawnIoFunction("ioctl_write_smu_register", inBuf, Array.Empty<long>());
    }

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
                (41394u << 16) | (0x841 << 2), // IoctlExecuteFn
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
