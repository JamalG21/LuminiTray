using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

/// <summary>
/// Representa uma pasta pessoal do Windows para inclusão no backup.
/// </summary>
public sealed class BackupFolderItem
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long TotalSizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsSelected { get; set; } = true;
    public string FormattedSize => DiskSpaceAnalyzer.FormatSize(TotalSizeBytes);
}

/// <summary>
/// Status de progresso durante o backup de arquivos.
/// </summary>
public sealed record BackupProgress(
    string CurrentFile,
    double Percentage,
    long CopiedBytes,
    long TotalBytes,
    int CopiedFiles,
    int TotalFiles);

/// <summary>
/// Serviço central de Backup do Lumini Tray:
/// 1. Salvador de Drivers (Exportação via pnputil e geração de instalador autônomo .bat).
/// 2. Salvador de Programas (Geração de instalador autônomo via winget).
/// 3. Backup de Pastas Pessoais (Área de Trabalho, Downloads, Imagens, Vídeos, Documentos em .ZIP ou cópia direta).
/// </summary>
public static class BackupService
{
    // =========================================================================
    // 1. PASTAS PESSOAIS DO USUÁRIO
    // =========================================================================

    public static List<BackupFolderItem> GetDefaultUserFolders()
    {
        var list = new List<BackupFolderItem>();

        void AddIfValid(string id, string name, string path, bool defaultSelected = true)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                list.Add(new BackupFolderItem
                {
                    Id = id,
                    DisplayName = name,
                    FullPath = path,
                    IsSelected = defaultSelected
                });
            }
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AddIfValid("Desktop", "Área de Trabalho (Desktop)", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        AddIfValid("Downloads", "Downloads", Path.Combine(userProfile, "Downloads"));
        AddIfValid("Pictures", "Imagens (Pictures)", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        AddIfValid("Videos", "Vídeos (Videos)", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        AddIfValid("Documents", "Documentos (Documents)", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), defaultSelected: false);

        return list;
    }

    /// <summary>
    /// Calcula de forma rápida e assíncrona o tamanho e contagem de arquivos das pastas selecionadas.
    /// </summary>
    public static async Task CalculateFolderSizesAsync(IEnumerable<BackupFolderItem> folders, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            foreach (var f in folders)
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(f.FullPath)) continue;

                long size = 0;
                int count = 0;
                try
                {
                    var opt = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.ReparsePoint // Evita loops em junctions/symlinks
                    };

                    foreach (var fi in new DirectoryInfo(f.FullPath).EnumerateFiles("*", opt))
                    {
                        if (ct.IsCancellationRequested) break;
                        size += fi.Length;
                        count++;
                    }
                }
                catch { }

                f.TotalSizeBytes = size;
                f.FileCount = count;
            }
        }, ct);
    }

    /// <summary>
    /// Executa o backup das pastas selecionadas para o destino (em formato ZIP ou cópia direta de pastas).
    /// </summary>
    public static async Task<(bool success, string message)> ExecuteFoldersBackupAsync(
        List<BackupFolderItem> selectedFolders,
        string destinationDir,
        bool compressZip,
        IProgress<BackupProgress> progress,
        CancellationToken ct)
    {
        if (selectedFolders == null || selectedFolders.Count == 0)
            return (false, "Nenhuma pasta foi selecionada para o backup.");

        if (string.IsNullOrWhiteSpace(destinationDir) || !Directory.Exists(destinationDir))
            return (false, "O diretório de destino selecionado não existe ou é inválido.");

        return await Task.Run<(bool, string)>(() =>
        {
            try
            {
                // 1. Coleta lista completa de arquivos de todas as pastas selecionadas
                long totalBytes = 0;
                int totalFiles = 0;
                var allFiles = new List<(string sourcePath, string relativePath)>();

                var enumOpt = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                foreach (var folder in selectedFolders)
                {
                    if (ct.IsCancellationRequested) return (false, "Backup cancelado pelo usuário.");
                    if (!Directory.Exists(folder.FullPath)) continue;

                    string folderName = folder.Id;
                    var dirInfo = new DirectoryInfo(folder.FullPath);
                    int rootLength = dirInfo.FullName.TrimEnd('\\').Length;

                    foreach (var file in dirInfo.EnumerateFiles("*", enumOpt))
                    {
                        if (ct.IsCancellationRequested) return (false, "Backup cancelado pelo usuário.");
                        string rel = folderName + file.FullName.Substring(rootLength);
                        allFiles.Add((file.FullName, rel));
                        totalBytes += file.Length;
                        totalFiles++;
                    }
                }

                if (allFiles.Count == 0)
                    return (false, "Nenhum arquivo encontrado nas pastas selecionadas.");

                long copiedBytes = 0;
                int copiedCount = 0;
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");

                if (compressZip)
                {
                    // 2A. Compactação em arquivo único .ZIP
                    string zipPath = Path.Combine(destinationDir, $"LuminiTray_Backup_{timestamp}.zip");
                    if (File.Exists(zipPath)) File.Delete(zipPath);

                    using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                    {
                        byte[] buffer = new byte[64 * 1024];

                        foreach (var (src, rel) in allFiles)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                zipStream.Dispose();
                                try { File.Delete(zipPath); } catch { }
                                return (false, "Backup em arquivo ZIP cancelado pelo usuário.");
                            }

                            try
                            {
                                var entry = archive.CreateEntry(rel, CompressionLevel.Optimal);
                                using var entryStream = entry.Open();
                                using var fileStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                                int bytesRead;
                                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    if (ct.IsCancellationRequested) break;
                                    entryStream.Write(buffer, 0, bytesRead);
                                    copiedBytes += bytesRead;

                                    double pct = totalBytes > 0 ? (double)copiedBytes / totalBytes * 100.0 : 0;
                                    progress.Report(new BackupProgress(Path.GetFileName(src), pct, copiedBytes, totalBytes, copiedCount, totalFiles));
                                }
                                copiedCount++;
                            }
                            catch { }
                        }
                    }

                    return (true, $"Backup concluído com sucesso em: {zipPath} ({DiskSpaceAnalyzer.FormatSize(copiedBytes)})");
                }
                else
                {
                    // 2B. Cópia direta de pastas
                    string targetRoot = Path.Combine(destinationDir, $"LuminiTray_Backup_{timestamp}");
                    Directory.CreateDirectory(targetRoot);

                    byte[] buffer = new byte[64 * 1024];

                    foreach (var (src, rel) in allFiles)
                    {
                        if (ct.IsCancellationRequested) return (false, "Backup cancelado pelo usuário.");

                        try
                        {
                            string destFilePath = Path.Combine(targetRoot, rel);
                            string destFileDir = Path.GetDirectoryName(destFilePath)!;
                            if (!Directory.Exists(destFileDir)) Directory.CreateDirectory(destFileDir);

                            using (var sourceStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var targetStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                int bytesRead;
                                while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    if (ct.IsCancellationRequested) break;
                                    targetStream.Write(buffer, 0, bytesRead);
                                    copiedBytes += bytesRead;

                                    double pct = totalBytes > 0 ? (double)copiedBytes / totalBytes * 100.0 : 0;
                                    progress.Report(new BackupProgress(Path.GetFileName(src), pct, copiedBytes, totalBytes, copiedCount, totalFiles));
                                }
                            }
                            copiedCount++;
                        }
                        catch { }
                    }

                    return (true, $"Backup de {copiedCount} arquivos concluído em: {targetRoot}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erro durante o backup: {ex.Message}");
            }
        }, ct);
    }

    // =========================================================================
    // 2. SALVADOR DE DRIVERS (EXPORTAÇÃO + INSTALADOR AUTÔNOMO .BAT)
    // =========================================================================

    /// <summary>
    /// Exporta os drivers de terceiros instalados na máquina via pnputil e gera o arquivo executável Instalar_Drivers.bat.
    /// </summary>
    public static async Task<(bool success, string message, string installerPath)> ExportDriversAndGenerateInstallerAsync(
        string targetDirectory,
        Action<string> progressCallback,
        CancellationToken ct)
    {
        return await Task.Run<(bool, string, string)>(async () =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetDirectory))
                    return (false, "Diretório de destino não selecionado.", "");

                string backupDir = Path.Combine(targetDirectory, $"Backup_Drivers_{DateTime.Now:yyyyMMdd-HHmm}");
                string driversFolder = Path.Combine(backupDir, "Drivers");
                Directory.CreateDirectory(driversFolder);

                progressCallback("Iniciando exportação de drivers via pnputil...");

                var psi = new ProcessStartInfo("pnputil.exe", $"/export-driver * \"{driversFolder}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "Não foi possível iniciar o utilitário pnputil do Windows.", "");

                int exportedCount = 0;
                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
                {
                    if (ct.IsCancellationRequested)
                    {
                        proc.Kill();
                        return (false, "Exportação cancelada pelo usuário.", "");
                    }
                    if (line.Contains(".inf", StringComparison.OrdinalIgnoreCase))
                    {
                        exportedCount++;
                        progressCallback($"Exportando driver #{exportedCount}...");
                    }
                }
                await proc.WaitForExitAsync(ct);

                // Gera o script instalador executável Instalar_Drivers.bat na raiz da pasta
                string installerScriptPath = Path.Combine(backupDir, "Instalar_Drivers.bat");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine(":: ========================================================================");
                sb.AppendLine(":: LUMINI TRAY - RESTAURADOR AUTOMATICO DE DRIVERS DO WINDOWS");
                sb.AppendLine(":: Gerado em: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                sb.AppendLine(":: ========================================================================");
                sb.AppendLine("title Restaurador de Drivers - LUMINI TRAY");
                sb.AppendLine("cd /d \"%~dp0\"");
                sb.AppendLine();
                sb.AppendLine(":: 1. Verificacao e elevacao automatica para Administrador");
                sb.AppendLine("net session >nul 2>&1");
                sb.AppendLine("if %errorlevel% neq 0 (");
                sb.AppendLine("    echo.");
                sb.AppendLine("    echo Solicitando privilegios de Administrador para instalar drivers...");
                sb.AppendLine("    powershell -NoProfile -Command \"Start-Process '%~f0' -Verb RunAs\"");
                sb.AppendLine("    exit /b");
                sb.AppendLine(")");
                sb.AppendLine();
                sb.AppendLine("echo ========================================================================");
                sb.AppendLine("echo              RESTAURANDO DRIVERS DE DISPOSITIVOS");
                sb.AppendLine("echo ========================================================================");
                sb.AppendLine("echo.");
                sb.AppendLine("echo Instalando todos os drivers exportados da pasta .\\Drivers ...");
                sb.AppendLine("echo Aguarde, isso pode levar alguns instantes...");
                sb.AppendLine("echo.");
                sb.AppendLine();
                sb.AppendLine("if exist \"Drivers\" (");
                sb.AppendLine("    pnputil.exe /add-driver \"Drivers\\*.inf\" /subdirs /install");
                sb.AppendLine(") else if exist \"*.inf\" (");
                sb.AppendLine("    pnputil.exe /add-driver \"*.inf\" /subdirs /install");
                sb.AppendLine(") else (");
                sb.AppendLine("    echo [ERRO] A pasta de drivers nao foi encontrada!");
                sb.AppendLine(")");
                sb.AppendLine();
                sb.AppendLine("echo.");
                sb.AppendLine("echo ========================================================================");
                sb.AppendLine("echo [CONCLUIDO] A instalacao de drivers foi finalizada com sucesso!");
                sb.AppendLine("echo Reinicie o computador caso algum dispositivo solicite.");
                sb.AppendLine("echo ========================================================================");
                sb.AppendLine("echo.");
                sb.AppendLine("pause");

                await File.WriteAllTextAsync(installerScriptPath, sb.ToString(), Encoding.Latin1, ct);

                return (true, $"{exportedCount} drivers exportados com sucesso!", installerScriptPath);
            }
            catch (Exception ex)
            {
                return (false, $"Falha ao exportar drivers: {ex.Message}", "");
            }
        });
    }

    // =========================================================================
    // 3. SALVADOR DE PROGRAMAS (GERADOR DE SCRIPT WINGET COM SELEÇÃO)
    // =========================================================================

    /// <summary>
    /// Gera um script executável (.bat) que instala via winget todos os softwares selecionados pelo usuário.
    /// </summary>
    public static async Task<(bool success, string message)> GenerateWingetInstallerScriptAsync(
        IEnumerable<InstalledSoftwareItem> selectedApps,
        string targetFilePath)
    {
        try
        {
            var appsList = selectedApps.Where(a => !string.IsNullOrWhiteSpace(a.DisplayName)).ToList();
            if (appsList.Count == 0)
                return (false, "Nenhum programa foi selecionado para o script de instalação.");

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine(":: ========================================================================");
            sb.AppendLine(":: LUMINI TRAY - INSTALADOR AUTOMATICO DE PROGRAMAS (WINGET)");
            sb.AppendLine(":: Gerado em: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            sb.AppendLine(":: ========================================================================");
            sb.AppendLine("title Instalador de Programas - LUMINI TRAY");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine();
            sb.AppendLine(":: 1. Verificacao de permissao de Administrador");
            sb.AppendLine("net session >nul 2>&1");
            sb.AppendLine("if %errorlevel% neq 0 (");
            sb.AppendLine("    echo Solicitando privilegios de Administrador...");
            sb.AppendLine("    powershell -NoProfile -Command \"Start-Process '%~f0' -Verb RunAs\"");
            sb.AppendLine("    exit /b");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine(":: 2. Verificacao do Gerenciador de Pacotes do Windows (winget)");
            sb.AppendLine("where winget >nul 2>&1");
            sb.AppendLine("if %errorlevel% neq 0 (");
            sb.AppendLine("    echo ========================================================================");
            sb.AppendLine("    echo [ERRO] O comando winget nao foi encontrado neste computador!");
            sb.AppendLine("    echo Instale o 'Instalador de Aplicativo' da Microsoft Store ou acesse:");
            sb.AppendLine("    echo https://github.com/microsoft/winget-cli/releases");
            sb.AppendLine("    echo ========================================================================");
            sb.AppendLine("    pause");
            sb.AppendLine("    exit /b 1");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("echo ========================================================================");
            sb.AppendLine($"echo      INICIANDO INSTALACAO AUTOMATIZADA DE {appsList.Count} PROGRAMAS");
            sb.AppendLine("echo ========================================================================");
            sb.AppendLine("echo.");

            int idx = 1;
            foreach (var app in appsList)
            {
                // Limpa o nome do app para busca precisa no winget
                string cleanName = app.DisplayName.Trim();
                // Remove caracteres problemáticos para comando batch
                cleanName = cleanName.Replace("\"", "").Replace("&", "^&").Replace("%", "%%");

                sb.AppendLine($"echo [{idx}/{appsList.Count}] Instalando: {cleanName} ...");
                sb.AppendLine($"winget install --name \"{cleanName}\" --accept-source-agreements --accept-package-agreements");
                sb.AppendLine("echo ------------------------------------------------------------------------");
                sb.AppendLine();
                idx++;
            }

            sb.AppendLine("echo.");
            sb.AppendLine("echo ========================================================================");
            sb.AppendLine("echo [SUCESSO] Todos os programas selecionados foram processados pelo winget!");
            sb.AppendLine("echo ========================================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("pause");

            string dir = Path.GetDirectoryName(targetFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(targetFilePath, sb.ToString(), Encoding.Latin1);
            return (true, $"Instalador de programas gerado com sucesso em: {targetFilePath}");
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao gerar instalador de programas: {ex.Message}");
        }
    }

    // =========================================================================
    // 4. BACKUP E RESTAURAÇÃO DE REDES WI-FI E SENHAS
    // =========================================================================

    public static async Task<List<WiFiNetworkProfile>> ListSavedWiFiProfilesAsync() =>
        await Task.Run(() =>
        {
            var list = new List<WiFiNetworkProfile>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "wlan show profiles",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var p = Process.Start(psi);
                if (p == null) return list;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    int colonIdx = line.IndexOf(':');
                    if (colonIdx != -1 && (line.Contains("Todos os Usu") || line.Contains("All User") || line.Contains("Perfil") || line.Contains("Profile")))
                    {
                        string ssid = line[(colonIdx + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(ssid))
                        {
                            list.Add(new WiFiNetworkProfile
                            {
                                Ssid = ssid,
                                Authentication = "WPA2/WPA3",
                                Password = "••• (será exportada)"
                            });
                        }
                    }
                }
            }
            catch { }
            return list;
        });

    public static async Task<(bool Success, string Message, List<WiFiNetworkProfile> ExportedProfiles)> ExportWiFiProfilesAsync(string targetDir) =>
        await Task.Run(() =>
        {
            var exported = new List<WiFiNetworkProfile>();
            try
            {
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                // Exporta perfis de rede com chave em texto legível
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"wlan export profile folder=\"{targetDir}\" key=clear",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);

                // Procura arquivos XML gerados
                var xmlFiles = Directory.GetFiles(targetDir, "Wi-Fi-*.xml");
                if (xmlFiles.Length == 0)
                {
                    // Tenta padrão sem prefixo Wi-Fi
                    xmlFiles = Directory.GetFiles(targetDir, "*.xml");
                }

                var textSummary = new StringBuilder();
                textSummary.AppendLine("========================================================================");
                textSummary.AppendLine("       LUMINI TRAY - BACKUP DE REDES WI-FI E SENHAS DO WINDOWS");
                textSummary.AppendLine($"       Data do Backup: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                textSummary.AppendLine($"       Computador: {Environment.MachineName}");
                textSummary.AppendLine("========================================================================");
                textSummary.AppendLine("ATENÇÃO: Este arquivo armazena as senhas de Wi-Fi em texto legível.");
                textSummary.AppendLine("Guarde este backup com segurança e exclua este arquivo após a restauração.");
                textSummary.AppendLine("========================================================================");
                textSummary.AppendLine();

                foreach (var xmlFile in xmlFiles)
                {
                    try
                    {
                        string content = File.ReadAllText(xmlFile, Encoding.UTF8);
                        string ssid = ExtractXmlValue(content, "name");
                        string key = ExtractXmlValue(content, "keyMaterial");
                        string auth = ExtractXmlValue(content, "authentication");

                        if (string.IsNullOrWhiteSpace(ssid))
                            ssid = Path.GetFileNameWithoutExtension(xmlFile).Replace("Wi-Fi-", "");

                        if (string.IsNullOrWhiteSpace(key))
                            key = "(rede aberta / sem senha)";

                        if (string.IsNullOrWhiteSpace(auth))
                            auth = "WPA2PSK";

                        var item = new WiFiNetworkProfile
                        {
                            Ssid = ssid,
                            Password = key,
                            Authentication = auth,
                            XmlFilePath = xmlFile
                        };
                        exported.Add(item);

                        textSummary.AppendLine($"Rede Wi-Fi (SSID): {ssid}");
                        textSummary.AppendLine($"Senha da Rede:     {key}");
                        textSummary.AppendLine($"Tipo de Segurança: {auth}");
                        textSummary.AppendLine("------------------------------------------------------------------------");
                    }
                    catch { }
                }

                // Salva o resumo legível em texto
                string txtPath = Path.Combine(targetDir, "Redes-WiFi-Senhas.txt");
                File.WriteAllText(txtPath, textSummary.ToString(), Encoding.UTF8);

                // Gera o script de restauração automática .bat
                var bat = new StringBuilder();
                bat.AppendLine("@echo off");
                bat.AppendLine("chcp 65001 >nul");
                bat.AppendLine("echo ========================================================================");
                bat.AppendLine("echo       LUMINI TRAY - RESTAURADOR AUTOMATICO DE REDES WI-FI");
                bat.AppendLine("echo ========================================================================");
                bat.AppendLine("echo.");
                bat.AppendLine("for %%f in (\"%~dp0Wi-Fi-*.xml\" \"%~dp0*.xml\") do (");
                bat.AppendLine("    echo Restaurando perfil: %%~nxf ...");
                bat.AppendLine("    netsh wlan add profile filename=\"%%f\" user=all >nul 2>&1");
                bat.AppendLine(")");
                bat.AppendLine("echo.");
                bat.AppendLine("echo ========================================================================");
                bat.AppendLine("echo [SUCESSO] Todas as redes Wi-Fi salvas foram restauradas no Windows!");
                bat.AppendLine("echo ========================================================================");
                bat.AppendLine("pause");

                string batPath = Path.Combine(targetDir, "Restaurar-WiFi.bat");
                File.WriteAllText(batPath, bat.ToString(), Encoding.Latin1);

                return (true, $"{exported.Count} rede(s) Wi-Fi exportada(s) com sucesso com suas respectivas senhas.", exported);
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao exportar redes Wi-Fi: {ex.Message}", exported);
            }
        });

    public static async Task<(bool Success, string Message, int RestoredCount)> RestoreWiFiProfilesAsync(string sourceDir) =>
        await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(sourceDir))
                    return (false, "Pasta selecionada não existe.", 0);

                var xmlFiles = Directory.GetFiles(sourceDir, "*.xml");
                if (xmlFiles.Length == 0)
                    return (false, "Nenhum arquivo de perfil Wi-Fi (.xml) encontrado na pasta.", 0);

                int ok = 0;
                foreach (var xml in xmlFiles)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "netsh.exe",
                            Arguments = $"wlan add profile filename=\"{xml}\" user=all",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(3000);
                        if (p?.ExitCode == 0) ok++;
                    }
                    catch { }
                }

                return (true, $"{ok} de {xmlFiles.Length} rede(s) Wi-Fi restaurada(s) com sucesso no sistema.", ok);
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao restaurar redes Wi-Fi: {ex.Message}", 0);
            }
        });

    private static string ExtractXmlValue(string xml, string tag)
    {
        string open = $"<{tag}>";
        string close = $"</{tag}>";
        int start = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start == -1) return "";
        start += open.Length;
        int end = xml.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
        if (end == -1) return "";
        return xml[start..end].Trim();
    }
}

/// <summary>
/// Perfil de rede Wi-Fi salvo com suas credenciais para backup e restauração pós-formatação.
/// </summary>
public sealed class WiFiNetworkProfile
{
    public string Ssid { get; set; } = "";
    public string Password { get; set; } = "";
    public string Authentication { get; set; } = "WPA2PSK";
    public string XmlFilePath { get; set; } = "";

    public string Icon => "📶";
    public string Subtitle => $"{Authentication} · Senha: {Password}";
}
