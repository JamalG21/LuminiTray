using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace LuminiTray;

/// <summary>Representa um Atalho Especial criado na Área de Trabalho.</summary>
public sealed record SpecialShortcutItem(
    string DisplayName,
    string TaskName,
    string ShortcutPath,
    string Arguments);

/// <summary>Representa um executável encontrado no sistema para seleção rápida.</summary>
public sealed record SpecialShortcutExeCandidate(
    string Name,
    string ExePath,
    string Source)
{
    public string Subtitle => $"{Source} · {ExePath}";
}

/// <summary>
/// Gerencia a criação, listagem e remoção de Atalhos Especiais (execução elevada sem UAC via Agendador de Tarefas + .lnk).
/// </summary>
public static class SpecialShortcutService
{
    public const string TaskPrefix = "AtalhoEspecial_";
    public static string SchtasksExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "schtasks.exe");

    /// <summary>Gera um nome de tarefa único sem espaços ou caracteres inválidos: AtalhoEspecial_NomeDoApp.</summary>
    public static string BuildTaskName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return TaskPrefix + "App";

        string normalized = rawName.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (char ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch))
                sb.Append('_');
        }

        string cleaned = Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        if (string.IsNullOrEmpty(cleaned))
            cleaned = "App";
        if (cleaned.StartsWith(TaskPrefix, StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[TaskPrefix.Length..];
        if (string.IsNullOrEmpty(cleaned))
            cleaned = "App";

        return TaskPrefix + cleaned;
    }

    public static string BuildSchtasksArguments(string taskName) => $"/run /tn \"{taskName}\"";

    public static string BuildRegisterTaskPowerShellScript(string exePath, string taskName)
    {
        static string Esc(string s) => s.Replace("'", "''");
        string safeExe = Esc(exePath);
        string safeTask = Esc(taskName);

        return string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"$action = New-ScheduledTaskAction -Execute '{safeExe}'",
            "$principal = New-ScheduledTaskPrincipal -UserId \"$env:USERDOMAIN\\$env:USERNAME\" -LogonType Interactive -RunLevel Highest",
            "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds 0)",
            $"Register-ScheduledTask -TaskName '{safeTask}' -Action $action -Principal $principal -Settings $settings -Force | Out-Null"
        });
    }

    public static bool IsProcessElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cria a tarefa elevada no Agendador de Tarefas (pedindo UAC apenas se necessário) e gera o .lnk na Área de Trabalho.
    /// </summary>
    public static (bool Success, string Message, SpecialShortcutItem? Item) CreateSpecialShortcut(string exePath, string shortcutName)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return (false, "Selecione um arquivo executável (.exe) válido.", null);

        string cleanDisplayName = string.IsNullOrWhiteSpace(shortcutName)
            ? Path.GetFileNameWithoutExtension(exePath)
            : shortcutName.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
            cleanDisplayName = cleanDisplayName.Replace(invalid, '_');
        if (string.IsNullOrWhiteSpace(cleanDisplayName))
            cleanDisplayName = "AtalhoEspecial";

        string taskName = BuildTaskName(cleanDisplayName);
        string script = BuildRegisterTaskPowerShellScript(Path.GetFullPath(exePath), taskName);

        var psResult = RunPowerShellElevated(script);
        if (!psResult.Success)
            return (false, psResult.Message, null);

        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string lnkPath = Path.Combine(desktopDir, $"{cleanDisplayName}.lnk");
        string arguments = BuildSchtasksArguments(taskName);

        try
        {
            CreateWindowsShortcut(lnkPath, SchtasksExePath, arguments, Path.GetFullPath(exePath));
        }
        catch (Exception ex)
        {
            return (false, $"Tarefa '{taskName}' criada, mas falhou ao gerar o atalho na Área de Trabalho: {ex.Message}", null);
        }

        var item = new SpecialShortcutItem(cleanDisplayName, taskName, lnkPath, arguments);
        return (true, $"Atalho '{cleanDisplayName}.lnk' criado na Área de Trabalho (tarefa: {taskName}).", item);
    }

    /// <summary>Descobre executáveis (.exe) instalados via Menu Iniciar e Registro para seleção rápida.</summary>
    public static async System.Threading.Tasks.Task<List<SpecialShortcutExeCandidate>> DiscoverExeCandidatesAsync()
    {
        var byPath = new Dictionary<string, SpecialShortcutExeCandidate>(StringComparer.OrdinalIgnoreCase);

        static bool IsIgnoredExe(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            return file.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
                || file.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
                || file.Equals("setup", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith("crashreport", StringComparison.OrdinalIgnoreCase);
        }

        await System.Threading.Tasks.Task.Run(() =>
        {
            var startDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs)
            };

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic? shell = shellType != null ? Activator.CreateInstance(shellType) : null;

            if (shell != null)
            {
                foreach (string dir in startDirs)
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    IEnumerable<string> lnks;
                    try { lnks = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories); }
                    catch { continue; }

                    foreach (string lnk in lnks)
                    {
                        try
                        {
                            dynamic sc = shell.CreateShortcut(lnk);
                            string target = ((string?)sc.TargetPath ?? "").Trim();
                            if (string.IsNullOrEmpty(target)
                                || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                || !File.Exists(target)
                                || IsIgnoredExe(target))
                                continue;

                            string name = Path.GetFileNameWithoutExtension(lnk);
                            byPath.TryAdd(target, new SpecialShortcutExeCandidate(name, target, "Menu Iniciar"));
                        }
                        catch { }
                    }
                }
            }
        });

        try
        {
            var apps = await AppUninstaller.GetInstalledAppsAsync();
            foreach (var app in apps)
            {
                string candidate = CleanExeFromIconPath(app.DisplayIcon);
                if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate) || IsIgnoredExe(candidate))
                    continue;

                string name = string.IsNullOrWhiteSpace(app.DisplayName)
                    ? Path.GetFileNameWithoutExtension(candidate)
                    : app.DisplayName.Trim();
                byPath.TryAdd(candidate, new SpecialShortcutExeCandidate(name, candidate, "Instalado"));
            }
        }
        catch { }

        return byPath.Values
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string CleanExeFromIconPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim().Trim('"');
        int commaIdx = s.LastIndexOf(',');
        if (commaIdx > 0)
            s = s[..commaIdx].Trim().Trim('"');
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s : "";
    }

    /// <summary>Lista os atalhos especiais existentes na Área de Trabalho que apontam para schtasks.exe /run /tn "AtalhoEspecial_...".</summary>
    public static List<SpecialShortcutItem> ListDesktopSpecialShortcuts()
    {
        var result = new List<SpecialShortcutItem>();
        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(desktopDir)) return result;

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return result;

        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return result;

            foreach (string lnkFile in Directory.GetFiles(desktopDir, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    dynamic shortcut = shell.CreateShortcut(lnkFile);
                    string target = (string?)shortcut.TargetPath ?? "";
                    string args = (string?)shortcut.Arguments ?? "";

                    if (!target.EndsWith("schtasks.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var m = Regex.Match(args, $@"/tn\s+""?({Regex.Escape(TaskPrefix)}[^\s""]+)""?", RegexOptions.IgnoreCase);
                    if (!m.Success)
                        continue;

                    result.Add(new SpecialShortcutItem(
                        Path.GetFileNameWithoutExtension(lnkFile),
                        m.Groups[1].Value,
                        lnkFile,
                        args));
                }
                catch { }
            }
        }
        catch { }

        return result.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Remove a tarefa do Agendador de Tarefas e exclui o arquivo .lnk da Área de Trabalho.</summary>
    public static (bool Success, string Message) RemoveSpecialShortcut(SpecialShortcutItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.TaskName))
            return (false, "Atalho inválido.");

        var delRes = RunSchtasksDeleteElevated(item.TaskName);
        if (!delRes.Success)
            return (false, delRes.Message);

        try
        {
            if (File.Exists(item.ShortcutPath))
                File.Delete(item.ShortcutPath);
        }
        catch (Exception ex)
        {
            return (false, $"Tarefa removida, mas falhou ao excluir o arquivo .lnk: {ex.Message}");
        }

        return (true, $"Atalho especial '{item.DisplayName}' e tarefa '{item.TaskName}' removidos.");
    }

    private static void CreateWindowsShortcut(string lnkPath, string targetPath, string arguments, string iconExePath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new InvalidOperationException("WScript.Shell não disponível neste sistema.");

        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Não foi possível instanciar WScript.Shell.");

        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = arguments;
        shortcut.WindowStyle = 7; // Minimizado (evita terminal aberto)
        shortcut.IconLocation = $"{iconExePath},0";
        shortcut.Description = "Atalho Especial (Admin sem UAC) - Lumini Tray";
        shortcut.Save();
    }

    private static (bool Success, string Message) RunPowerShellElevated(string script)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        bool elevated = IsProcessElevated();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = !elevated,
                CreateNoWindow = elevated,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = elevated ? "" : "runas",
                RedirectStandardError = elevated,
                RedirectStandardOutput = elevated
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (false, "Não foi possível iniciar o PowerShell.");

            proc.WaitForExit(30000);
            if (proc.ExitCode != 0)
            {
                string err = elevated ? proc.StandardError.ReadToEnd().Trim() : "";
                return (false, string.IsNullOrEmpty(err)
                    ? $"PowerShell retornou código de saída {proc.ExitCode}."
                    : $"Erro ao registrar tarefa: {err}");
            }

            return (true, "Tarefa registrada com sucesso.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Permissão UAC cancelada pelo usuário.");
        }
        catch (Exception ex)
        {
            return (false, $"Falha ao executar PowerShell: {ex.Message}");
        }
    }

    private static (bool Success, string Message) RunSchtasksDeleteElevated(string taskName)
    {
        bool elevated = IsProcessElevated();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SchtasksExePath,
                Arguments = $"/delete /tn \"{taskName}\" /f",
                UseShellExecute = !elevated,
                CreateNoWindow = elevated,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = elevated ? "" : "runas",
                RedirectStandardError = elevated,
                RedirectStandardOutput = elevated
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (false, "Não foi possível iniciar schtasks.exe.");

            proc.WaitForExit(15000);
            if (proc.ExitCode != 0)
            {
                string err = elevated ? proc.StandardError.ReadToEnd().Trim() : "";
                // Código 1 pode ocorrer se a tarefa já não existir no Agendador
                if (!err.Contains("não existe", StringComparison.OrdinalIgnoreCase) &&
                    !err.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, string.IsNullOrEmpty(err)
                        ? $"schtasks retornou código {proc.ExitCode}."
                        : $"Erro ao remover tarefa: {err}");
                }
            }

            return (true, "Tarefa removida.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Permissão UAC cancelada pelo usuário.");
        }
        catch (Exception ex)
        {
            return (false, $"Falha ao remover tarefa: {ex.Message}");
        }
    }
}
