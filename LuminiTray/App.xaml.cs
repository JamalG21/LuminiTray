using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace LuminiTray;

public partial class App : Application
{
    // Uma única instância: abrir o Lumini Tray de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;
    private static bool _ownsMutex;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try
            {
                string logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuminiTray", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.WriteAllText(logPath, ev.ExceptionObject?.ToString());
                MessageBox.Show($"Ocorreu um erro ao iniciar o Lumini Tray:\n\n{ev.ExceptionObject}", "Lumini Tray - Erro de Inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        };

        DispatcherUnhandledException += (s, ev) =>
        {
            try
            {
                string logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuminiTray", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ev.Exception}\n\n");
            }
            catch { }

            // Marca a exceção como tratada para evitar a segunda janela ("Erro de Inicialização")
            // disparada por AppDomain.UnhandledException e o encerramento abrupto do processo.
            ev.Handled = true;

            bool mainWindowReady = Current?.MainWindow != null;
            try
            {
                if (mainWindowReady)
                {
                    MessageBox.Show($"Ocorreu um erro inesperado:\n\n{ev.Exception?.Message}", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // Falha antes da janela principal existir: não há UI para continuar, então encerra.
                    MessageBox.Show($"Ocorreu um erro ao iniciar o Lumini Tray:\n\n{ev.Exception}", "Lumini Tray - Erro de Inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch { }

            if (!mainWindowReady)
            {
                Current?.Shutdown(1);
            }
        };

        // 1. Suporte a testes automatizados por linha de comando (--test ou --selftest)
        if (e.Args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase) ||
                           a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            int code = AutomatedTests.RunAll();
            Shutdown(code);
            return;
        }

        // 2. Subtarefas e rotinas de segundo plano em processos isolados
        string? subtaskArg = e.Args.FirstOrDefault(a => a.StartsWith("--subtask=", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(subtaskArg))
        {
            string subtaskType = subtaskArg.Substring("--subtask=".Length);
            SubtaskWorker.Run(subtaskType, e.Args);
            Shutdown(0);
            return;
        }

        var settings = AppSettings.Load();

        // Auto-reparo: se o programa foi movido de pasta, atualiza o caminho do início automático.
        bool syncMinimized = settings.AutoStartMinimized;
        _ = System.Threading.Tasks.Task.Run(() => AutoStartManager.SyncExecutablePath(syncMinimized));
        bool isAutoStart = e.Args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        bool isMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) || (isAutoStart && settings.StartMinimizedToTray) || settings.StartMinimizedToTray;

        bool first;
        try { _single = new Mutex(true, @"Local\LuminiTray.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; }
        if (first) _ownsMutex = true; // criado por outra instância com outro nível de permissão

        if (!first)
        {
            IntPtr h = FindWindow(null, "Lumini Tray");
            if (h != IntPtr.Zero)
            {
                ShowWindow(h, 9); // SW_RESTORE
                ShowWindow(h, 5); // SW_SHOW
                SetForegroundWindow(h);
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var mainWindow = new MainWindow(isAutoStart, isMinimized);
        if (isMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.Show();
            mainWindow.Hide();
            LuminiTray.MainWindow.TrimMemory();
        }
        else
        {
                    mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try { _single?.ReleaseMutex(); } catch { }
            try { _single?.Dispose(); } catch { }
            _ownsMutex = false;
        }
        base.OnExit(e);
        Environment.Exit(e.ApplicationExitCode);
    }
}
