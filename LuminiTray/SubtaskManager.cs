using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LuminiTray;

/// <summary>
/// Gerencia a exibição e identificação das subtarefas do Lumini Tray no Gerenciador de Tarefas do Windows.
/// Cria janelas nativas de segundo plano vinculadas ao processo principal (HWND Owner) sem poluir a barra
/// de tarefas, permitindo que o usuário visualize cada subtarefa com seu nome amigável e consumo agrupado
/// na aba 'Processos' do Gerenciador de Tarefas (Taskmgr) e possa finalizar individualmente qualquer uma delas via 'Finalizar Tarefa'.
/// </summary>
public static class SubtaskManager
{
    private sealed class SubtaskWindowHandle : IDisposable
    {
        public string Key { get; }
        public string Title { get; }
        public IntPtr Hwnd { get; private set; }
        public Action OnEndTask { get; }
        private readonly WndProcDelegate _wndProcDelegate;
        private volatile bool _isDisposing;

        public SubtaskWindowHandle(string key, string title, Action onEndTask)
        {
            Key = key;
            Title = title;
            OnEndTask = onEndTask;
            _wndProcDelegate = CustomWndProc;

            EnsureClassRegistered();

            // Janela top-level pura (IntPtr.Zero como parent): nunca é ocultada quando MainWindow
            // for minimizada ou ocultada na bandeja do sistema, permanecendo continuamente agrupada
            // sob 'LuminiTray' na aba Processos do Gerenciador de Tarefas do Windows.
            Hwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                ClassName,
                title,
                WS_POPUP | WS_VISIBLE,
                -32000, -32000, 1, 1,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero
            );

            if (Hwnd != IntPtr.Zero)
            {
                SetWindowLongPtr(Hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
                IntPtr hIcon = GetAppIconHandle();
                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        SendMessage(Hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hIcon);
                        SendMessage(Hwnd, WM_SETICON, (IntPtr)ICON_BIG, hIcon);
                    }
                    catch { }
                }
                ShowWindow(Hwnd, SW_SHOWNA);
            }
        }

        public void EnsureVisible()
        {
            if (!_isDisposing && Hwnd != IntPtr.Zero)
            {
                if (!IsWindowVisible(Hwnd))
                {
                    ShowWindow(Hwnd, SW_SHOWNA);
                }
            }
        }

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (!_isDisposing && (msg == WM_CLOSE || (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_CLOSE)))
            {
                _isDisposing = true;
                try
                {
                    OnEndTask();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SubtaskManager] Erro ao encerrar subtarefa {Key}: {ex.Message}");
                }
                return IntPtr.Zero;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            _isDisposing = true;
            if (Hwnd != IntPtr.Zero)
            {
                try
                {
                    SetWindowLongPtr(Hwnd, GWLP_HWNDPARENT, IntPtr.Zero);
                }
                catch { }
                try
                {
                    DestroyWindow(Hwnd);
                }
                catch { }
                Hwnd = IntPtr.Zero;
            }
        }
    }

    private static readonly ConcurrentDictionary<string, SubtaskWindowHandle> _activeSubtasks = new(StringComparer.OrdinalIgnoreCase);
    private static bool _classRegistered = false;
    private static readonly object _classLock = new();
    private const string ClassName = "LuminiTray_SubtaskHost";

    private static IntPtr _mainHwnd = IntPtr.Zero;
    private static System.Threading.Timer? _keepAliveTimer;

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_SHOWNA = 8;
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;
    private const int GWLP_WNDPROC = -4;
    private const int GWLP_HWNDPARENT = -8;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr _cachedAppIcon = IntPtr.Zero;

    private static IntPtr GetAppIconHandle()
    {
        if (_cachedAppIcon != IntPtr.Zero) return _cachedAppIcon;
        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    _cachedAppIcon = icon.Handle;
                }
            }
        }
        catch { }
        return _cachedAppIcon;
    }

    private static void EnsureClassRegistered()
    {
        lock (_classLock)
        {
            if (_classRegistered) return;

            IntPtr iconHandle = GetAppIconHandle();
            var wcx = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = DefWindowProc,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = GetModuleHandle(null),
                hIcon = iconHandle,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = ClassName,
                hIconSm = iconHandle
            };

            RegisterClassEx(ref wcx);
            _classRegistered = true;
        }
    }

    /// <summary>
    /// Registra o HWND da janela principal para referência global interna.
    /// As subtarefas mantêm HWND independente (unowned) para não serem ocultadas quando o app minimizar na bandeja.
    /// </summary>
    public static void SetMainHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        _mainHwnd = hwnd;
    }

    private static void EnsureWatchdogRunning()
    {
        if (_keepAliveTimer != null) return;
        _keepAliveTimer = new System.Threading.Timer(_ =>
        {
            foreach (var sub in _activeSubtasks.Values)
            {
                try { sub.EnsureVisible(); } catch { }
            }
        }, null, 2000, 3000);
    }

    /// <summary>
    /// Registra uma subtarefa ativa do Lumini Tray para exibição amigável no Gerenciador de Tarefas do Windows.
    /// </summary>
    /// <param name="key">Identificador único (ex: "wallpaper", "audio-suppressor", "auto-optimizer", "metrics-monitor").</param>
    /// <param name="friendlyTitle">Nome exibido na árvore do Task Manager (ex: "Lumini Tray - Papel de Parede Animado").</param>
    /// <param name="onEndTask">Ação executada quando o usuário clica em 'Finalizar Tarefa' no Gerenciador.</param>
    public static void RegisterSubtask(string key, string friendlyTitle, Action onEndTask)
    {
        UnregisterSubtask(key);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        void CreateSub()
        {
            try
            {
                var sub = new SubtaskWindowHandle(key, friendlyTitle, onEndTask);
                _activeSubtasks[key] = sub;
                EnsureWatchdogRunning();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SubtaskManager] Falha ao registrar subtarefa {key}: {ex.Message}");
            }
        }

        if (dispatcher.CheckAccess())
            CreateSub();
        else
            dispatcher.BeginInvoke(CreateSub);
    }

    /// <summary>
    /// Remove a subtarefa do registro quando ela for desativada normalmente pelo aplicativo.
    /// </summary>
    public static void UnregisterSubtask(string key)
    {
        if (_activeSubtasks.TryRemove(key, out var sub))
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                sub.Dispose();
            }
            else
            {
                dispatcher.BeginInvoke(() => sub.Dispose());
            }
        }
    }

    /// <summary>
    /// Remove todas as subtarefas registradas no encerramento do Lumini Tray.
    /// </summary>
    public static void UnregisterAll()
    {
        try { _keepAliveTimer?.Dispose(); } catch { }
        _keepAliveTimer = null;

        foreach (var key in _activeSubtasks.Keys)
        {
            UnregisterSubtask(key);
        }
    }
}
