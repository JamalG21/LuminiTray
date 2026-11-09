using System;
using System.Diagnostics;
using System.IO;

namespace LuminiTray.Wallpaper;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "mpv-core.exe"),
            Path.Combine(baseDir, "mpv.exe"),
            Path.Combine(baseDir, "Assets", "Plugins", "mpv", "mpv-core.exe"),
            Path.Combine(baseDir, "Assets", "Plugins", "mpv", "mpv.exe")
        };

        string? coreExe = null;
        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                coreExe = c;
                break;
            }
        }

        if (coreExe == null)
        {
            return 1;
        }

        string fullArgs = string.Join(" ", args);
        var psi = new ProcessStartInfo
        {
            FileName = coreExe,
            Arguments = fullArgs,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(coreExe)
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return 2;

            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch
        {
            return 3;
        }
    }
}
