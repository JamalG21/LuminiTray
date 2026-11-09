using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LuminiTray;

/// <summary>
/// Executa rotinas de segundo plano em subprocessos isolados sob o processo principal do Lumini Tray.
/// Cada subtarefa possui consumo isolado de CPU e memória no Gerenciador de Tarefas do Windows
/// e encerra automaticamente caso o processo pai seja finalizado.
/// </summary>
public static class SubtaskWorker
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent,
        IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint PM_REMOVE = 0x0001;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_CLOSE = 0x0010;

    public static void Run(string subtaskType, string[] args)
    {
        // Define o título da thread e console para identificação
        string friendlyName = subtaskType.ToLowerInvariant() switch
        {
            "audio-suppressor" => "Lumini Tray - Supressor de Ruído",
            "metrics-monitor" => "Lumini Tray - Monitor de Hardware",
            "fan-controller" => "Lumini Tray - Controlador de Fans",
            _ => $"Lumini Tray - Subtarefa ({subtaskType})"
        };

        try
        {
            Console.Title = friendlyName;
        }
        catch { }

        // Carrega configurações da aplicação
        var settings = AppSettings.Load();

        using var cancelSource = new CancellationTokenSource();

        // Monitor do processo pai: se o pai morrer, a subtarefa encerra imediatamente
        int parentPid = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--parent-pid=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(args[i].Substring("--parent-pid=".Length), out parentPid);
            }
        }

        if (parentPid > 0)
        {
            new Thread(() =>
            {
                try
                {
                    var parent = Process.GetProcessById(parentPid);
                    parent.WaitForExit();
                }
                catch { }
                finally
                {
                    try { cancelSource.Cancel(); } catch { }
                    Environment.Exit(0);
                }
            })
            { IsBackground = true }.Start();
        }

        // Cria janela de segundo plano independente (IntPtr.Zero como parentHwnd para evitar deadlock
        // cross-process no DestroyWindow do processo principal)
        IntPtr hostHwnd = IntPtr.Zero;
        try
        {
            hostHwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                "static",
                friendlyName,
                WS_POPUP | WS_VISIBLE,
                -32000, -32000, 1, 1,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero
            );
        }
        catch { }

        try
        {
            // Executa a carga específica
            switch (subtaskType.ToLowerInvariant())
            {
                case "audio-suppressor":
                    RunAudioSuppressor(settings, cancelSource.Token);
                    break;

                case "metrics-monitor":
                    RunMetricsMonitor(cancelSource.Token);
                    break;

                case "fan-controller":
                    RunFanController(settings, cancelSource.Token);
                    break;

                default:
                    Debug.WriteLine($"[SubtaskWorker] Tipo de subtarefa desconhecido: {subtaskType}");
                    break;
            }
        }
        finally
        {
            if (hostHwnd != IntPtr.Zero)
            {
                try { DestroyWindow(hostHwnd); } catch { }
            }
        }
    }

    /// <summary>
    /// Drena a fila de mensagens Win32 periodicamente para manter a janela responsiva ao Windows Explorer / Taskmgr.
    /// </summary>
    private static void PumpWaitLoop(CancellationToken token, int sleepMs)
    {
        while (!token.IsCancellationRequested)
        {
            while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                if (msg.message == WM_QUIT || msg.message == WM_CLOSE)
                {
                    Environment.Exit(0);
                    return;
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            Thread.Sleep(sleepMs);
        }
    }

    private static void RunAudioSuppressor(AppSettings settings, CancellationToken token)
    {
        try
        {
            var svc = MicrophoneSuppressorService.Instance;
            svc.SelectedInputDeviceId = settings.SuppressorSelectedInputId;
            svc.SelectedVirtualCableDeviceId = settings.SuppressorVirtualMicDeviceId;
            svc.SelectedOutputDeviceId = settings.SuppressorSelectedOutputId;
            svc.Engine.SuppressionIntensity = settings.SuppressorIntensity;

            if (settings.SuppressorMasterEnabled && !string.IsNullOrEmpty(settings.SuppressorSelectedInputId))
            {
                svc.MasterEnabled = true;
            }

            PumpWaitLoop(token, 100);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SubtaskWorker:AudioSuppressor] Erro: {ex.Message}");
        }
    }

    private static void RunMetricsMonitor(CancellationToken token)
    {
        try
        {
            var svc = MiniTaskManagerService.Instance;
            PumpWaitLoop(token, 100);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SubtaskWorker:MetricsMonitor] Erro: {ex.Message}");
        }
    }

    private static void RunFanController(AppSettings settings, CancellationToken token)
    {
        try
        {
            using var monitor = new SensorMonitor();
            using var svc = new FanControlService(monitor);
            svc.DiscoverFans();
            svc.ApplySettings(settings.FanControl);
            svc.StartBackgroundLoop(1000);

            PumpWaitLoop(token, 100);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SubtaskWorker:FanController] Erro: {ex.Message}");
        }
    }
}
