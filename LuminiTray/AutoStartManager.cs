using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Gerencia a inicialização automática do Lumini Tray junto com o Windows.
/// Utiliza o Agendador de Tarefas do Windows (schtasks) com privilégios elevados (highest)
/// para que o aplicativo inicie como administrador sem sofrer bloqueio silencioso do UAC,
/// e mantém sincronizado com a chave Run do registro do usuário para compatibilidade.
/// </summary>
public static class AutoStartManager
{
    public const string TaskName = "LuminiTrayAutoStart";
    public const string RegistryValueName = "LuminiTray";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Verifica se a tarefa agendada no Windows existe e está ativa.</summary>
    public static bool IsScheduledTaskConfigured()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/query /tn \"{TaskName}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Verifica se a chave Run do registro do usuário contém o Lumini Tray.</summary>
    public static bool IsRegistryRunConfigured()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath);
            return key?.GetValue(RegistryValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Indica se a inicialização automática está habilitada (via Agendador ou Registro).</summary>
    public static bool IsAutoStartEnabled() => IsScheduledTaskConfigured() || IsRegistryRunConfigured();

    /// <summary>Obtém o caminho do executável do Lumini Tray.</summary>
    public static string GetExecutablePath()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, "LuminiTray.exe");
            if (File.Exists(candidate)) return candidate;

            string distCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\LuminiTray-dist\LuminiTray.exe"));
            if (File.Exists(distCandidate)) return distCandidate;
        }
        return exe ?? "";
    }

    /// <summary>
    /// Ativa ou desativa a inicialização automática junto com o Windows.
    /// </summary>
    public static void SetAutoStart(bool enable, bool minimized = true)
    {
        if (enable)
        {
            string exe = GetExecutablePath();
            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException("Não foi possível determinar o executável do Lumini Tray para agendar a inicialização.");

            string args = "--autostart" + (minimized ? " --minimized" : "");
            string fullCmd = $"\"{exe}\" {args}";

            // 1. Cria tarefa no Agendador do Windows com privilégios de administrador (/rl highest)
            // ao fazer logon do usuário atual (/sc onlogon).
            string schArgs = $"/create /tn \"{TaskName}\" /tr \"\\\"{exe}\\\" {args}\" /sc onlogon /rl highest /f";
            var psiTask = new ProcessStartInfo("schtasks.exe", schArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var pTask = Process.Start(psiTask);
            pTask?.WaitForExit(5000);

            // 2. Registra na chave Run do usuário para visibilidade e compatibilidade
            try
            {
                using var runKey = Registry.CurrentUser.CreateSubKey(RunPath);
                runKey?.SetValue(RegistryValueName, fullCmd, RegistryValueKind.String);
            }
            catch { }
        }
        else
        {
            // 1. Remove do Agendador de Tarefas
            try
            {
                var psiDel = new ProcessStartInfo("schtasks.exe", $"/delete /tn \"{TaskName}\" /f")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var pDel = Process.Start(psiDel);
                pDel?.WaitForExit(5000);
            }
            catch { }

            // 2. Remove da chave Run do registro
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
                runKey?.DeleteValue(RegistryValueName, false);
            }
            catch { }
        }
    }

    /// <summary>Retorna um resumo descritivo do status da inicialização automática.</summary>
    public static string GetStatusSummary()
    {
        bool sched = IsScheduledTaskConfigured();
        bool reg = IsRegistryRunConfigured();
        if (sched) return "Ativado (Agendador de Tarefas · Privilégios de Administrador)";
        if (reg) return "Ativado (Registro do Windows)";
        return "Desativado";
    }
}
