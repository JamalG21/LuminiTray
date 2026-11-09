using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Status de drivers e dispositivos detectados para limpeza profunda pelo DDU.
/// </summary>
public sealed class DduDetectedHardware
{
    public bool HasNvidia { get; set; }
    public bool HasAmd { get; set; }
    public bool HasIntel { get; set; }
    public bool HasRealtekAudio { get; set; }
    public string GpuSummary { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public bool IsInSafeMode { get; set; }
}

/// <summary>
/// Display Driver Uninstaller (DDU): Remove completamente drivers de vídeo (NVIDIA, AMD, Intel) e de áudio,
/// excluindo pacotes do repositório de drivers do Windows (DriverStore via pnputil), pastas residuais,
/// serviços, processos e chaves do Registro.
/// </summary>
public static class DisplayDriverUninstaller
{
    /// <summary>
    /// Detecta o hardware de vídeo e áudio presente na máquina.
    /// </summary>
    public static DduDetectedHardware DetectHardware()
    {
        var info = new DduDetectedHardware();
        info.IsInSafeMode = IsSafeMode();

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterCompatibility FROM Win32_VideoController");
            var gpus = new List<string>();

            foreach (var mo in searcher.Get())
            {
                string name = mo["Name"]?.ToString() ?? "";
                string ver = mo["DriverVersion"]?.ToString() ?? "";
                string compat = mo["AdapterCompatibility"]?.ToString() ?? "";

                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || compat.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    info.HasNvidia = true;
                    if (string.IsNullOrEmpty(info.DriverVersion)) info.DriverVersion = ver;
                }
                if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || compat.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase))
                {
                    info.HasAmd = true;
                    if (string.IsNullOrEmpty(info.DriverVersion)) info.DriverVersion = ver;
                }
                if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || compat.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    info.HasIntel = true;
                    if (string.IsNullOrEmpty(info.DriverVersion)) info.DriverVersion = ver;
                }

                if (!string.IsNullOrWhiteSpace(name)) gpus.Add(name);
            }

            info.GpuSummary = gpus.Count > 0 ? string.Join(" | ", gpus) : "Placa de vídeo não identificada";
        }
        catch
        {
            info.GpuSummary = "Detecção padrão";
        }

        return info;
    }

    /// <summary>
    /// Executa a rotina completa de desinstalação profunda do driver selecionado (NVIDIA, AMD ou Intel).
    /// </summary>
    public static async Task<(bool Success, string Log)> CleanGpuDriverAsync(
        string vendor,
        Action<string, double>? onProgress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var log = new System.Text.StringBuilder();
            void LogMsg(string msg, double progress)
            {
                log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
                onProgress?.Invoke(msg, progress);
            }

            try
            {
                LogMsg($"Iniciando DDU para fabricante: {vendor.ToUpperInvariant()}...", 0.05);

                // 1. Encerra processos associados
                LogMsg("Finalizando processos em segundo plano do driver...", 0.15);
                KillVendorProcesses(vendor, log);

                // 2. Para serviços do driver
                LogMsg("Parando serviços do driver no Windows...", 0.25);
                StopVendorServices(vendor, log);

                // 3. Remove pacotes do DriverStore com pnputil
                LogMsg("Desinstalando e expurgando pacotes do DriverStore (PnPUtil)...", 0.45);
                UninstallPnpDrivers(vendor, log);

                // 4. Exclui diretórios e pastas residuais
                LogMsg("Excluindo diretórios, instaladores descompactados e caches de shaders...", 0.70);
                DeleteVendorDirectories(vendor, log);

                // 5. Limpa entradas de registro do fabricante
                LogMsg("Limpando chaves residuais de registro...", 0.88);
                CleanVendorRegistry(vendor, log);

                LogMsg($"Limpeza completa de driver {vendor.ToUpperInvariant()} finalizada com sucesso!", 1.0);
                return (true, log.ToString());
            }
            catch (Exception ex)
            {
                LogMsg($"Erro durante a limpeza: {ex.Message}", 1.0);
                return (false, log.ToString());
            }
        }, ct);
    }

    private static void KillVendorProcesses(string vendor, System.Text.StringBuilder log)
    {
        string[] procs = vendor.ToUpperInvariant() switch
        {
            "NVIDIA" => new[] { "nvcontainer", "NVIDIA Share", "nvsphelper64", "NvDisplay.Container", "GeForceNOW", "nvtelemetrycontainer" },
            "AMD" => new[] { "RadeonSoftware", "AMDRSServ", "amdow", "cplspcon", "cncmd", "atieclxx", "atiesrxx" },
            "INTEL" => new[] { "igfxEM", "igfxHK", "igfxTray", "ArcControl", "igfxCUIService" },
            _ => Array.Empty<string>()
        };

        foreach (var pName in procs)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(pName))
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(1000);
                        log.AppendLine($"  Processo finalizado: {pName}.exe (PID: {p.Id})");
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void StopVendorServices(string vendor, System.Text.StringBuilder log)
    {
        string[] services = vendor.ToUpperInvariant() switch
        {
            "NVIDIA" => new[] { "NvContainerLocalSystem", "NVDisplay.ContainerLocalSystem", "NvTelemetryContainer" },
            "AMD" => new[] { "AMD Crash Defender Service", "AMD External Events Utility", "AMDRyzenMasterDriver" },
            "INTEL" => new[] { "igfxCUIService1.0.0.0", "igfxCUIService2.0.0.0" },
            _ => Array.Empty<string>()
        };

        foreach (var s in services)
        {
            RunCmd("net", $"stop \"{s}\" /y", log);
            RunCmd("sc", $"config \"{s}\" start= disabled", log);
        }
    }

    private static void UninstallPnpDrivers(string vendor, System.Text.StringBuilder log)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = "/enum-drivers",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);

            // Procura blocos com o nome do arquivo publicado (ex: oem12.inf) e nome do provedor
            var lines = output.Split('\n');
            string currentOem = "";
            string currentClass = "";

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("Nome publicado:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Published Name:", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0) currentOem = line.Substring(colon + 1).Trim();
                }
                else if (line.StartsWith("Nome da classe:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Class Name:", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0) currentClass = line.Substring(colon + 1).Trim();
                }
                else if (line.StartsWith("Nome do provedor:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Provider Name:", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = line.IndexOf(':');
                    string provider = colon > 0 ? line.Substring(colon + 1).Trim() : "";

                    bool match = false;
                    if (vendor.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase) && provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        match = true;
                    else if (vendor.Equals("AMD", StringComparison.OrdinalIgnoreCase) && (provider.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase) || provider.Contains("AMD", StringComparison.OrdinalIgnoreCase)))
                        match = true;
                    else if (vendor.Equals("INTEL", StringComparison.OrdinalIgnoreCase) && provider.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                        match = true;

                    if (match && !string.IsNullOrWhiteSpace(currentOem))
                    {
                        log.AppendLine($"  Removendo pacote do DriverStore: {currentOem} ({provider} - {currentClass})");
                        RunCmd("pnputil.exe", $"/delete-driver {currentOem} /uninstall /force", log);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Aviso ao consultar PnPUtil: {ex.Message}");
        }
    }

    private static void DeleteVendorDirectories(string vendor, System.Text.StringBuilder log)
    {
        var dirs = new List<string>();

        if (vendor.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            dirs.Add(@"C:\NVIDIA");
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA Corporation"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA Corporation"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NVIDIA"));
        }
        else if (vendor.Equals("AMD", StringComparison.OrdinalIgnoreCase))
        {
            dirs.Add(@"C:\AMD");
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ATI"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "AMD"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AMD"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AMD"));
        }
        else if (vendor.Equals("INTEL", StringComparison.OrdinalIgnoreCase))
        {
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Intel", "GFX"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Intel", "ArcControl"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Intel", "GFX"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Intel", "ArcControl"));
        }

        foreach (var d in dirs)
        {
            if (Directory.Exists(d))
            {
                try
                {
                    Directory.Delete(d, true);
                    log.AppendLine($"  Diretório removido: {d}");
                }
                catch (Exception ex)
                {
                    log.AppendLine($"  Não foi possível remover totalmente {d}: {ex.Message}");
                }
            }
        }
    }

    private static void CleanVendorRegistry(string vendor, System.Text.StringBuilder log)
    {
        string[] regKeys = vendor.ToUpperInvariant() switch
        {
            "NVIDIA" => new[] { @"SOFTWARE\NVIDIA Corporation", @"SOFTWARE\WOW6432Node\NVIDIA Corporation" },
            "AMD" => new[] { @"SOFTWARE\AMD", @"SOFTWARE\ATI", @"SOFTWARE\WOW6432Node\AMD" },
            "INTEL" => new[] { @"SOFTWARE\Intel\GFX", @"SOFTWARE\Intel\ArcControl" },
            _ => Array.Empty<string>()
        };

        foreach (var sub in regKeys)
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
                hklm.DeleteSubKeyTree(sub, false);
                log.AppendLine($"  Chave HKLM removida: {sub}");
            }
            catch { }

            try
            {
                using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                hkcu.DeleteSubKeyTree(sub, false);
                log.AppendLine($"  Chave HKCU removida: {sub}");
            }
            catch { }
        }
    }

    private static void RunCmd(string file, string args, System.Text.StringBuilder log)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Comando '{file} {args}' gerou aviso: {ex.Message}");
        }
    }

    /// <summary>
    /// Configura o Windows para reiniciar no Modo de Segurança mínimo.
    /// </summary>
    public static bool EnableSafeBootMinimal()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("bcdedit.exe", "/set {current} safeboot minimal")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Restaura a inicialização normal do Windows (desativa o Modo de Segurança).
    /// </summary>
    public static bool RestoreNormalBoot()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("bcdedit.exe", "/deletevalue {current} safeboot")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Reinicia o computador imediatamente.
    /// </summary>
    public static void RebootComputer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 3 /c \"Lumini Tray: Reiniciando para finalizar desinstalacao limpa de driver\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    /// <summary>
    /// Verifica se o sistema operacional está rodando atualmente em Modo de Segurança.
    /// </summary>
    public static bool IsSafeMode()
    {
        const int SM_CLEANBOOT = 67;
        return GetSystemMetrics(SM_CLEANBOOT) != 0;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
