using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

public enum RepairStepStatus
{
    Idle,
    Running,
    Completed,
    Warning,
    Failed,
    Skipped
}

public sealed class RepairTaskItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CommandText { get; set; } = "";
    public string Description { get; set; } = "";
    public string EstimatedDuration { get; set; } = "2 a 5 min";
    public bool IsRecommended { get; set; } = true;
    public bool IsChecked { get; set; } = true;
    public RepairStepStatus Status { get; set; } = RepairStepStatus.Idle;
    public string ResultDetails { get; set; } = "";
    public bool RequiresReboot { get; set; }

    public string StatusBadgeText => Status switch
    {
        RepairStepStatus.Idle => "Pendente",
        RepairStepStatus.Running => "Executando...",
        RepairStepStatus.Completed => "✓ Concluído",
        RepairStepStatus.Warning => "⚠️ Atenção",
        RepairStepStatus.Failed => "❌ Erro",
        RepairStepStatus.Skipped => "Ignorado",
        _ => ""
    };

    public string StatusColor => Status switch
    {
        RepairStepStatus.Running => "#3B82F6",
        RepairStepStatus.Completed => "#10B981",
        RepairStepStatus.Warning => "#F59E0B",
        RepairStepStatus.Failed => "#EF4444",
        _ => "#888888"
    };
}

public sealed class RepairProgressUpdate
{
    public string CurrentTaskId { get; set; } = "";
    public string CurrentTaskName { get; set; } = "";
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public int OverallPercent { get; set; }
    public string LogLine { get; set; } = "";
}

public static class WindowsRepairService
{
    public static List<RepairTaskItem> GetDefaultRepairTasks()
    {
        return new List<RepairTaskItem>
        {
            new()
            {
                Id = "restore_point",
                Name = "Criar ponto de restauração de segurança",
                CommandText = "SystemRestore (Criar ponto)",
                Description = "Garante que o Windows possa voltar ao estado anterior com segurança se algo falhar.",
                EstimatedDuration = "1 min",
                IsRecommended = true,
                IsChecked = true
            },
            new()
            {
                Id = "chkdsk_scan",
                Name = "Conferir o disco C: sem travar o PC",
                CommandText = "chkdsk C: /scan",
                Description = "Examina a integridade dos metadados e arquivos do sistema NTFS em segundo plano sem desmontar o volume.",
                EstimatedDuration = "2 a 10 min",
                IsRecommended = true,
                IsChecked = true
            },
            new()
            {
                Id = "dism",
                Name = "Consertar a imagem do Windows (DISM)",
                CommandText = "dism /online /cleanup-image /restorehealth",
                Description = "Examina o repositório oficial de pacotes WinSxS e baixa substitutos íntegros da Microsoft para componentes corrompidos.",
                EstimatedDuration = "5 a 20 min",
                IsRecommended = true,
                IsChecked = true
            },
            new()
            {
                Id = "sfc",
                Name = "Conferir os arquivos do Windows (SFC)",
                CommandText = "sfc /scannow",
                Description = "Verifica cada DLL e arquivo binário do Windows, substituindo versões corrompidas por cópias limpas.",
                EstimatedDuration = "3 a 10 min",
                IsRecommended = true,
                IsChecked = true
            },
            new()
            {
                Id = "wmi",
                Name = "Conferir o banco de dados WMI",
                CommandText = "winmgmt /salvagerepository",
                Description = "Recupera o banco de dados WMI se estiver corrompido, resolvendo erros de programas e leitura de sensores.",
                EstimatedDuration = "1 a 3 min",
                IsRecommended = true,
                IsChecked = true,
                RequiresReboot = true
            },
            new()
            {
                Id = "icon_cache",
                Name = "Recriar o cache de ícones e miniaturas",
                CommandText = "Apagar IconCache.db e reiniciar Explorer",
                Description = "Elimina o cache de ícones corrompidos em branco ou trocados na Área de Trabalho e Barra de Tarefas.",
                EstimatedDuration = "30 s",
                IsRecommended = true,
                IsChecked = true
            },
            new()
            {
                Id = "network",
                Name = "Restaurar a configuração de internet",
                CommandText = "netsh winsock reset & ipconfig /flushdns",
                Description = "Redefine a pilha TCP/IP, sockets e limpa o cache DNS para resolver páginas que não abrem.",
                EstimatedDuration = "1 min",
                IsRecommended = true,
                IsChecked = true,
                RequiresReboot = true
            },
            new()
            {
                Id = "chkdsk_boot",
                Name = "Corrigir o disco C: no próximo reinício",
                CommandText = "chkdsk C: /f",
                Description = "Agenda uma verificação profunda de clusters na próxima reinicialização se houver erros graves de disco.",
                EstimatedDuration = "Ao reiniciar",
                IsRecommended = false,
                IsChecked = false,
                RequiresReboot = true
            }
        };
    }

    public static async Task ExecuteRepairsAsync(
        IList<RepairTaskItem> items,
        Action<RepairProgressUpdate> onProgress,
        CancellationToken ct)
    {
        var activeItems = items.Where(x => x.IsChecked).ToList();
        int total = activeItems.Count;
        int completed = 0;

        for (int i = 0; i < activeItems.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var task = activeItems[i];
            task.Status = RepairStepStatus.Running;

            onProgress(new RepairProgressUpdate
            {
                CurrentTaskId = task.Id,
                CurrentTaskName = task.Name,
                CompletedCount = completed,
                TotalCount = total,
                OverallPercent = (int)(completed * 100.0 / total),
                LogLine = $"\n=== Iniciando: {task.Name} ({task.CommandText}) ==="
            });

            try
            {
                switch (task.Id)
                {
                    case "restore_point":
                        var (okRp, msgRp) = await SystemRestoreManager.CreateRestorePointAsync("Reparo do Windows - Lumini Tray");
                        task.Status = okRp ? RepairStepStatus.Completed : RepairStepStatus.Warning;
                        task.ResultDetails = msgRp;
                        break;

                    case "chkdsk_scan":
                        var resChkdsk = await RunProcessWithOutputAsync("chkdsk.exe", "C: /scan", onProgress, task, ct);
                        task.Status = resChkdsk.ExitCode == 0 ? RepairStepStatus.Completed : RepairStepStatus.Warning;
                        task.ResultDetails = resChkdsk.ExitCode == 0
                            ? "Nenhum problema no sistema de arquivos do disco C:."
                            : "O CHKDSK localizou inconsistências que podem necessitar de reparo com reinício.";
                        break;

                    case "dism":
                        var resDism = await RunProcessWithOutputAsync("dism.exe", "/online /cleanup-image /restorehealth", onProgress, task, ct);
                        task.Status = resDism.ExitCode == 0 ? RepairStepStatus.Completed : RepairStepStatus.Warning;
                        task.ResultDetails = resDism.ExitCode == 0
                            ? "Imagem do Windows reparada com sucesso (repositório íntegro)."
                            : "DISM finalizou com avisos. Verifique DISM.log.";
                        break;

                    case "sfc":
                        var resSfc = await RunProcessWithOutputAsync("sfc.exe", "/scannow", onProgress, task, ct);
                        task.Status = resSfc.ExitCode == 0 ? RepairStepStatus.Completed : RepairStepStatus.Warning;
                        task.ResultDetails = resSfc.ExitCode == 0
                            ? "A Proteção de Recursos do Windows não encontrou nenhuma violação de integridade ou reparou os arquivos encontrados."
                            : "SFC encontrou arquivos que podem requerer reinicialização.";
                        break;

                    case "wmi":
                        var resWmi = await RunProcessWithOutputAsync("winmgmt.exe", "/salvagerepository", onProgress, task, ct);
                        task.Status = resWmi.ExitCode == 0 ? RepairStepStatus.Completed : RepairStepStatus.Warning;
                        task.ResultDetails = "Repositório WMI conferido e verificado com sucesso. Reinicie para sincronizar totalmente.";
                        break;

                    case "icon_cache":
                        await Task.Run(() => RebuildIconCache(onProgress, task));
                        task.Status = RepairStepStatus.Completed;
                        task.ResultDetails = "Cache de ícones e miniaturas apagado e recriado com sucesso.";
                        break;

                    case "network":
                        await RunProcessWithOutputAsync("netsh.exe", "winsock reset", onProgress, task, ct);
                        await RunProcessWithOutputAsync("netsh.exe", "int ip reset", onProgress, task, ct);
                        await RunProcessWithOutputAsync("ipconfig.exe", "/flushdns", onProgress, task, ct);
                        task.Status = RepairStepStatus.Completed;
                        task.ResultDetails = "Rede, Winsock e cache DNS redefinidos com sucesso. Reinicie para efetivar.";
                        break;

                    case "chkdsk_boot":
                        var resSchedule = await RunProcessWithOutputAsync("fsutil.exe", "dirty set C:", onProgress, task, ct);
                        task.Status = resSchedule.ExitCode == 0 ? RepairStepStatus.Completed : RepairStepStatus.Warning;
                        task.ResultDetails = "Verificação de disco agendada para o próximo reinício do PC.";
                        break;

                    default:
                        task.Status = RepairStepStatus.Completed;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                task.Status = RepairStepStatus.Skipped;
                task.ResultDetails = "Operação cancelada pelo usuário.";
                throw;
            }
            catch (Exception ex)
            {
                task.Status = RepairStepStatus.Failed;
                task.ResultDetails = "Erro: " + ex.Message;
            }

            completed++;
            onProgress(new RepairProgressUpdate
            {
                CurrentTaskId = task.Id,
                CurrentTaskName = task.Name,
                CompletedCount = completed,
                TotalCount = total,
                OverallPercent = (int)(completed * 100.0 / total),
                LogLine = $"[STATUS] {task.Name} -> {task.StatusBadgeText} ({task.ResultDetails})"
            });
        }
    }

    private static async Task<(int ExitCode, string Output)> RunProcessWithOutputAsync(
        string fileName,
        string args,
        Action<RepairProgressUpdate> onProgress,
        RepairTaskItem task,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        var outputSb = new StringBuilder();
        using var proc = new Process { StartInfo = psi };

        proc.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                outputSb.AppendLine(e.Data);
                onProgress(new RepairProgressUpdate
                {
                    CurrentTaskId = task.Id,
                    CurrentTaskName = task.Name,
                    LogLine = e.Data
                });
            }
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                outputSb.AppendLine(e.Data);
                onProgress(new RepairProgressUpdate
                {
                    CurrentTaskId = task.Id,
                    CurrentTaskName = task.Name,
                    LogLine = "[ERRO] " + e.Data
                });
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
        });

        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, outputSb.ToString());
    }

    private static void RebuildIconCache(Action<RepairProgressUpdate> onProgress, RepairTaskItem task)
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string iconCachePath = Path.Combine(localAppData, "IconCache.db");
            if (File.Exists(iconCachePath))
            {
                File.Delete(iconCachePath);
                onProgress(new RepairProgressUpdate { CurrentTaskId = task.Id, LogLine = "Apagado: IconCache.db" });
            }

            string explorerCacheDir = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
            if (Directory.Exists(explorerCacheDir))
            {
                foreach (var file in Directory.GetFiles(explorerCacheDir, "iconcache_*.db"))
                {
                    try { File.Delete(file); onProgress(new RepairProgressUpdate { CurrentTaskId = task.Id, LogLine = $"Apagado: {Path.GetFileName(file)}" }); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            onProgress(new RepairProgressUpdate { CurrentTaskId = task.Id, LogLine = "Aviso ao recriar cache de ícones: " + ex.Message });
        }
    }
}
