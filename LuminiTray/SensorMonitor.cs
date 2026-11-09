using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using LibreHardwareMonitor.Hardware;

namespace LuminiTray;

/// <summary>
/// Fonte única de verdade (Single Source of Truth) para sensores de hardware via LibreHardwareMonitorLib.
/// Garante que todas as abas (Sensores, Visão Geral, Controle de Fans, Gargalo, Overlay, Estresse e Barra Lateral)
/// exibam exatamente a mesma leitura de temperatura, alinhada com o padrão do CPUID HWMonitor.
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly Dictionary<string, SensorRow> _index = new();
    private volatile bool _open;
    private volatile bool _isRefreshing;
    private readonly object _syncLock = new();

    private SensorRow? _mainCpuTempRow;
    private int _mainCpuTempPriority = -1;

    private SensorRow? _mainGpuTempRow;
    private int _mainGpuTempPriority = -1;

    private SensorRow? _mainCpuLoadRow;
    private SensorRow? _mainCpuPowerRow;

    public ObservableCollection<SensorRow> Rows { get; } = new();
    public bool IsOpen => _open;
    public Computer Computer => _computer;
    public Computer GetComputer() => _computer;
    public object SyncLock => _syncLock;

    /// <summary>Nome da GPU dedicada principal (ex.: _gpus[0].Name) para desempate em sistemas com iGPU + dGPU.</summary>
    public string? PrimaryGpuName { get; set; }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ISensor, string> _sensorIdCache = new();
    private static long _lastStorageUpdateTicks;

    private readonly record struct SensorSnapshot(
        string Id,
        string HardwareName,
        HardwareType HardwareType,
        string SensorName,
        SensorType SensorType,
        float? Value,
        float? Min,
        float? Max);

    public SensorMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false,
            IsControllerEnabled = false
        };
    }

    /// <summary>Detecta o hardware fora da thread da janela.</summary>
    public Task OpenAsync() => Task.Run(() =>
    {
        lock (_syncLock)
        {
            _computer.Open();
            _open = true;
        }
    });

    /// <summary>
    /// Atualização assíncrona: executa o I/O de hardware (hw.Update) em background
    /// e aplica as atualizações nas linhas (SensorRow) na thread da UI em lote único,
    /// garantindo sincronia total entre a aba Sensores e o restante do programa.
    /// Em segundo plano (updateUi = false), atualiza apenas os valores numéricos sem acionar o WPF.
    /// </summary>
    public async Task RefreshAsync(bool updateUi = true)
    {
        if (!_open || _isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var snapshots = await Task.Run(() =>
            {
                var list = new List<SensorSnapshot>(128);
                lock (_syncLock)
                {
                    foreach (var hw in _computer.Hardware)
                    {
                        CollectSnapshots(hw, list);
                    }
                }
                return list;
            });

            if (updateUi)
            {
                if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                {
                    await dispatcher.InvokeAsync(() => ApplySnapshots(snapshots));
                }
                else
                {
                    ApplySnapshots(snapshots);
                }
            }
            else
            {
                ApplySnapshotsRaw(snapshots);
            }
        }
        catch { }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Leitura ultra-leve de sensores essenciais em segundo plano:
    /// Atualiza apenas temperaturas e rotações (Fans) de CPU e GPU para controle contínuo de ventoinhas.
    /// Ignora placa-mãe, memória, discos e rede, consumindo CPU e memória mínimos.
    /// </summary>
    public async Task RefreshEssentialTempsOnlyAsync()
    {
        if (!_open || _isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var snapshots = await Task.Run(() =>
            {
                var list = new List<SensorSnapshot>(32);
                lock (_syncLock)
                {
                    foreach (var hw in _computer.Hardware)
                    {
                        if (hw.HardwareType is HardwareType.Cpu or HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                        {
                            CollectEssentialSnapshots(hw, list);
                        }
                    }
                }
                return list;
            });

            ApplySnapshotsRaw(snapshots);
        }
        catch { }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static void CollectEssentialSnapshots(IHardware hw, List<SensorSnapshot> list)
    {
        try { hw.Update(); } catch { }

        foreach (var s in hw.Sensors)
        {
            if (s.SensorType is SensorType.Temperature or SensorType.Fan or SensorType.Control)
            {
                if (s.SensorType == SensorType.Temperature &&
                    s.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string sensorId = _sensorIdCache.GetValue(s, static sensor => sensor.Identifier.ToString());
                list.Add(new SensorSnapshot(
                    sensorId,
                    hw.Name,
                    hw.HardwareType,
                    s.Name,
                    s.SensorType,
                    s.Value,
                    s.Min,
                    s.Max));
            }
        }

        foreach (var sub in hw.SubHardware)
        {
            CollectEssentialSnapshots(sub, list);
        }
    }

    public void Refresh()
    {
        if (!_open || _isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var list = new List<SensorSnapshot>(128);
            lock (_syncLock)
            {
                foreach (var hw in _computer.Hardware)
                {
                    CollectSnapshots(hw, list);
                }
            }

            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => ApplySnapshots(list));
            }
            else
            {
                ApplySnapshots(list);
            }
        }
        catch { }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static void CollectSnapshots(IHardware hw, List<SensorSnapshot> list)
    {
        if (hw.HardwareType == HardwareType.Storage)
        {
            if (Stopwatch.GetElapsedTime(_lastStorageUpdateTicks).TotalSeconds >= 10.0)
            {
                _lastStorageUpdateTicks = Stopwatch.GetTimestamp();
                try { hw.Update(); } catch { }
            }
        }
        else
        {
            try { hw.Update(); } catch { }
        }

        foreach (var s in hw.Sensors)
        {
            // Ignora sensores "Distance to TjMax" (margem até o limite térmico, ex: 100°C - Temp),
            // pois o LibreHardwareMonitor os classifica como SensorType.Temperature, o que confunde a leitura
            // e diverge do HWMonitor.
            if (s.SensorType == SensorType.Temperature &&
                s.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string sensorId = _sensorIdCache.GetValue(s, static sensor => sensor.Identifier.ToString());

            list.Add(new SensorSnapshot(
                sensorId,
                hw.Name,
                hw.HardwareType,
                s.Name,
                s.SensorType,
                s.Value,
                s.Min,
                s.Max));
        }

        foreach (var sub in hw.SubHardware)
        {
            CollectSnapshots(sub, list);
        }
    }

    private void ApplySnapshots(List<SensorSnapshot> snapshots)
    {
        foreach (var snap in snapshots)
        {
            if (!_index.TryGetValue(snap.Id, out var row))
            {
                row = new SensorRow
                {
                    Id = snap.Id,
                    Hardware = snap.HardwareName,
                    Category = SensorRow.CategoryName(snap.SensorType),
                    Name = snap.SensorName,
                    Type = snap.SensorType,
                    HardwareType = snap.HardwareType
                };
                _index[snap.Id] = row;
                Rows.Add(row);
            }

            row.Update(snap.Value, snap.Min, snap.Max);

            // Avalia prioridade para sensor principal de Temperatura da CPU (Padrão HWMonitor: Package / Core (Tctl/Tdie))
            if (snap.SensorType == SensorType.Temperature && snap.Value is > 0 and < 130)
            {
                int cpuPrio = GetCpuTempPriority(snap.SensorName, snap.HardwareType);
                if (cpuPrio > _mainCpuTempPriority)
                {
                    _mainCpuTempPriority = cpuPrio;
                    _mainCpuTempRow = row;
                }

                int gpuPrio = GetGpuTempPriority(snap.SensorName, snap.HardwareName, snap.HardwareType);
                if (gpuPrio > _mainGpuTempPriority)
                {
                    _mainGpuTempPriority = gpuPrio;
                    _mainGpuTempRow = row;
                }
            }
            else if (snap.HardwareType == HardwareType.Cpu)
            {
                if (snap.SensorType == SensorType.Load && snap.SensorName == "CPU Total")
                {
                    _mainCpuLoadRow = row;
                }
                else if (snap.SensorType == SensorType.Power &&
                         (snap.SensorName.Contains("Package", StringComparison.OrdinalIgnoreCase) || _mainCpuPowerRow == null))
                {
                    _mainCpuPowerRow = row;
                }
            }
        }
    }

    /// <summary>
    /// Aplica dados apenas na estrutura de dados em memória para manter o FanControl atualizado,
    /// sem acionar eventos WPF nem gastar ciclos com formatação de strings na interface oculta.
    /// </summary>
    private void ApplySnapshotsRaw(List<SensorSnapshot> snapshots)
    {
        foreach (var snap in snapshots)
        {
            if (_index.TryGetValue(snap.Id, out var row))
            {
                row.UpdateRawOnly(snap.Value, snap.Min, snap.Max);

                if (snap.SensorType == SensorType.Temperature && snap.Value is > 0 and < 130)
                {
                    int cpuPrio = GetCpuTempPriority(snap.SensorName, snap.HardwareType);
                    if (cpuPrio > _mainCpuTempPriority)
                    {
                        _mainCpuTempPriority = cpuPrio;
                        _mainCpuTempRow = row;
                    }

                    int gpuPrio = GetGpuTempPriority(snap.SensorName, snap.HardwareName, snap.HardwareType);
                    if (gpuPrio > _mainGpuTempPriority)
                    {
                        _mainGpuTempPriority = gpuPrio;
                        _mainGpuTempRow = row;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Calcula a prioridade do sensor de temperatura da CPU seguindo exatamente a hierarquia do CPUID HWMonitor:
    /// 1º Core (Tctl/Tdie) [AMD Package] ou CPU Package [Intel Package]
    /// 2º Core Max / Tctl
    /// 3º CCD1 (Tdie) / Tdie
    /// 4º CPU Core #1..N
    /// 5º Sensor CPU da Placa-Mãe (Super I/O) como fallback caso MSR/SMN não esteja disponível
    /// </summary>
    public static int GetCpuTempPriority(string sensorName, HardwareType hwType)
    {
        if (sensorName.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
            return -1;

        if (hwType == HardwareType.Cpu)
        {
            if (sensorName.Equals("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase) ||
                sensorName.Equals("CPU Package", StringComparison.OrdinalIgnoreCase))
                return 100;

            if (sensorName.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase))
                return 95;

            if (sensorName.Equals("Core Max", StringComparison.OrdinalIgnoreCase) ||
                sensorName.Contains("Core Max", StringComparison.OrdinalIgnoreCase))
                return 85;

            if (sensorName.Equals("CCD1 (Tdie)", StringComparison.OrdinalIgnoreCase))
                return 78;

            if (sensorName.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
                return 72;

            if (sensorName.Contains("Cores (Average)", StringComparison.OrdinalIgnoreCase) ||
                sensorName.Contains("Core Average", StringComparison.OrdinalIgnoreCase))
                return 68;

            if (sensorName.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase) ||
                sensorName.StartsWith("Core #", StringComparison.OrdinalIgnoreCase))
                return 60;

            return 50;
        }

        if (hwType is HardwareType.Motherboard or HardwareType.SuperIO)
        {
            if (sensorName.Equals("CPU", StringComparison.OrdinalIgnoreCase) ||
                sensorName.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) ||
                sensorName.Equals("CPU Socket", StringComparison.OrdinalIgnoreCase) ||
                sensorName.Equals("CPUTIN", StringComparison.OrdinalIgnoreCase))
                return 30;
        }

        return -1;
    }

    /// <summary>
    /// Calcula a prioridade do sensor de temperatura da GPU principal seguindo o padrão HWMonitor / GPU-Z:
    /// Prioriza a GPU dedicada principal e o sensor "GPU Core" (evitando que Hot Spot, Memory ou iGPU sobrescrevam).
    /// </summary>
    public int GetGpuTempPriority(string sensorName, string hwName, HardwareType hwType)
    {
        if (hwType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
            return -1;

        int baseScore = 0;

        // Se sabemos o nome da GPU principal dedicada, dá prioridade máxima a ela
        if (!string.IsNullOrWhiteSpace(PrimaryGpuName))
        {
            if (hwName.Equals(PrimaryGpuName, StringComparison.OrdinalIgnoreCase) ||
                hwName.Contains(PrimaryGpuName, StringComparison.OrdinalIgnoreCase) ||
                PrimaryGpuName.Contains(hwName, StringComparison.OrdinalIgnoreCase))
            {
                baseScore = 200;
            }
        }

        // Desempate por tipo de GPU (NVIDIA dedicada > AMD > Intel) se PrimaryGpuName não bateu
        if (baseScore == 0)
        {
            baseScore = hwType switch
            {
                HardwareType.GpuNvidia => 100,
                HardwareType.GpuAmd => !hwName.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ? 90 : 30,
                HardwareType.GpuIntel => hwName.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? 80 : 20,
                _ => 10
            };
        }

        int sensorScore;
        if (sensorName.Equals("GPU Core", StringComparison.OrdinalIgnoreCase))
            sensorScore = 50;
        else if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                 !sensorName.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase))
            sensorScore = 45;
        else if (!sensorName.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) &&
                 !sensorName.Contains("Memory", StringComparison.OrdinalIgnoreCase) &&
                 !sensorName.Contains("Memória", StringComparison.OrdinalIgnoreCase) &&
                 !sensorName.Contains("VR", StringComparison.OrdinalIgnoreCase))
            sensorScore = 35;
        else
            sensorScore = 10;

        return baseScore + sensorScore;
    }

    /// <summary>Temperatura principal da CPU (Package / Core Tctl-Tdie), idêntica ao HWMonitor e à aba Sensores.</summary>
    public float? CpuTemperature
    {
        get
        {
            if (_mainCpuTempRow?.RawValue is float t && t > 0) return t;

            SensorRow? best = null;
            int bestPrio = -1;
            foreach (var r in Rows)
            {
                if (r.Type == SensorType.Temperature && r.RawValue is > 0 and < 130)
                {
                    int p = GetCpuTempPriority(r.Name, r.HardwareType);
                    if (p > bestPrio)
                    {
                        bestPrio = p;
                        best = r;
                    }
                }
            }
            if (best != null)
            {
                _mainCpuTempRow = best;
                _mainCpuTempPriority = bestPrio;
                return best.RawValue;
            }
            return null;
        }
    }

    /// <summary>Temperatura principal da GPU (GPU Core da placa de vídeo principal), idêntica ao HWMonitor e à aba Sensores.</summary>
    public float? GpuTemperature
    {
        get
        {
            if (_mainGpuTempRow?.RawValue is float t && t > 0) return t;

            SensorRow? best = null;
            int bestPrio = -1;
            foreach (var r in Rows)
            {
                if (r.Type == SensorType.Temperature && r.RawValue is > 0 and < 130)
                {
                    int p = GetGpuTempPriority(r.Name, r.Hardware, r.HardwareType);
                    if (p > bestPrio)
                    {
                        bestPrio = p;
                        best = r;
                    }
                }
            }
            if (best != null)
            {
                _mainGpuTempRow = best;
                _mainGpuTempPriority = bestPrio;
                return best.RawValue;
            }
            return null;
        }
    }

    /// <summary>Recalcula o sensor de GPU principal quando a GPU selecionada muda.</summary>
    public void InvalidateGpuCache(string? primaryGpuName)
    {
        PrimaryGpuName = primaryGpuName;
        _mainGpuTempRow = null;
        _mainGpuTempPriority = -1;
    }

    /// <summary>Carga total da CPU em %.</summary>
    public float? CpuLoad => _mainCpuLoadRow?.RawValue ??
                             Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                                   && r.Type == SensorType.Load
                                                   && r.Name == "CPU Total")?.RawValue;

    /// <summary>Consumo do pacote da CPU em W.</summary>
    public float? CpuPower => _mainCpuPowerRow?.RawValue ??
                              Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                                    && r.Type == SensorType.Power
                                                    && r.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))?.RawValue;

    /// <summary>
    /// Driver PawnIO (necessário para temperatura/consumo da CPU via MSR/SMN).
    /// A biblioteca procura exatamente esta chave de desinstalação.
    /// </summary>
    public static bool IsPawnIOInstalled
    {
        get
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                     Microsoft.Win32.RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return key != null;
        }
    }

    public static bool IsElevated =>
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    public enum CpuSensorProblem { None, NotElevated, PawnIOMissing, ReadFailed }

    /// <summary>Por que a temperatura/consumo da CPU não aparecem (o motivo mais provável primeiro).</summary>
    public CpuSensorProblem DiagnoseCpuSensors()
    {
        if (CpuTemperature != null) return CpuSensorProblem.None;
        if (!IsElevated) return CpuSensorProblem.NotElevated;
        if (!IsPawnIOInstalled) return CpuSensorProblem.PawnIOMissing;
        return CpuSensorProblem.ReadFailed;
    }

    public static string Explain(CpuSensorProblem p) => p switch
    {
        CpuSensorProblem.NotElevated =>
            "O Lumini Tray não está rodando como administrador, e o Windows só libera os sensores internos da CPU " +
            "nesse modo. Feche e abra de novo aceitando o pedido de administrador.",
        CpuSensorProblem.PawnIOMissing =>
            "Falta o driver PawnIO, que lê a temperatura e o consumo da CPU. Ele é gratuito, assinado e usado também " +
            "pelo LibreHardwareMonitor e pelo FanControl. Depois de instalar, feche e abra o Lumini Tray.",
        CpuSensorProblem.ReadFailed =>
            "O PawnIO está instalado, mas a leitura falhou. Se acabou de instalar, reinicie o PC. Se continuar, " +
            "confira se o antivírus não bloqueou o driver ou se a CPU é muito antiga/recente para a biblioteca de sensores.",
        _ => ""
    };

    public void Dispose()
    {
        if (_open)
        {
            lock (_syncLock)
            {
                _computer.Close();
            }
        }
    }
}
