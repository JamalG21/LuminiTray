using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace LuminiTray;

/// <summary>
/// Gerencia o ciclo de vida dos subprocessos dedicados do Lumini Tray.
/// Inicia e encerra subtarefas vinculadas ao Job Object e trata encerramentos
/// manuais realizados pelo usuário no Gerenciador de Tarefas do Windows.
/// </summary>
public static class SubtaskProcessManager
{
    private static Process? _audioSuppressorProcess;
    private static Process? _metricsMonitorProcess;
    private static Process? _fanControllerProcess;
    private static readonly object _lock = new();

    /// <summary>
    /// Retorna a soma do WorkingSet (memória física) de todos os subprocessos ativos gerenciados.
    /// </summary>
    public static long GetSubtasksWorkingSet()
    {
        long sum = 0;
        lock (_lock)
        {
            Process?[] procs = { _audioSuppressorProcess, _metricsMonitorProcess, _fanControllerProcess };
            foreach (var p in procs)
            {
                try
                {
                    if (p != null && !p.HasExited)
                    {
                        p.Refresh();
                        sum += p.WorkingSet64;
                    }
                }
                catch { }
            }
        }
        return sum;
    }

    public static void StartSubtask(string type, Action? onManualExit = null)
    {
        lock (_lock)
        {
            StopSubtask(type);

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exeName = type.ToLowerInvariant() switch
                {
                    "audio-suppressor" => "LuminiTray.Suppressor.exe",
                    "metrics-monitor" => "LuminiTray.Metrics.exe",
                    "fan-controller" => "LuminiTray.Fans.exe",
                    _ => "LuminiTray.exe"
                };

                string targetExe = Path.Combine(baseDir, exeName);
                if (!File.Exists(targetExe))
                {
                    targetExe = Path.Combine(baseDir, "Assets", "Subtasks", exeName);
                }
                if (!File.Exists(targetExe))
                {
                    targetExe = Environment.ProcessPath ?? Path.Combine(baseDir, "LuminiTray.exe");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    Arguments = $"--subtask={type} --parent-pid={Environment.ProcessId}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = baseDir
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    JobObjectManager.AssignProcess(proc);

                    proc.EnableRaisingEvents = true;
                    proc.Exited += (_, _) =>
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            onManualExit?.Invoke();
                        });
                    };

                    switch (type.ToLowerInvariant())
                    {
                        case "audio-suppressor":
                            _audioSuppressorProcess = proc;
                            break;
                        case "metrics-monitor":
                            _metricsMonitorProcess = proc;
                            break;
                        case "fan-controller":
                            _fanControllerProcess = proc;
                            break;
                    }

                    string friendlyTitle = type.ToLowerInvariant() switch
                    {
                        "audio-suppressor" => "Lumini Tray - Supressor de Ruído",
                        "metrics-monitor" => "Lumini Tray - Monitor de Hardware",
                        "fan-controller" => "Lumini Tray - Controlador de Fans",
                        _ => $"Lumini Tray - Subtarefa ({type})"
                    };
                    SubtaskManager.RegisterSubtask(type, friendlyTitle, () => StopSubtask(type));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SubtaskProcessManager] Falha ao iniciar {type}: {ex.Message}");
            }
        }
    }

    public static void StopSubtask(string type)
    {
        lock (_lock)
        {
            Process? p = type.ToLowerInvariant() switch
            {
                "audio-suppressor" => _audioSuppressorProcess,
                "metrics-monitor" => _metricsMonitorProcess,
                "fan-controller" => _fanControllerProcess,
                _ => null
            };

            if (p != null)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                    }
                }
                catch { }
                try { p.Dispose(); } catch { }
            }

            switch (type.ToLowerInvariant())
            {
                case "audio-suppressor":
                    _audioSuppressorProcess = null;
                    break;
                case "metrics-monitor":
                    _metricsMonitorProcess = null;
                    break;
                case "fan-controller":
                    _fanControllerProcess = null;
                    break;
            }

            SubtaskManager.UnregisterSubtask(type);
        }
    }

    public static void StopAll()
    {
        StopSubtask("audio-suppressor");
        StopSubtask("metrics-monitor");
        StopSubtask("fan-controller");
    }
}
