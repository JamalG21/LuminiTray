using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace LuminiTray;

/// <summary>
/// Informações de plataforma detectadas via WMI/Sistema para diagnosticar suporte de controle de ventoinhas.
/// </summary>
public sealed class HardwarePlatformInfo
{
    public bool IsLaptop { get; init; }
    public string Manufacturer { get; init; } = "Desconhecido";
    public string Model { get; init; } = "Desconhecido";
    public string BiosVersion { get; init; } = "—";
    public bool IsAdmin { get; init; }
    public bool IsPawnIoDetected { get; init; }

    private static HardwarePlatformInfo? _cached;

    public static HardwarePlatformInfo Detect()
    {
        if (_cached != null) return _cached;

        bool isLaptop = false;
        string manufacturer = "Desconhecido";
        string model = "Desconhecido";
        string bios = "—";

        try
        {
            using var cs = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementObject mo in cs.Get())
            {
                manufacturer = mo["Manufacturer"]?.ToString()?.Trim() ?? manufacturer;
                model = mo["Model"]?.ToString()?.Trim() ?? model;
                break;
            }
        }
        catch { }

        try
        {
            using var bs = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (ManagementObject mo in bs.Get())
            {
                bios = mo["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? bios;
                break;
            }
        }
        catch { }

        try
        {
            using var enc = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject mo in enc.Get())
            {
                if (mo["ChassisTypes"] is ushort[] types)
                {
                    foreach (ushort t in types)
                    {
                        // Portable / Laptop / Notebook / SubNotebook / Convertible / Detachable
                        if (t is 8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 or 30 or 31 or 32)
                        {
                            isLaptop = true;
                            break;
                        }
                    }
                }
                if (isLaptop) break;
            }
        }
        catch { }

        if (!isLaptop)
        {
            try
            {
                var status = SystemInformation.PowerStatus;
                if (status.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery &&
                    status.BatteryChargeStatus != BatteryChargeStatus.Unknown)
                {
                    isLaptop = true;
                }
            }
            catch { }
        }

        bool isAdmin = false;
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            isAdmin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { }

        bool pawnIo = CheckPawnIoInstalled();

        _cached = new HardwarePlatformInfo
        {
            IsLaptop = isLaptop,
            Manufacturer = manufacturer,
            Model = model,
            BiosVersion = bios,
            IsAdmin = isAdmin,
            IsPawnIoDetected = pawnIo
        };
        return _cached;
    }

    private static bool CheckPawnIoInstalled()
    {
        try
        {
            using var sc = new ManagementObjectSearcher("SELECT Name, State FROM Win32_SystemDriver WHERE Name LIKE '%PawnIO%'");
            foreach (ManagementObject mo in sc.Get())
            {
                return true;
            }
        }
        catch { }

        try
        {
            string sysPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "PawnIO.sys");
            if (System.IO.File.Exists(sysPath)) return true;
        }
        catch { }

        return false;
    }
}

/// <summary>
/// Fallback térmico universal para notebooks sem controle direto de PWM no Embedded Controller (EC/BIOS).
/// Controla a Política de Resfriamento (Passivo vs Ativo) e o Estado Máximo do Processador via PowrProf.dll,
/// reduzindo temperatura e rotação do cooler controlado pela BIOS sem depender de Super I/O.
/// </summary>
public sealed class NotebookThermalBackend : IDisposable
{
    private static Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static Guid SystemCoolingPolicy = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");
    private static Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    private Guid _savedSchemeGuid;
    private uint? _origAcCoolingPolicy;
    private uint? _origDcCoolingPolicy;
    private uint? _origAcThrottleMax;
    private uint? _origDcThrottleMax;
    private bool _hasSnapshot;

    private uint _lastAppliedCooling = uint.MaxValue;
    private uint _lastAppliedMaxCpu = uint.MaxValue;

    public bool IsActive { get; private set; }
    public bool IsPassiveCooling => _lastAppliedCooling == 0;
    public int CurrentMaxCpuPercent => _lastAppliedMaxCpu <= 100 ? (int)_lastAppliedMaxCpu : 100;
    public string StatusSummary { get; private set; } = "Inativo (gerenciado pelo plano do Windows)";

    /// <summary>
    /// Calcula política de resfriamento (0 = Passivo, 1 = Ativo) e teto de CPU (55%..100%)
    /// a partir da curva de ventoinha e temperatura atual da CPU.
    /// </summary>
    public static (uint coolingPolicy, uint maxCpuPercent) ComputeThermalTarget(double cpuTemp, FanCurve curve)
    {
        double curvePwm = Math.Clamp(curve.Evaluate(cpuTemp), 0.0, 100.0);

        // Curvas mais agressivas (>= 60% PWM pedido) usam política Ativa (1);
        // abaixo disso priorizam resfriamento Passivo (0) para reduzir ruído da ventoinha no EC.
        uint coolingPolicy = curvePwm >= 60.0 ? 1u : 0u;

        // Se a temperatura está baixa/média e a curva pede silêncio, limita teto térmico suavemente (60% a 100%);
        // acima de 78 °C mantém folga ou reduz conforme perfil silencioso.
        uint maxCpu;
        if (cpuTemp >= 82.0 && curvePwm < 70.0)
        {
            // Proteção térmica em perfil silencioso: reduz boost para baixar temperatura do EC
            maxCpu = 75u;
        }
        else if (curvePwm <= 40.0)
        {
            maxCpu = 70u;
        }
        else if (curvePwm <= 60.0)
        {
            maxCpu = 85u;
        }
        else
        {
            maxCpu = 100u;
        }

        return (coolingPolicy, maxCpu);
    }

    public void ApplyForTemperature(double cpuTemp, FanCurve curve)
    {
        IntPtr schemePtr = IntPtr.Zero;
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out schemePtr) != 0 || schemePtr == IntPtr.Zero)
                return;

            Guid activeScheme = Marshal.PtrToStructure<Guid>(schemePtr);

            if (!_hasSnapshot || _savedSchemeGuid != activeScheme)
            {
                if (_hasSnapshot) RestoreOriginal();
                CaptureSnapshot(activeScheme);
            }

            var (targetCooling, targetMaxCpu) = ComputeThermalTarget(cpuTemp, curve);

            if (targetCooling == _lastAppliedCooling && targetMaxCpu == _lastAppliedMaxCpu && IsActive)
                return;

            bool changed = false;
            if (targetCooling != _lastAppliedCooling)
            {
                PowerWriteACValueIndex(IntPtr.Zero, ref activeScheme, ref SubProcessor, ref SystemCoolingPolicy, targetCooling);
                PowerWriteDCValueIndex(IntPtr.Zero, ref activeScheme, ref SubProcessor, ref SystemCoolingPolicy, targetCooling);
                _lastAppliedCooling = targetCooling;
                changed = true;
            }

            if (targetMaxCpu != _lastAppliedMaxCpu)
            {
                PowerWriteACValueIndex(IntPtr.Zero, ref activeScheme, ref SubProcessor, ref ProcThrottleMax, targetMaxCpu);
                PowerWriteDCValueIndex(IntPtr.Zero, ref activeScheme, ref SubProcessor, ref ProcThrottleMax, targetMaxCpu);
                _lastAppliedMaxCpu = targetMaxCpu;
                changed = true;
            }

            if (changed)
            {
                PowerSetActiveScheme(IntPtr.Zero, ref activeScheme);
            }

            IsActive = true;
            string polName = targetCooling == 0 ? "Passivo (Silencioso)" : "Ativo (Resfriamento)";
            StatusSummary = $"{polName} · Teto CPU {targetMaxCpu}%";
        }
        catch
        {
            StatusSummary = "Indisponível neste plano de energia";
        }
        finally
        {
            if (schemePtr != IntPtr.Zero) LocalFree(schemePtr);
        }
    }

    private void CaptureSnapshot(Guid scheme)
    {
        _savedSchemeGuid = scheme;
        _origAcCoolingPolicy = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref SystemCoolingPolicy, out uint acCool) == 0 ? acCool : null;
        _origDcCoolingPolicy = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref SystemCoolingPolicy, out uint dcCool) == 0 ? dcCool : null;
        _origAcThrottleMax = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref ProcThrottleMax, out uint acMax) == 0 ? acMax : null;
        _origDcThrottleMax = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref ProcThrottleMax, out uint dcMax) == 0 ? dcMax : null;
        _hasSnapshot = true;
        _lastAppliedCooling = uint.MaxValue;
        _lastAppliedMaxCpu = uint.MaxValue;
    }

    public void RestoreOriginal()
    {
        if (!_hasSnapshot)
        {
            IsActive = false;
            StatusSummary = "Inativo (gerenciado pelo plano do Windows)";
            return;
        }

        try
        {
            Guid scheme = _savedSchemeGuid;
            if (_origAcCoolingPolicy.HasValue)
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref SystemCoolingPolicy, _origAcCoolingPolicy.Value);
            if (_origDcCoolingPolicy.HasValue)
                PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref SystemCoolingPolicy, _origDcCoolingPolicy.Value);
            if (_origAcThrottleMax.HasValue)
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref ProcThrottleMax, _origAcThrottleMax.Value);
            if (_origDcThrottleMax.HasValue)
                PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref ProcThrottleMax, _origDcThrottleMax.Value);

            PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        }
        catch { }
        finally
        {
            _hasSnapshot = false;
            _lastAppliedCooling = uint.MaxValue;
            _lastAppliedMaxCpu = uint.MaxValue;
            IsActive = false;
            StatusSummary = "Restaurado para o plano do Windows";
        }
    }

    public void Dispose() => RestoreOriginal();

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint AcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint DcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint AcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint DcValueIndex);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
