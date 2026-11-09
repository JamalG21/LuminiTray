using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using NvAPIWrapper.GPU;

namespace LuminiTray;

public enum FanSourceType
{
    Motherboard,
    NvidiaGpu,
    AmdGpu,
    IntelGpu
}

public enum CurveTempSource
{
    Cpu,
    Gpu,
    MixedMax // Maior entre CPU e GPU
}

public sealed class FanCurvePoint
{
    public double Temp { get; set; }
    public double Pwm { get; set; }

    public FanCurvePoint(double temp, double pwm)
    {
        Temp = Math.Clamp(temp, 0, 105);
        Pwm = Math.Clamp(pwm, 0, 100);
    }
}

public sealed class FanCurve
{
    public string Name { get; set; } = "Equilibrada";
    public CurveTempSource TempSource { get; set; } = CurveTempSource.Cpu;
    public double RampUpSeconds { get; set; } = 4.0;   // 3 a 5s para absorver picos rápidos (ex: Ryzen X3D)
    public double RampDownSeconds { get; set; } = 8.0; // 8s descida gradual sem oscilação
    public List<FanCurvePoint> Points { get; set; } = new();

    public FanCurve()
    {
        // Curva padrão suave de 6 pontos para controle fino e preciso
        Points.Add(new FanCurvePoint(25, 20));
        Points.Add(new FanCurvePoint(40, 30));
        Points.Add(new FanCurvePoint(55, 45));
        Points.Add(new FanCurvePoint(68, 60));
        Points.Add(new FanCurvePoint(78, 80));
        Points.Add(new FanCurvePoint(88, 100));
    }

    public static FanCurve CreateQuiet(CurveTempSource source = CurveTempSource.Cpu) => new()
    {
        Name = "Silencioso",
        TempSource = source,
        RampUpSeconds = 5.0,
        RampDownSeconds = 10.0,
        Points = new List<FanCurvePoint>
        {
            new(30, 15),
            new(45, 25),
            new(60, 38),
            new(72, 50),
            new(82, 75),
            new(90, 100)
        }
    };

    public static FanCurve CreatePerformance(CurveTempSource source = CurveTempSource.Cpu) => new()
    {
        Name = "Desempenho",
        TempSource = source,
        RampUpSeconds = 2.0,
        RampDownSeconds = 6.0,
        Points = new List<FanCurvePoint>
        {
            new(25, 35),
            new(40, 50),
            new(55, 65),
            new(68, 80),
            new(78, 95),
            new(85, 100)
        }
    };

    public static FanCurve CreateGpuDefault() => new()
    {
        Name = "Padrão GPU",
        TempSource = CurveTempSource.Gpu,
        RampUpSeconds = 2.0,
        RampDownSeconds = 6.0,
        Points = new List<FanCurvePoint>
        {
            new(35, 0),   // Zero RPM em repouso
            new(48, 30),  // Acionamento suave
            new(60, 45),  // Carga moderada
            new(70, 62),  // Jogos exigentes
            new(78, 82),  // Carga pesada
            new(85, 100)  // Limite térmico
        }
    };

    public double Evaluate(double temp)
    {
        if (Points.Count == 0) return 50.0;
        var sorted = Points.OrderBy(p => p.Temp).ToList();
        if (temp <= sorted[0].Temp) return sorted[0].Pwm;
        if (temp >= sorted[^1].Temp) return sorted[^1].Pwm;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var p1 = sorted[i];
            var p2 = sorted[i + 1];
            if (temp >= p1.Temp && temp <= p2.Temp)
            {
                double range = p2.Temp - p1.Temp;
                if (range <= 0.001) return p1.Pwm;
                double factor = (temp - p1.Temp) / range;
                return p1.Pwm + factor * (p2.Pwm - p1.Pwm);
            }
        }
        return sorted[^1].Pwm;
    }
}

public sealed class FanChannel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string HardwareName { get; set; } = "";
    public FanSourceType SourceType { get; set; }
    public ISensor? LhmControlSensor { get; set; }
    public ISensor? LhmRpmSensor { get; set; }
    public int GpuIndex { get; set; }
    public int CoolerId { get; set; }

    public float CurrentRpm { get; set; }
    public float CurrentPwm { get; set; }
    public float TargetPwm { get; set; }
    public bool IsManual { get; set; }
    public bool IsLocked100 { get; set; } // Trava de segurança para Bomba de Water Cooler (AIO)
    public DateTime LastPulseTime { get; set; } = DateTime.MinValue; // Heartbeat anti-timeout para BIOS / Super I/O

    public FanCurve Curve { get; set; } = new();
    public double SmoothedTemp { get; set; } = 40.0;
    public double AppliedFilteredPwm { get; set; } = 40.0;
    public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;

    public void SetPwm(float percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        TargetPwm = percent;

        if (SourceType == FanSourceType.Motherboard)
        {
            try
            {
                LhmControlSensor?.Control?.SetSoftware(percent);
                CurrentPwm = percent;
            }
            catch { }
        }
        else if (SourceType == FanSourceType.NvidiaGpu)
        {
            try
            {
                var gpus = PhysicalGPU.GetPhysicalGPUs();
                if (GpuIndex >= 0 && GpuIndex < gpus.Length)
                {
                    gpus[GpuIndex].CoolerInformation.SetCoolerSettings(CoolerId, (int)Math.Round(percent));
                    CurrentPwm = percent;
                }
            }
            catch { }
        }
        else if (SourceType == FanSourceType.AmdGpu)
        {
            try
            {
                AdlNative.SetFanSpeedPercent(GpuIndex, (int)Math.Round(percent));
                CurrentPwm = percent;
            }
            catch { }
        }
        else if (SourceType == FanSourceType.IntelGpu)
        {
            try
            {
                IgclNative.SetFanSpeed(GpuIndex, (int)Math.Round(percent));
                CurrentPwm = percent;
            }
            catch { }
        }
    }

    public void RestoreDefault()
    {
        IsManual = false;
        IsLocked100 = false;

        if (SourceType == FanSourceType.Motherboard)
        {
            try
            {
                LhmControlSensor?.Control?.SetDefault();
            }
            catch { }
        }
        else if (SourceType == FanSourceType.NvidiaGpu)
        {
            try
            {
                var gpus = PhysicalGPU.GetPhysicalGPUs();
                if (GpuIndex >= 0 && GpuIndex < gpus.Length)
                {
                    gpus[GpuIndex].CoolerInformation.RestoreCoolerSettingsToDefault(CoolerId);
                }
            }
            catch { }
        }
        else if (SourceType == FanSourceType.AmdGpu)
        {
            try
            {
                AdlNative.RestoreFanSpeedToDefault(GpuIndex);
            }
            catch { }
        }
        else if (SourceType == FanSourceType.IntelGpu)
        {
            try
            {
                // Em Intel IGCL, 0 ou default restaura
                IgclNative.SetFanSpeed(GpuIndex, 0);
            }
            catch { }
        }
    }
}

/// <summary>
/// Serviço central de controle de ventoinhas com motor de curvas, histerese anti-spike e fail-safe de BIOS.
/// </summary>
public sealed class FanControlService : IDisposable
{
    private readonly SensorMonitor _monitor;
    private readonly Computer _computer;
    private readonly ObservableCollection<FanChannel> _channels = new();
    private readonly object _syncLock = new();
    private bool _isInitialized;
    private bool _disposed;
    private ISensor? _gpuFanRpmSensor;
    private System.Threading.Timer? _backgroundTimer;

    public ObservableCollection<FanChannel> Channels => _channels;
    public double LastCpuTemp { get; private set; } = 40.0;
    public double LastGpuTemp { get; private set; } = 40.0;

    /// <summary>
    /// Indica se o controle inteligente de software está ativo.
    /// Se falso, as ventoinhas operam sob controle nativo da BIOS e firmware dos fabricantes.
    /// </summary>
    public bool IsControlActive { get; private set; } = true;

    public static FanControlService? Instance { get; private set; }

    public FanCurve CpuCurve { get; set; } = new() { Name = "Curva CPU", TempSource = CurveTempSource.Cpu };
    public FanCurve GpuCurve { get; set; } = FanCurve.CreateGpuDefault();

    public FanControlService(SensorMonitor monitor)
    {
        Instance = this;
        _monitor = monitor;
        _computer = monitor.GetComputer();
        CpuCurve.TempSource = CurveTempSource.Cpu;
        GpuCurve.TempSource = CurveTempSource.Gpu;
    }

    /// <summary>
    /// Alterna entre controle de ventoinhas por software e devolução integral à BIOS/Firmware.
    /// </summary>
    public void SetControlActive(bool active)
    {
        lock (_syncLock)
        {
            IsControlActive = active;
            if (active)
            {
                StartBackgroundLoop();
            }
            else
            {
                RestoreAllToBios();
                StopBackgroundLoop();
            }
        }
    }

    public void DiscoverFans()
    {
        lock (_syncLock)
        {
            _channels.Clear();
            _gpuFanRpmSensor = null;

            // 1. Placa-mãe Super I/O e sensores de telemetria via LibreHardwareMonitor
            // Sincroniza com o lock do SensorMonitor para evitar concorrência no barramento LPC/SMBus
            try
            {
                lock (_monitor.SyncLock)
                {
                    foreach (var hw in _computer.Hardware)
                    {
                        hw.Update();
                        WalkHardware(hw);
                    }
                }
            }
            catch { }

            // 2. GPU NVIDIA via NvAPIWrapper (Controle real de cooler PWM)
            try
            {
                var nvgpus = PhysicalGPU.GetPhysicalGPUs();
                for (int i = 0; i < nvgpus.Length; i++)
                {
                    var gpu = nvgpus[i];
                    var coolerInfo = gpu.CoolerInformation;
                    var coolers = coolerInfo.Coolers.ToList();
                    int cIndex = 1;
                    foreach (var cooler in coolers)
                    {
                        var ch = new FanChannel
                        {
                            Id = $"GPU_NV_{i}_{cooler.CoolerId}",
                            Name = coolers.Count > 1 ? $"Ventoinha {cIndex++} (GPU)" : "Ventoinha da GPU",
                            HardwareName = gpu.FullName,
                            SourceType = FanSourceType.NvidiaGpu,
                            GpuIndex = i,
                            CoolerId = cooler.CoolerId,
                            CurrentPwm = cooler.CurrentLevel,
                            TargetPwm = cooler.CurrentLevel,
                            LhmRpmSensor = _gpuFanRpmSensor,
                            CurrentRpm = _gpuFanRpmSensor?.Value ?? 0f,
                            Curve = GpuCurve
                        };
                        _channels.Add(ch);
                    }
                }
            }
            catch { }

            // 3. GPU AMD via AdlNative
            try
            {
                if (AdlNative.IsAvailable)
                {
                    var adapters = AdlNative.Adapters;
                    for (int i = 0; i < adapters.Length; i++)
                    {
                        int currentPwm = AdlNative.GetFanSpeedPercent(adapters[i].AdapterIndex);
                        if (currentPwm >= 0)
                        {
                            var ch = new FanChannel
                            {
                                Id = $"GPU_AMD_{adapters[i].AdapterIndex}",
                                Name = "Ventoinha da GPU (AMD Radeon)",
                                HardwareName = adapters[i].AdapterName,
                                SourceType = FanSourceType.AmdGpu,
                                GpuIndex = adapters[i].AdapterIndex,
                                CurrentPwm = currentPwm,
                                TargetPwm = currentPwm,
                                LhmRpmSensor = _gpuFanRpmSensor,
                                CurrentRpm = _gpuFanRpmSensor?.Value ?? 0f,
                                Curve = GpuCurve
                            };
                            _channels.Add(ch);
                        }
                    }
                }
            }
            catch { }

            // 4. GPU Intel Arc/Xe via IgclNative
            try
            {
                if (IgclNative.IsAvailable && IgclNative.DeviceCount > 0)
                {
                    for (int i = 0; i < IgclNative.DeviceCount; i++)
                    {
                        var ch = new FanChannel
                        {
                            Id = $"GPU_INTEL_{i}",
                            Name = "Ventoinha da GPU (Intel Arc/Xe)",
                            HardwareName = "Intel Graphics",
                            SourceType = FanSourceType.IntelGpu,
                            GpuIndex = i,
                            CurrentPwm = 50,
                            TargetPwm = 50,
                            LhmRpmSensor = _gpuFanRpmSensor,
                            CurrentRpm = _gpuFanRpmSensor?.Value ?? 0f,
                            Curve = GpuCurve
                        };
                        _channels.Add(ch);
                    }
                }
            }
            catch { }

            _isInitialized = true;
        }
    }

    private void WalkHardware(IHardware hw)
    {
        // Se for hardware de GPU, captura o sensor de tacômetro (RPM) para associar ao canal oficial da GPU
        // e NÃO adiciona como canal de placa-mãe para evitar duplicatas sem controle.
        if (hw.HardwareType == HardwareType.GpuNvidia ||
            hw.HardwareType == HardwareType.GpuAmd ||
            hw.HardwareType == HardwareType.GpuIntel)
        {
            var gpuFan = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan);
            if (gpuFan != null)
            {
                _gpuFanRpmSensor = gpuFan;
            }
            return;
        }

        // Encontra controles e fans associados na placa-mãe (Super I/O)
        var controls = hw.Sensors.Where(s => s.SensorType == SensorType.Control).ToList();
        var fans = hw.Sensors.Where(s => s.SensorType == SensorType.Fan).ToList();

        foreach (var ctrl in controls)
        {
            // Tenta parear com o sensor de RPM mais próximo pelo identificador ou nome
            var matchedFan = fans.FirstOrDefault(f => f.Name.Contains(ctrl.Name) || ctrl.Name.Contains(f.Name))
                             ?? fans.FirstOrDefault();

            float rpm = matchedFan?.Value ?? 0f;
            float pwm = ctrl.Value ?? 0f;

            var channel = new FanChannel
            {
                Id = ctrl.Identifier.ToString(),
                Name = ctrl.Name,
                HardwareName = hw.Name,
                SourceType = FanSourceType.Motherboard,
                LhmControlSensor = ctrl,
                LhmRpmSensor = matchedFan,
                CurrentRpm = rpm,
                CurrentPwm = pwm,
                TargetPwm = pwm,
                Curve = CpuCurve
            };
            _channels.Add(channel);
        }

        foreach (var subHw in hw.SubHardware)
        {
            subHw.Update();
            WalkHardware(subHw);
        }
    }

    /// <summary>
    /// Ciclo de atualização periódico: lê sensores unificados do SensorMonitor, calcula histerese anti-spike e injeta PWM.
    /// </summary>
    public void Tick()
    {
        if (!_isInitialized || _disposed) return;

        UpdateHardwareTemps();

        foreach (var ch in _channels)
        {
            if (ch.LhmRpmSensor?.Value != null)
            {
                ch.CurrentRpm = ch.LhmRpmSensor.Value.Value;
            }
            if (!IsControlActive && ch.LhmControlSensor?.Value != null)
            {
                ch.CurrentPwm = ch.LhmControlSensor.Value.Value;
            }
        }

        // Se o recurso estiver desativado, apenas mantivemos a telemetria sem interferir na BIOS
        if (!IsControlActive) return;

        foreach (var ch in _channels)
        {
            // Se for trava de 100% contínuo (Bomba de Water Cooler / AIO)
            if (ch.IsLocked100)
            {
                var now = DateTime.UtcNow;
                if (ch.CurrentPwm < 99.5f || (now - ch.LastPulseTime).TotalSeconds >= 2.0)
                {
                    ch.SetPwm(100f);
                    ch.LastPulseTime = now;
                }
                continue;
            }

            // Se o usuário estiver no modo manual por slider
            if (ch.IsManual)
            {
                var now = DateTime.UtcNow;
                if (Math.Abs(ch.CurrentPwm - ch.TargetPwm) >= 0.8f || (now - ch.LastPulseTime).TotalSeconds >= 5.0)
                {
                    ch.SetPwm(ch.TargetPwm);
                    ch.LastPulseTime = now;
                }
                continue;
            }

            // Define a curva ativa para este canal (CPU vs GPU)
            var activeCurve = ch.SourceType == FanSourceType.Motherboard ? CpuCurve : GpuCurve;
            ch.Curve = activeCurve;

            // Modo automático por curva com HISTERESE e TEMPO DE RESPOSTA ANTI-SPIKE
            double targetTemp = activeCurve.TempSource switch
            {
                CurveTempSource.Cpu => LastCpuTemp,
                CurveTempSource.Gpu => LastGpuTemp,
                CurveTempSource.MixedMax => Math.Max(LastCpuTemp, LastGpuTemp),
                _ => ch.SourceType == FanSourceType.Motherboard ? LastCpuTemp : LastGpuTemp
            };

            // Cálculo do tempo decorrido desde o último tick
            DateTime utcNow = DateTime.UtcNow;
            double dt = (utcNow - ch.LastUpdateTime).TotalSeconds;
            ch.LastUpdateTime = utcNow;
            if (dt <= 0.001 || dt > 5.0) dt = 1.0;

            // Filtro de Histerese (Ramp-up e Ramp-down assimétricos):
            double rampTime = targetTemp > ch.SmoothedTemp
                ? Math.Max(1.0, activeCurve.RampUpSeconds)
                : Math.Max(1.0, activeCurve.RampDownSeconds);

            double alpha = Math.Clamp(dt / rampTime, 0.05, 1.0);
            ch.SmoothedTemp = ch.SmoothedTemp + alpha * (targetTemp - ch.SmoothedTemp);

            // Avalia a curva com a temperatura suavizada anti-spike
            double targetCurvePwm = activeCurve.Evaluate(ch.SmoothedTemp);
            float targetFloat = (float)Math.Round(targetCurvePwm);

            // Injeta no controlador somente se houver variação (evita saturar barramento/driver)
            if (Math.Abs(ch.CurrentPwm - targetFloat) >= 0.8f)
            {
                ch.SetPwm(targetFloat);
            }
        }
    }

    private void UpdateHardwareTemps()
    {
        // Usa a Fonte Única de Verdade (SensorMonitor) para garantir paridade de 100%
        // com a Aba Sensores, Dashboard, Sidebar, Overlay e HWMonitor, sem concorrência no barramento LPC/SMBus.
        var cpuTemp = _monitor.CpuTemperature;
        if (cpuTemp.HasValue && cpuTemp.Value > 0)
        {
            LastCpuTemp = cpuTemp.Value;
        }

        var gpuTemp = _monitor.GpuTemperature;
        if (gpuTemp.HasValue && gpuTemp.Value > 0)
        {
            LastGpuTemp = gpuTemp.Value;
        }
    }

    /// <summary>
    /// Fail-safe crítico: devolve o controle imediato de todas as ventoinhas para a BIOS e padrão dos drivers.
    /// Chamado ao encerrar o aplicativo ou fechar a janela.
    /// </summary>
    public void RestoreAllToBios()
    {
        lock (_syncLock)
        {
            foreach (var ch in _channels)
            {
                try
                {
                    ch.RestoreDefault();
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Inicia o ciclo de atualização em segundo plano desacoplado da interface gráfica.
    /// Continua executando mesmo quando a janela estiver minimizada ou oculta na bandeja.
    /// </summary>
    public void StartBackgroundLoop(int intervalMs = 1000)
    {
        lock (_syncLock)
        {
            _backgroundTimer?.Dispose();
            _backgroundTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    Tick();
                }
                catch { }
            }, null, intervalMs, intervalMs);
        }
    }

    public void StopBackgroundLoop()
    {
        lock (_syncLock)
        {
            _backgroundTimer?.Dispose();
            _backgroundTimer = null;
        }
    }

    /// <summary>
    /// Restaura curvas e configurações personalizadas salvas pelo usuário em AppSettings.
    /// </summary>
    public void ApplySettings(FanControlSaveSettings? settings)
    {
        if (settings == null) return;
        lock (_syncLock)
        {
            IsControlActive = settings.IsControlActive;

            if (settings.CpuCurve != null && settings.CpuCurve.Points.Count >= 2)
            {
                CpuCurve.Name = settings.CpuCurve.Name;
                CpuCurve.RampUpSeconds = Math.Max(1.0, settings.CpuCurve.RampUpSeconds);
                CpuCurve.RampDownSeconds = Math.Max(1.0, settings.CpuCurve.RampDownSeconds);
                if (Enum.TryParse<CurveTempSource>(settings.CpuCurve.TempSource, out var src))
                    CpuCurve.TempSource = src;
                CpuCurve.Points = settings.CpuCurve.Points.Select(p => new FanCurvePoint(p.Temp, p.Pwm)).ToList();
            }

            if (settings.GpuCurve != null && settings.GpuCurve.Points.Count >= 2)
            {
                GpuCurve.Name = settings.GpuCurve.Name;
                GpuCurve.RampUpSeconds = Math.Max(1.0, settings.GpuCurve.RampUpSeconds);
                GpuCurve.RampDownSeconds = Math.Max(1.0, settings.GpuCurve.RampDownSeconds);
                if (Enum.TryParse<CurveTempSource>(settings.GpuCurve.TempSource, out var src))
                    GpuCurve.TempSource = src;
                GpuCurve.Points = settings.GpuCurve.Points.Select(p => new FanCurvePoint(p.Temp, p.Pwm)).ToList();
            }

            foreach (var ch in _channels)
            {
                if (settings.Channels.TryGetValue(ch.Id, out var state))
                {
                    ch.IsLocked100 = state.IsLocked100;
                    ch.IsManual = state.IsManual;
                    ch.TargetPwm = state.TargetPwm;
                    if (ch.IsLocked100)
                    {
                        ch.SetPwm(100f);
                        ch.LastPulseTime = DateTime.UtcNow;
                    }
                    else if (ch.IsManual)
                    {
                        ch.SetPwm(ch.TargetPwm);
                        ch.LastPulseTime = DateTime.UtcNow;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Exporta o estado atual das curvas e canais para serialização no AppSettings.
    /// </summary>
    public FanControlSaveSettings ExportSettings()
    {
        lock (_syncLock)
        {
            var s = new FanControlSaveSettings
            {
                IsControlActive = IsControlActive,
                CpuCurve = new FanCurveSaveConfig
                {
                    Name = CpuCurve.Name,
                    TempSource = CpuCurve.TempSource.ToString(),
                    RampUpSeconds = CpuCurve.RampUpSeconds,
                    RampDownSeconds = CpuCurve.RampDownSeconds,
                    Points = CpuCurve.Points.Select(p => new FanCurvePointSave(p.Temp, p.Pwm)).ToList()
                },
                GpuCurve = new FanCurveSaveConfig
                {
                    Name = GpuCurve.Name,
                    TempSource = GpuCurve.TempSource.ToString(),
                    RampUpSeconds = GpuCurve.RampUpSeconds,
                    RampDownSeconds = GpuCurve.RampDownSeconds,
                    Points = GpuCurve.Points.Select(p => new FanCurvePointSave(p.Temp, p.Pwm)).ToList()
                }
            };

            foreach (var ch in _channels)
            {
                s.Channels[ch.Id] = new FanChannelSaveState
                {
                    IsLocked100 = ch.IsLocked100,
                    IsManual = ch.IsManual,
                    TargetPwm = ch.TargetPwm
                };
            }

            return s;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopBackgroundLoop();
        RestoreAllToBios();
    }
}
