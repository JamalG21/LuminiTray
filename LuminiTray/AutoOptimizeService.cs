using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace LuminiTray;

/// <summary>
/// Resultado da execução de um ciclo de otimização automática.
/// </summary>
public sealed record AutoOptimizeResult(
    DateTime Timestamp,
    long RamFreedBytes,
    bool StandbyPurged,
    List<string> ClosedProcesses,
    long TempFreedBytes,
    string Summary,
    bool Success
);

/// <summary>
/// Serviço de gerenciamento da Otimização Automática Programada do Lumini Tray.
/// Executa em segundo plano com intervalo configurável na ThreadPool nativa,
/// funcionando continuamente mesmo com a janela minimizada na bandeja do sistema.
/// </summary>
public sealed class AutoOptimizeService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly object _timerLock = new();
    private System.Threading.Timer? _timer;
    private bool _isRunning;

    public event Action<TimeSpan>? TickCountdown;
    public event Action<AutoOptimizeResult>? OptimizationCompleted;

    public bool IsEnabled => _settings.AutoOptimizeEnabled;
    public int IntervalMinutes => Math.Max(1, _settings.AutoOptimizeIntervalMinutes);
    public DateTime? NextRunTime { get; private set; }
    public DateTime? LastRunTime => _settings.LastAutoOptimizeRun;
    public string? LastSummary => _settings.LastAutoOptimizeSummary;
    public int RunCount => _settings.AutoOptimizeRunCount;
    public bool IsRunning => _isRunning;

    public ObservableCollection<string> Exclusions { get; } = new();

    public AutoOptimizeService(AppSettings settings)
    {
        _settings = settings;

        // Inicializa a coleção de exclusões a partir das configurações salvas
        foreach (var item in _settings.AutoOptimizeExclusions)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                string norm = NormalizeProcessName(item);
                if (!Exclusions.Contains(norm, StringComparer.OrdinalIgnoreCase))
                    Exclusions.Add(norm);
            }
        }

        if (_settings.AutoOptimizeEnabled)
        {
            Start();
        }
    }

    public void SetEnabled(bool enabled)
    {
        _settings.AutoOptimizeEnabled = enabled;
        _settings.Save();

        if (enabled)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    public void SetInterval(int minutes)
    {
        int validMinutes = Math.Clamp(minutes, 1, 1440); // 1 minuto até 24 horas
        _settings.AutoOptimizeIntervalMinutes = validMinutes;
        _settings.Save();

        if (_settings.AutoOptimizeEnabled)
        {
            NextRunTime = DateTime.Now.AddMinutes(validMinutes);
        }
    }

    public void SetOptions(bool ram, bool processes, bool cleanTemp)
    {
        _settings.AutoOptimizeRam = ram;
        _settings.AutoOptimizeProcesses = processes;
        _settings.AutoOptimizeCleanTemp = cleanTemp;
        _settings.Save();
    }

    public bool AddExclusion(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        string norm = NormalizeProcessName(processName);

        if (Exclusions.Contains(norm, StringComparer.OrdinalIgnoreCase))
            return false;

        Exclusions.Add(norm);
        _settings.AutoOptimizeExclusions = Exclusions.ToList();
        _settings.Save();
        return true;
    }

    public bool RemoveExclusion(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        string norm = NormalizeProcessName(processName);

        bool removed = false;
        for (int i = Exclusions.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Exclusions[i], norm, StringComparison.OrdinalIgnoreCase))
            {
                Exclusions.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            _settings.AutoOptimizeExclusions = Exclusions.ToList();
            _settings.Save();
        }
        return removed;
    }

    public void Start()
    {
        lock (_timerLock)
        {
            NextRunTime = DateTime.Now.AddMinutes(IntervalMinutes);
            _timer?.Dispose();
            _timer = new System.Threading.Timer(Timer_Tick, null, 1000, 1000);
        }
    }

    public void Stop()
    {
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
            NextRunTime = null;
        }
        TickCountdown?.Invoke(TimeSpan.Zero);
    }

    public void Dispose()
    {
        Stop();
    }

    private async void Timer_Tick(object? state)
    {
        if (!_settings.AutoOptimizeEnabled || NextRunTime == null)
        {
            TickCountdown?.Invoke(TimeSpan.Zero);
            return;
        }

        var remaining = NextRunTime.Value - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            if (!_isRunning)
            {
                await RunAsync(isManual: false);
            }
        }
        else
        {
            TickCountdown?.Invoke(remaining);
        }
    }

    public async Task<AutoOptimizeResult> RunAsync(bool isManual = false)
    {
        if (_isRunning)
            return new AutoOptimizeResult(DateTime.Now, 0, false, new List<string>(), 0, "Otimização já em andamento...", false);

        _isRunning = true;
        try
        {
            long ramFreed = 0;
            bool standbyPurged = false;
            var closedProcs = new List<string>();
            long tempFreed = 0;
            var summaryParts = new List<string>();

            // 1. Encerramento de processos ociosos (respeitando exclusões)
            if (_settings.AutoOptimizeProcesses)
            {
                var closeRes = await Task.Run(() => Optimizer.CloseUselessProcesses(Exclusions));
                closedProcs = closeRes.Closed;
                if (closedProcs.Count > 0)
                {
                    summaryParts.Add($"{closedProcs.Count} processo(s) encerrado(s) ({string.Join(", ", closedProcs)})");
                }
            }

            // 2. Limpeza profunda de Memória RAM e Standby List
            if (_settings.AutoOptimizeRam)
            {
                var mem = await Task.Run(Optimizer.CleanMemory);
                ramFreed = mem.FreedBytes;
                standbyPurged = mem.StandbyPurged;
                summaryParts.Add($"RAM: {ramFreed / 1048576.0:N0} MB liberados");
                if (standbyPurged) summaryParts.Add("Standby limpo");
            }

            // 3. Limpeza de arquivos temporários do sistema
            if (_settings.AutoOptimizeCleanTemp)
            {
                var temp = await Task.Run(Optimizer.CleanTempFiles);
                tempFreed = temp.FreedBytes;
                if (temp.FilesDeleted > 0)
                {
                    summaryParts.Add($"Temp: {tempFreed / 1048576.0:N0} MB ({temp.FilesDeleted} arquivos)");
                }
            }

            string fullSummary = summaryParts.Count > 0
                ? string.Join(" · ", summaryParts) + "."
                : "Ciclo concluído sem processos ou memória pendente.";

            _settings.LastAutoOptimizeRun = DateTime.Now;
            _settings.LastAutoOptimizeSummary = fullSummary;
            _settings.AutoOptimizeRunCount++;
            _settings.Save();

            // Reagenda o próximo ciclo
            if (_settings.AutoOptimizeEnabled)
            {
                NextRunTime = DateTime.Now.AddMinutes(IntervalMinutes);
            }

            var result = new AutoOptimizeResult(
                Timestamp: DateTime.Now,
                RamFreedBytes: ramFreed,
                StandbyPurged: standbyPurged,
                ClosedProcesses: closedProcs,
                TempFreedBytes: tempFreed,
                Summary: fullSummary,
                Success: true
            );

            OptimizationCompleted?.Invoke(result);
            return result;
        }
        finally
        {
            _isRunning = false;
        }
    }

    public static string NormalizeProcessName(string input)
    {
        string trimmed = input.Trim().ToLowerInvariant();
        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed += ".exe";
        return trimmed;
    }
}
