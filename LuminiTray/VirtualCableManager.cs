using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

/// <summary>
/// Gerencia a detecção, extração autônoma e instalação do driver de Microfone Virtual (VB-Audio Virtual Cable).
/// O pacote do driver vem 100% embutido no executável (Drivers/VBCable.zip) e é extraído automaticamente
/// para que a instalação funcione em qualquer máquina, mesmo em releases Single-File sem pastas externas.
/// </summary>
public static class VirtualCableManager
{
    private static readonly object _extractLock = new();

    /// <summary>
    /// Garante que o dispositivo CABLE Input esteja ativo no Windows via PolicyConfig,
    /// reativando-o automaticamente caso esteja desabilitado.
    /// </summary>
    public static bool EnsureCableInputEnabled()
    {
        try
        {
            var cable = GetCableInputDevice();
            if (cable != null)
            {
                if (cable.IsDisabled)
                {
                    AudioCore.SetDeviceVisibility(cable.Id, true);
                    cable.IsDisabled = false;
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsVirtualCableInstalled()
    {
        try
        {
            var render = AudioCore.GetDevices(AudioInterop.EDataFlow.Render);
            var capture = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture);
            return render.Any(d => IsCableDevice(d.Name)) || capture.Any(d => IsCableDevice(d.Name));
        }
        catch
        {
            return false;
        }
    }

    public static AudioDeviceInfo? GetCableInputDevice()
    {
        try
        {
            var render = AudioCore.GetDevices(AudioInterop.EDataFlow.Render);
            return render.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
                                              (d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return null;
        }
    }

    public static AudioDeviceInfo? GetCableOutputDevice()
    {
        try
        {
            var capture = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture);
            return capture.FirstOrDefault(d => d.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase) ||
                                               (d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return null;
        }
    }

    public static bool IsCableDevice(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Virtual Audio", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Localiza ou extrai automaticamente o executável VBCABLE_Setup_x64.exe para o disco.
    /// </summary>
    public static string GetInstallerPath()
    {
        lock (_extractLock)
        {
            string baseDir = AppContext.BaseDirectory;
            string[] candidates = new[]
            {
                Path.Combine(baseDir, "Assets", "Drivers", "VBCable", "VBCABLE_Setup_x64.exe"),
                Path.Combine(baseDir, "Drivers", "VBCable", "VBCABLE_Setup_x64.exe"),
                Path.Combine(baseDir, "..", "LuminiTray", "Assets", "Drivers", "VBCable", "VBCABLE_Setup_x64.exe"),
                Path.Combine(Path.GetTempPath(), "LuminiTray", "VBCable", "VBCABLE_Setup_x64.exe")
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            // Se não encontrado em disco, extrai do recurso embutido
            string extracted = EnsureExtractedFromResource();
            if (!string.IsNullOrEmpty(extracted) && File.Exists(extracted))
            {
                return extracted;
            }

            return candidates[0];
        }
    }

    /// <summary>
    /// Extrai o arquivo zip embutido (Drivers/VBCable.zip) para a pasta temporária do Lumini Tray.
    /// </summary>
    public static string EnsureExtractedFromResource()
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "LuminiTray", "VBCable");
            string targetExe = Path.Combine(tempDir, "VBCABLE_Setup_x64.exe");
            if (File.Exists(targetExe)) return targetExe;

            Directory.CreateDirectory(tempDir);

            var assembly = typeof(VirtualCableManager).Assembly;
            using (var stream = assembly.GetManifestResourceStream("Drivers/VBCable.zip")
                             ?? assembly.GetManifestResourceStream("LuminiTray.Drivers.VBCable.zip")
                             ?? assembly.GetManifestResourceStream("LuminiTray.Assets.Drivers.VBCable.zip"))
            {
                if (stream != null)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    archive.ExtractToDirectory(tempDir, overwriteFiles: true);
                    if (File.Exists(targetExe)) return targetExe;
                }
            }

            // Fallback de arquivos ZIP em disco
            string[] diskZipCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "Drivers", "VBCable.zip"),
                Path.Combine(AppContext.BaseDirectory, "..", "LuminiTray", "Assets", "Drivers", "VBCable.zip")
            };

            foreach (var z in diskZipCandidates)
            {
                if (File.Exists(z))
                {
                    ZipFile.ExtractToDirectory(z, tempDir, overwriteFiles: true);
                    if (File.Exists(targetExe)) return targetExe;
                }
            }

            return targetExe;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task<(bool success, string message)> InstallDriverAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                string installer = GetInstallerPath();
                if (!File.Exists(installer))
                {
                    installer = EnsureExtractedFromResource();
                }

                if (!File.Exists(installer))
                {
                    return (false, "Não foi possível extrair o pacote do driver. Verifique permissões do sistema.");
                }

                string workDir = Path.GetDirectoryName(installer) ?? AppContext.BaseDirectory;

                // 1. Tentar instalação silenciosa oficial do fabricante
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = installer,
                        Arguments = "-i -h",
                        WorkingDirectory = workDir,
                        UseShellExecute = true,
                        Verb = "runas"
                    };

                    using (var proc = Process.Start(psi))
                    {
                        proc?.WaitForExit(20000);
                    }
                }
                catch { }

                Thread.Sleep(2000);

                if (IsVirtualCableInstalled())
                {
                    return (true, "Microfone Virtual VB-CABLE instalado com sucesso! Selecione 'CABLE Output' nos seus aplicativos.");
                }

                // 2. Método nativo via pnputil direto do driver .inf
                string infPath = Path.Combine(workDir, "vbMmeCable64_win10.inf");
                if (File.Exists(infPath))
                {
                    try
                    {
                        var pnpPsi = new ProcessStartInfo
                        {
                            FileName = "pnputil.exe",
                            Arguments = $"/add-driver \"{infPath}\" /install",
                            WorkingDirectory = workDir,
                            UseShellExecute = true,
                            Verb = "runas",
                            CreateNoWindow = true
                        };
                        using var pnpProc = Process.Start(pnpPsi);
                        pnpProc?.WaitForExit(15000);
                        Thread.Sleep(2000);
                    }
                    catch { }
                }

                if (IsVirtualCableInstalled())
                {
                    return (true, "Microfone Virtual instalado com sucesso via pnputil!");
                }

                // 3. Se ainda não foi detectado automaticamente, abre a janela do instalador oficial
                // para o usuário simplesmente clicar no botão "Install Driver"
                try
                {
                    var guiPsi = new ProcessStartInfo
                    {
                        FileName = installer,
                        WorkingDirectory = workDir,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(guiPsi);

                    return (true, "Instalador do Microfone Virtual aberto! Basta clicar no botão 'Install Driver' na janela.");
                }
                catch (Exception guiEx)
                {
                    return (false, $"Erro ao abrir instalador: {guiEx.Message}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao instalar driver: {ex.Message}");
            }
        });
    }

    public static async Task<(bool success, string message)> UninstallDriverAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                string installer = GetInstallerPath();
                if (!File.Exists(installer))
                {
                    return (false, "Desinstalador não encontrado.");
                }

                string workDir = Path.GetDirectoryName(installer) ?? AppContext.BaseDirectory;

                var psi = new ProcessStartInfo
                {
                    FileName = installer,
                    Arguments = "-u -h",
                    WorkingDirectory = workDir,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using (var proc = Process.Start(psi))
                {
                    proc?.WaitForExit(25000);
                }

                Thread.Sleep(2000);
                return (true, "Driver desinstalado com sucesso.");
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao desinstalar: {ex.Message}");
            }
        });
    }
}
