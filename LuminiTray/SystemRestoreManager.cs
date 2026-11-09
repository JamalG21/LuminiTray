using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LuminiTray;

public sealed class RestorePointItem
{
    public uint SequenceNumber { get; init; }
    public string Description { get; init; } = "";
    public DateTime CreationTime { get; init; }
    public string DateText => CreationTime.ToString("dd/MM/yyyy 'às' HH:mm:ss");
    public string TimeAgoText => GetTimeAgo(CreationTime);
    public uint RestorePointType { get; init; }
    public string TypeText => FriendlyType(RestorePointType);
    public string ShadowId { get; set; } = "";
    public bool HasShadowId => !string.IsNullOrEmpty(ShadowId);

    private static string FriendlyType(uint type) => type switch
    {
        0 => "Instalação de Aplicativo",
        1 => "Desinstalação de Aplicativo",
        7 => "Verificação Agendada",
        10 => "Instalação de Driver",
        12 => "Alteração de Configurações",
        13 => "Operação Cancelada",
        _ => "Ponto do Sistema"
    };

    private static string GetTimeAgo(DateTime dt)
    {
        var span = DateTime.Now - dt;
        if (span.TotalDays >= 2) return $"{(int)span.TotalDays} dias atrás";
        if (span.TotalDays >= 1) return "Ontem";
        if (span.TotalHours >= 2) return $"{(int)span.TotalHours} horas atrás";
        if (span.TotalHours >= 1) return "1 hora atrás";
        if (span.TotalMinutes >= 2) return $"{(int)span.TotalMinutes} minutos atrás";
        return "Recentemente";
    }
}

public static class SystemRestoreManager
{
    /// <summary>
    /// Lista os pontos de restauração do sistema combinando WMI SystemRestore e Win32_ShadowCopy.
    /// </summary>
    public static List<RestorePointItem> GetRestorePoints()
    {
        var result = new List<RestorePointItem>();
        var shadows = new List<(string Id, DateTime InstallDate)>();

        // 1. Carrega as cópias de sombra (Shadow Copies) para obter os IDs deletáveis
        try
        {
            using var scSearcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT ID, InstallDate FROM Win32_ShadowCopy");
            foreach (ManagementObject s in scSearcher.Get())
            {
                string id = s["ID"]?.ToString() ?? "";
                string idate = s["InstallDate"]?.ToString() ?? "";
                if (TryParseCimDateTime(idate, out var dt))
                {
                    shadows.Add((id, dt));
                }
            }
        }
        catch { }

        // 2. Carrega os Pontos de Restauração em root\default:SystemRestore
        try
        {
            using var srSearcher = new ManagementObjectSearcher(@"root\default", "SELECT * FROM SystemRestore");
            foreach (ManagementObject r in srSearcher.Get())
            {
                uint seq = Convert.ToUInt32(r["SequenceNumber"] ?? 0);
                string desc = r["Description"]?.ToString() ?? "Ponto de Restauração";
                string ctime = r["CreationTime"]?.ToString() ?? "";
                uint rtype = Convert.ToUInt32(r["RestorePointType"] ?? 0);

                DateTime dt = DateTime.Now;
                if (!TryParseCimDateTime(ctime, out dt))
                {
                    dt = DateTime.Now;
                }

                var item = new RestorePointItem
                {
                    SequenceNumber = seq,
                    Description = desc,
                    CreationTime = dt,
                    RestorePointType = rtype
                };

                // Correlaciona com o shadow mais próximo (diferença menor que 5 minutos)
                var matched = shadows
                    .Select(s => new { s.Id, Diff = Math.Abs((s.InstallDate - dt).TotalSeconds) })
                    .Where(x => x.Diff < 300)
                    .OrderBy(x => x.Diff)
                    .FirstOrDefault();

                if (matched != null)
                {
                    item.ShadowId = matched.Id;
                }

                result.Add(item);
            }
        }
        catch { }

        // Ordena do mais recente para o mais antigo
        return result.OrderByDescending(x => x.CreationTime).ToList();
    }

    /// <summary>
    /// Converte timestamp CIM (ex.: "20261027003020.386102-000") para DateTime local.
    /// </summary>
    public static bool TryParseCimDateTime(string cim, out DateTime dt)
    {
        dt = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(cim) || cim.Length < 14) return false;
        try
        {
            int y = int.Parse(cim[..4]);
            int m = int.Parse(cim.Substring(4, 2));
            int d = int.Parse(cim.Substring(6, 2));
            int h = int.Parse(cim.Substring(8, 2));
            int min = int.Parse(cim.Substring(10, 2));
            int s = int.Parse(cim.Substring(12, 2));

            // Verifica se tem fuso horário no final (ex: -180 = UTC-3)
            if (cim.Contains("-") || cim.Contains("+"))
            {
                try
                {
                    dt = ManagementDateTimeConverter.ToDateTime(cim);
                    return true;
                }
                catch { }
            }

            dt = new DateTime(y, m, d, h, min, s, DateTimeKind.Local);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Obtém informações sobre o espaço ocupado pelas cópias de sombra / pontos de restauração.
    /// </summary>
    public static async Task<string> GetStorageUsageAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("vssadmin.exe", "list shadowstorage")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.Default
                };
                using var p = Process.Start(psi);
                if (p == null) return "Não foi possível obter o armazenamento.";
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                string used = "";
                string allocated = "";
                string max = "";

                foreach (var line in output.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Contains("usado:", StringComparison.OrdinalIgnoreCase))
                        used = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    else if (trimmed.Contains("alocado:", StringComparison.OrdinalIgnoreCase))
                        allocated = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    else if (trimmed.Contains("máximo:", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("mximo:", StringComparison.OrdinalIgnoreCase))
                        max = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                }

                if (!string.IsNullOrEmpty(used))
                {
                    string info = $"Espaço Usado: {used}";
                    if (!string.IsNullOrEmpty(allocated)) info += $" · Alocado: {allocated}";
                    if (!string.IsNullOrEmpty(max)) info += $" · Limite: {max}";
                    return info;
                }
            }
            catch { }
            return "Armazenamento VSS ativo.";
        });
    }

    /// <summary>
    /// Cria um novo ponto de restauração com descrição personalizada.
    /// </summary>
    public static async Task<(bool Success, string Message)> CreateRestorePointAsync(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            description = $"Lumini Tray - Backup {DateTime.Now:dd/MM/yyyy HH:mm}";

        description = description.Replace("'", "").Replace("\"", "").Trim();

        return await Task.Run(() =>
        {
            try
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", true);
                    key?.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                }
                catch { }

                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                if (p == null) return (false, "Falha ao iniciar PowerShell para criar o ponto.");
                p.WaitForExit();

                if (p.ExitCode == 0)
                {
                    return (true, "Ponto de restauração criado com sucesso!");
                }
                else
                {
                    string err = p.StandardError.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(err)) err = p.StandardOutput.ReadToEnd();
                    return (false, string.IsNullOrWhiteSpace(err) ? "O Windows não permitiu criar o ponto de restauração (verifique se a Proteção do Sistema está ativada no Disco C:)." : err.Trim());
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao criar ponto: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Exclui um ponto de restauração específico através do ShadowId ou ID correspondente.
    /// </summary>
    public static async Task<(bool Success, string Message)> DeleteRestorePointAsync(RestorePointItem item)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(item.ShadowId))
                {
                    // 1. Tenta via vssadmin delete shadows /shadow={id} /quiet
                    var psi = new ProcessStartInfo("vssadmin.exe", $"delete shadows /shadow={item.ShadowId} /quiet")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit();
                    if (p?.ExitCode == 0) return (true, "Ponto de restauração excluído com sucesso!");

                    // 2. Fallback via WMI Win32_ShadowCopy
                    using var scSearcher = new ManagementObjectSearcher(@"root\cimv2", $"SELECT * FROM Win32_ShadowCopy WHERE ID = '{item.ShadowId}'");
                    bool deleted = false;
                    foreach (ManagementObject s in scSearcher.Get())
                    {
                        s.Delete();
                        deleted = true;
                    }
                    if (deleted) return (true, "Ponto de restauração excluído via WMI com sucesso!");
                }

                return (false, "Não foi possível localizar o identificador VSS deste ponto para exclusão individual.");
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao excluir ponto: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Exclui os pontos mais antigos mantendo apenas o mais recente (libera espaço no disco).
    /// </summary>
    public static async Task<(bool Success, string Message)> DeleteOldestPointsAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("vssadmin.exe", "delete shadows /for=C: /oldest /quiet")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                return p?.ExitCode == 0
                    ? (true, "Pontos antigos excluídos com sucesso! Espaço em disco liberado.")
                    : (false, "O Windows não pôde excluir cópias antigas ou não havia cópias qualificadas.");
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao excluir pontos antigos: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Executa a restauração do sistema para o ponto informado.
    /// </summary>
    public static async Task<(bool Success, string Message)> ExecuteRestoreAsync(uint sequenceNumber)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var srClass = new ManagementClass(@"root\default", "SystemRestore", null);
                var inParams = srClass.GetMethodParameters("Restore");
                inParams["SequenceNumber"] = sequenceNumber;
                var outParams = srClass.InvokeMethod("Restore", inParams, null);
                uint ret = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1);

                if (ret == 0)
                {
                    return (true, "Restauração agendada com sucesso! O computador será reiniciado.");
                }
                else
                {
                    return (false, $"Código de retorno da API do Windows: {ret}.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Não foi possível iniciar restauração automática: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Abre o assistente oficial de Restauração do Sistema do Windows (rstrui.exe).
    /// </summary>
    public static void OpenWindowsRestoreWizard()
    {
        try
        {
            Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Não foi possível abrir o rstrui.exe: {ex.Message}", "Lumini Tray",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }
}
