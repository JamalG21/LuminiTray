using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

/// <summary>
/// Mod de controle de volume da barra de tarefas de altíssimo desempenho (Windhawk Taskbar Volume Control nativo).
/// Permite rolar a roda do mouse sobre a barra de tarefas para ajustar o volume geral do sistema
/// ou de uma aplicação específica ao mover o cursor sobre o atalho dela e segurar uma tecla modificadora (ex: Ctrl Esquerdo).
/// Também permite definir um aplicativo fixo para controle imediato com a tecla modificadora (ex: Spotify, Discord).
/// 
/// Arquitetura de Latência Zero:
/// 1. O gancho de mouse (WH_MOUSE_LL) é ultra-seletivo: ignora todos os cliques normais (esquerdo, direito, movimento) em ~1ns.
/// 2. NUNCA engole mensagens de clique (WM_LBUTTON*, WM_RBUTTON*, WM_MBUTTON*), garantindo que o mouse NUNCA trave ou perca cliques.
/// 3. Detecção de colisão da barra de tarefas ultra-rápida via cache de retângulos em memória (sem chamadas bloqueantes de Win32/UIA no hook).
/// 4. Coalescência atômica de giro rápido (Interlocked Accumulator): giros rápidos do mouse não disparam avalanche de threads.
/// 5. Resolução assíncrona inteligente com cache de proximidade espacial e suporte nativo a múltiplos monitores.
/// </summary>
public sealed class TaskbarVolumeHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const uint WM_QUIT = 0x0012;
    private const uint GA_ROOT = 2;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string? windowTitle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        [return: MarshalAs(UnmanagedType.IDispatch)] object get_accParent();
        int get_accChildCount();
        [return: MarshalAs(UnmanagedType.IDispatch)] object get_accChild([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accName([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accValue([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accDescription([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.Struct)] object get_accRole([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.Struct)] object get_accState([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accHelp([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        int get_accHelpTopic([Out, MarshalAs(UnmanagedType.BStr)] out string pszHelpFile, [In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accKeyboardShortcut([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.IDispatch)] object get_accFocus();
        [return: MarshalAs(UnmanagedType.IDispatch)] object get_accSelection();
        [return: MarshalAs(UnmanagedType.BStr)] string get_accDefaultAction([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        void accSelect(int flagsSelect, [In, MarshalAs(UnmanagedType.Struct)] object varChild);
        void accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, [In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.IDispatch)] object accNavigate(int navDir, [In, MarshalAs(UnmanagedType.Struct)] object varChild);
        [return: MarshalAs(UnmanagedType.IDispatch)] object accHitTest(int xLeft, int yTop);
        void accDoDefaultAction([In, MarshalAs(UnmanagedType.Struct)] object varChild);
        void set_accName([In, MarshalAs(UnmanagedType.Struct)] object varChild, [In, MarshalAs(UnmanagedType.BStr)] string pszName);
        void set_accValue([In, MarshalAs(UnmanagedType.Struct)] object varChild, [In, MarshalAs(UnmanagedType.BStr)] string pszValue);
    }

    [DllImport("oleacc.dll", PreserveSig = true)]
    private static extern int AccessibleObjectFromPoint(
        POINT pt,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible ppvObject,
        [MarshalAs(UnmanagedType.Struct)] out object pvarChild);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);
    }

    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private readonly object _hookLock = new();
    private readonly LowLevelMouseProc _proc;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private volatile bool _isRunning;
    private IntPtr _hookId = IntPtr.Zero;

    private readonly AudioSettings _settings;
    private readonly Action<string, int, int, int> _showOsdAction;

    // Cache de áreas das barras de tarefas (Principal + Monitores Secundários)
    private static volatile RECT[] _cachedTaskbarRects = Array.Empty<RECT>();
    private static long _lastTaskbarRectRefresh = 0;

    // Fila Coalescente de Giro de Scroll
    private int _accumulatedWheelDelta = 0;
    private int _lastMouseX = 0;
    private int _lastMouseY = 0;
    private volatile bool _lastModifierActive = false;
    private readonly AutoResetEvent _workSignal = new(false);
    private Thread? _workerThread;

    // Cache espacial para app sob o atalho da barra
    private uint _cachedAppPid = 0;
    private string _cachedAppName = "";
    private int _lastResolvedX = 0;
    private int _lastResolvedY = 0;
    private long _lastResolvedTimestamp = 0;

    public TaskbarVolumeHook(AudioSettings settings, Action<string, int, int, int> showOsdAction)
    {
        _settings = settings;
        _showOsdAction = showOsdAction;
        _proc = HookCallback;

        RefreshTaskbarRectsCache();

        if (_settings.EnableTaskbarVolumeScroll)
        {
            StartHook();
        }
    }

    public void StartHook()
    {
        lock (_hookLock)
        {
            if (_hookThread != null && _hookThread.IsAlive) return;

            _isRunning = true;
            using var readyEvent = new ManualResetEventSlim(false);

            // Pré-aquece o UI Automation numa thread dedicada (primeira rolagem já resolve rápido)
            EnsureUiaThread();

            // Inicia thread de trabalho coalescente dedicada
            _workerThread = new Thread(WorkerLoop)
            {
                Name = "LuminiTray.TaskbarVolumeWorker",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _workerThread.Start();

            // Inicia thread de hook de mouse de alta prioridade
            _hookThread = new Thread(() =>
            {
                _hookThreadId = GetCurrentThreadId();
                PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

                try
                {
                    using var curProcess = Process.GetCurrentProcess();
                    using var curModule = curProcess.MainModule;
                    IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
                    _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, hMod, 0);
                }
                catch
                {
                    _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
                }

                readyEvent.Set();

                MSG msg;
                while (_isRunning && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (_hookId != IntPtr.Zero)
                {
                    try { UnhookWindowsHookEx(_hookId); } catch { }
                    _hookId = IntPtr.Zero;
                }
            })
            {
                Name = "LuminiTray.TaskbarVolumeHookThread",
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };

            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
            readyEvent.Wait(2000);
        }
    }

    public void StopHook()
    {
        lock (_hookLock)
        {
            _isRunning = false;
            _workSignal.Set();

            if (_hookThreadId != 0)
            {
                PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _hookThreadId = 0;
            }

            if (_hookThread != null)
            {
                if (_hookThread.IsAlive)
                {
                    _hookThread.Join(500);
                }
                _hookThread = null;
            }

            if (_workerThread != null)
            {
                if (_workerThread.IsAlive)
                {
                    _workerThread.Join(500);
                }
                _workerThread = null;
            }

            if (_hookId != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hookId); } catch { }
                _hookId = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Callback do gancho WH_MOUSE_LL.
    /// FILOSOFIA DE SEGURANÇA MÁXIMA:
    /// - Executa em ~1ns.
    /// - NUNCA consome ou engole cliques (WM_LBUTTON*, WM_RBUTTON*, WM_MBUTTON*).
    /// - Só consome o giro do scroll (WM_MOUSEWHEEL) se o cursor estiver comprovadamente sobre a barra de tarefas.
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;

            // 1. FAST-PATH: Descarta imediatamente qualquer mensagem que não seja WM_MOUSEWHEEL e WM_MBUTTONUP
            if (msg != WM_MOUSEWHEEL && msg != WM_MBUTTONUP)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (!_settings.EnableTaskbarVolumeScroll)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            int x = Marshal.ReadInt32(lParam, 0);
            int y = Marshal.ReadInt32(lParam, 4);

            // 2. Proteção de Jogos e Apps em Tela Cheia / Janela Sem Bordas (Valorant, CS2, etc.)
            // Descarta imediatamente se um jogo competitivo ou janela em tela cheia/sem bordas estiver em primeiro plano
            if (_settings.DisableTaskbarVolumeInFullscreen && IsForegroundFullscreenOrGame(x, y))
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            bool isTaskbar = IsPointOverTaskbarFast(x, y);

            // Clique do meio (scroll click) sobre a barra de tarefas: mutar/desmutar
            // IMPORTANTE: NUNCA consome WM_MBUTTONUP! Retorna CallNextHookEx para manter o estado do mouse 100% íntegro no Windows.
            if (msg == WM_MBUTTONUP)
            {
                if (_settings.MiddleClickToMute && isTaskbar)
                {
                    _lastMouseX = x;
                    _lastMouseY = y;
                    ThreadPool.QueueUserWorkItem(_ => HandleMiddleClickAsync(x, y));
                }
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // Giro do scroll
            if (msg == WM_MOUSEWHEEL)
            {
                bool scrollAnywhere = _settings.ScrollAnywhereWithModifier && IsModifierActive(_settings.TaskbarPerAppModifierKey);

                if (isTaskbar || scrollAnywhere)
                {
                    uint mouseData = (uint)Marshal.ReadInt32(lParam, 8);
                    short delta = (short)((mouseData >> 16) & 0xFFFF);

                    // Coalescência atômica ultra-rápida e captura imediata da tecla modificadora
                    Interlocked.Add(ref _accumulatedWheelDelta, delta);
                    _lastMouseX = x;
                    _lastMouseY = y;
                    _lastModifierActive = IsModifierActive(_settings.TaskbarPerAppModifierKey);
                    _workSignal.Set();

                    // Consome apenas o giro da roda sobre a barra para não dar scroll em janelas de fundo
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Loop da thread de trabalho dedicada para processar giros acumulados de volume suavemente sem engasgos.
    /// </summary>
    private void WorkerLoop()
    {
        while (_isRunning)
        {
            _workSignal.WaitOne(100);
            if (!_isRunning) break;

            int accumulated = Interlocked.Exchange(ref _accumulatedWheelDelta, 0);
            if (accumulated == 0) continue;

            int x = _lastMouseX;
            int y = _lastMouseY;
            bool modifierPressed = _lastModifierActive || IsModifierActive(_settings.TaskbarPerAppModifierKey);
            _lastModifierActive = false;

            ProcessAccumulatedScroll(accumulated, x, y, modifierPressed);
        }
    }

    private void ProcessAccumulatedScroll(int accumulatedDelta, int cursorX, int cursorY, bool modifierPressed)
    {
        // 120 = 1 tick padrão de scroll
        int notches = accumulatedDelta / 120;
        if (notches == 0) notches = accumulatedDelta > 0 ? 1 : -1;

        int step = Math.Clamp(_settings.TaskbarVolumeScrollStep, 1, 20);
        float deltaScalar = (notches * step) / 100f;

        bool isMod = modifierPressed;
        bool shouldControlPlayer = _settings.InvertTaskbarModifier ? !isMod : isMod;

        // Se a tecla modificadora (ou atalho) estiver acionada: altera o volume do Player Atual (Spotify, YouTube, VLC, etc.)
        if (shouldControlPlayer && _settings.TaskbarPerAppModifierKey is not ("Nenhum" or "Desativado"))
        {
            int playerVol = AudioCore.AdjustActiveMediaPlayerVolume(deltaScalar, out string activePlayer, _settings.ExcludedPlayerApps);
            if (playerVol >= 0)
            {
                _showOsdAction(activePlayer, playerVol, cursorX, cursorY);
                return;
            }
            else
            {
                _showOsdAction("Nenhum Player Ativo", -2, cursorX, cursorY);
                return;
            }
        }

        // Padrão imediato: Volume Geral / Mestre do Sistema
        int sysVol = AudioCore.AdjustDefaultRenderVolume(deltaScalar);
        if (sysVol >= 0)
        {
            _showOsdAction("Volume do Sistema", sysVol, cursorX, cursorY);
        }
    }

    private void HandleMiddleClickAsync(int cursorX, int cursorY)
    {
        try
        {
            bool isMod = IsModifierActive(_settings.TaskbarPerAppModifierKey);
            bool shouldControlPlayer = _settings.InvertTaskbarModifier ? !isMod : isMod;

            if (shouldControlPlayer && _settings.TaskbarPerAppModifierKey is not ("Nenhum" or "Desativado"))
            {
                if (AudioCore.ToggleActiveMediaPlayerMute(out string activePlayer, out bool isPlayerMuted, _settings.ExcludedPlayerApps))
                {
                    _showOsdAction(activePlayer, isPlayerMuted ? -1 : 0, cursorX, cursorY);
                    return;
                }
            }

            // Alterna mudo do sistema mestre
            bool nowMuted = AudioCore.ToggleDefaultMute(AudioInterop.EDataFlow.Render);
            float currentVolMaster = AudioCore.GetDefaultVolumeScalar(AudioInterop.EDataFlow.Render);
            int volPercent = Math.Clamp((int)Math.Round(currentVolMaster * 100f), 0, 100);
            _showOsdAction("Volume do Sistema", nowMuted ? -1 : volPercent, cursorX, cursorY);
        }
        catch { }
    }

    // ======================================================================================
    // RESOLUÇÃO DO BOTÃO DA BARRA DE TAREFAS SOB O CURSOR
    // Windows 11: a barra é uma ilha XAML ("DesktopWindowXamlSource"), então o MSAA retorna o nome
    // da janela-ilha e NÃO do botão. Por isso o MSAA só é aceito quando o papel (role) é de botão,
    // e a resolução principal é feita via UI Automation numa thread MTA dedicada e pré-aquecida,
    // procurando o elemento "Taskbar.TaskListButtonAutomationPeer" (Name + AutomationId/AUMID).
    // ======================================================================================

    private sealed record TaskbarButtonInfo(string Name, string AutomationId, string ClassName, bool IsTaskListButton);

    private const int ROLE_SYSTEM_PUSHBUTTON = 0x2B;
    private const int ROLE_SYSTEM_LISTITEM = 0x22;
    private const int ROLE_SYSTEM_BUTTONMENU = 0x39;
    private const int ROLE_SYSTEM_BUTTONDROPDOWN = 0x38;

    private static readonly BlockingCollection<(int X, int Y, long Ticks, TaskCompletionSource<TaskbarButtonInfo?> Tcs)> _uiaQueue = new();
    private static Thread? _uiaThread;
    private static readonly object _uiaLock = new();

    /// <summary>
    /// Inicia (uma única vez) a thread MTA dedicada ao UI Automation e faz o "warm-up" do UIA,
    /// para que a primeira rolagem sobre um atalho já responda rápido.
    /// </summary>
    private static void EnsureUiaThread()
    {
        lock (_uiaLock)
        {
            if (_uiaThread != null) return;
            _uiaThread = new Thread(() =>
            {
                try { System.Windows.Automation.AutomationElement.FromPoint(new System.Windows.Point(0, 0)); } catch { }

                foreach (var req in _uiaQueue.GetConsumingEnumerable())
                {
                    // Descarta pedidos antigos (o usuário já moveu/rolou de novo)
                    if (Environment.TickCount64 - req.Ticks > 1500)
                    {
                        req.Tcs.TrySetResult(null);
                        continue;
                    }

                    TaskbarButtonInfo? result = null;
                    try { result = QueryTaskbarButtonUia(req.X, req.Y); } catch { }
                    req.Tcs.TrySetResult(result);
                }
            })
            {
                Name = "LuminiTray.TaskbarUia",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _uiaThread.SetApartmentState(ApartmentState.MTA);
            _uiaThread.Start();
        }
    }

    private static TaskbarButtonInfo? QueryTaskbarButtonUiaWithTimeout(int x, int y, int timeoutMs)
    {
        EnsureUiaThread();
        var tcs = new TaskCompletionSource<TaskbarButtonInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiaQueue.Add((x, y, Environment.TickCount64, tcs));
        return tcs.Task.Wait(timeoutMs) ? tcs.Task.Result : null;
    }

    /// <summary>
    /// Hit-test UIA no ponto e sobe na árvore até achar o botão de app da barra de tarefas.
    /// </summary>
    private static TaskbarButtonInfo? QueryTaskbarButtonUia(int x, int y)
    {
        var elem = System.Windows.Automation.AutomationElement.FromPoint(new System.Windows.Point(x, y));
        if (elem == null) return null;

        var walker = System.Windows.Automation.TreeWalker.RawViewWalker;
        TaskbarButtonInfo? weakCandidate = null;
        var cur = elem;

        for (int level = 0; level < 8 && cur != null; level++)
        {
            string cls = "", name = "", aid = "";
            System.Windows.Automation.ControlType? ct = null;
            try
            {
                var c = cur.Current;
                cls = c.ClassName ?? "";
                name = c.Name ?? "";
                aid = c.AutomationId ?? "";
                ct = c.ControlType;
            }
            catch { break; }

            // Para ao chegar na raiz da barra / desktop
            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32769")
                break;

            bool isTaskListButton = cls.Contains("TaskListButton", StringComparison.OrdinalIgnoreCase) ||
                                    cls.Contains("TaskbarButton", StringComparison.OrdinalIgnoreCase);

            bool hasValidName = !string.IsNullOrWhiteSpace(name) && !IsGenericTaskbarName(CleanTaskbarButtonText(name));
            bool hasValidAid = !string.IsNullOrWhiteSpace(aid) && !IsGenericTaskbarName(aid);

            if (hasValidName || hasValidAid)
            {
                if (isTaskListButton)
                {
                    return new TaskbarButtonInfo(name, aid, cls, true);
                }

                bool isButtonLike = ct == System.Windows.Automation.ControlType.Button ||
                                    ct == System.Windows.Automation.ControlType.ListItem ||
                                    ct == System.Windows.Automation.ControlType.MenuItem ||
                                    ct == System.Windows.Automation.ControlType.SplitButton ||
                                    ct == System.Windows.Automation.ControlType.Group ||
                                    ct == System.Windows.Automation.ControlType.Custom;
                if (isButtonLike && weakCandidate == null)
                {
                    weakCandidate = new TaskbarButtonInfo(name, aid, cls, true);
                }
            }

            try { cur = walker.GetParent(cur); } catch { break; }
        }

        return weakCandidate;
    }

    /// <summary>
    /// MSAA (oleacc.dll) síncrono — só aceito quando o objeto sob o cursor é de fato um botão/item
    /// (exatamente como o mod do Windhawk). No Windows 11 a ilha XAML retorna papel "client" e é ignorada.
    /// </summary>
    private static bool TryGetMsaaButtonName(int x, int y, out string name)
    {
        name = "";
        IAccessible? pAcc = null;
        try
        {
            int hr = AccessibleObjectFromPoint(new POINT { x = x, y = y }, out pAcc, out object varChild);
            if (hr != 0 || pAcc == null) return false;

            object child = varChild ?? 0;
            int role = 0;
            try
            {
                object r = pAcc.get_accRole(child);
                if (r is int ri) role = ri;
            }
            catch { }

            if (role != ROLE_SYSTEM_PUSHBUTTON && role != ROLE_SYSTEM_LISTITEM &&
                role != ROLE_SYSTEM_BUTTONMENU && role != ROLE_SYSTEM_BUTTONDROPDOWN)
                return false;

            string? n = null;
            try { n = pAcc.get_accName(child); } catch { }
            if (string.IsNullOrWhiteSpace(n)) return false;
            if (IsGenericTaskbarName(CleanTaskbarButtonText(n))) return false;

            name = n;
            return true;
        }
        catch { return false; }
        finally
        {
            if (pAcc != null)
            {
                try { Marshal.ReleaseComObject(pAcc); } catch { }
            }
        }
    }

    private static bool IsWin10TaskListAt(int x, int y)
    {
        try
        {
            IntPtr h = WindowFromPoint(new POINT { x = x, y = y });
            if (h == IntPtr.Zero) return false;
            var sb = new StringBuilder(64);
            GetClassName(h, sb, 64);
            string cls = sb.ToString();
            return cls is "MSTaskListWClass" or "MSTaskSwWClass" or "ToolbarWindow32";
        }
        catch { return false; }
    }

    /// <summary>
    /// Resolve o app (PID + nome do processo) associado ao atalho sob o cursor.
    /// Ordem: cache espacial → MSAA (somente botões reais, Win10) → UI Automation (Win11 XAML).
    /// NUNCA usa a janela em primeiro plano como palpite (isso fazia o volume errado mudar).
    /// </summary>
    private bool TryResolveTargetAppSafe(int x, int y, out uint targetPid, out string appName)
    {
        targetPid = 0;
        appName = "";

        long now = Environment.TickCount64;

        // 1. Cache espacial (mesmo atalho nos últimos 1.8s)
        if (!string.IsNullOrEmpty(_cachedAppName) &&
            Math.Abs(x - _lastResolvedX) <= 14 &&
            Math.Abs(y - _lastResolvedY) <= 14 &&
            (now - _lastResolvedTimestamp) < 1800)
        {
            targetPid = _cachedAppPid;
            appName = _cachedAppName;
            return true;
        }

        // 2. MSAA — só quando o papel é botão (barra clássica do Windows 10)
        try
        {
            if (TryGetMsaaButtonName(x, y, out string accName))
            {
                bool strong = IsWin10TaskListAt(x, y);
                if (TryMapTaskbarItemToProcess(accName, "", strong, out targetPid, out appName))
                {
                    UpdateResolvedCache(targetPid, appName, x, y);
                    return true;
                }
            }
        }
        catch { }

        // 3. UI Automation (barra XAML do Windows 11) em thread dedicada
        try
        {
            var info = QueryTaskbarButtonUiaWithTimeout(x, y, 1200);
            if (info != null &&
                TryMapTaskbarItemToProcess(info.Name, info.AutomationId, info.IsTaskListButton, out targetPid, out appName))
            {
                UpdateResolvedCache(targetPid, appName, x, y);
                return true;
            }
        }
        catch { }

        targetPid = 0;
        appName = "";
        return false;
    }

    /// <summary>
    /// Extrai um nome de processo a partir do AutomationId do botão (AUMID ou caminho do .exe).
    /// Ex.: "MSEdge" → msedge, "Chrome" → chrome, "C:\...\Discord.exe" → Discord, "com.squirrel.Discord.Discord" → Discord.
    /// </summary>
    public static string ProcessHintFromAutomationId(string automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId)) return "";
        string aid = automationId.Trim();

        int exeIdx = aid.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
        {
            string path = aid.Substring(0, exeIdx + 4);
            int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string file = slash >= 0 ? path.Substring(slash + 1) : path;
            string exe = System.IO.Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(exe)) return exe;
        }

        string alias = MapAppNameToProcessAlias(aid);
        return alias;
    }

    private bool TryMapTaskbarItemToProcess(string rawName, string rawAutoId, bool allowNameOnly, out uint targetPid, out string appName)
    {
        targetPid = 0;
        appName = "";

        string cleaned = CleanTaskbarButtonText(rawName);
        if (IsGenericTaskbarName(cleaned)) cleaned = "";

        string alias = MapAppNameToProcessAlias(cleaned);
        string aidHint = ProcessHintFromAutomationId(rawAutoId);
        if (string.IsNullOrWhiteSpace(alias)) alias = aidHint;

        if (string.IsNullOrWhiteSpace(cleaned) && string.IsNullOrWhiteSpace(alias) && string.IsNullOrWhiteSpace(rawAutoId))
            return false;

        // 1. Sessões de áudio WASAPI ativas
        var sessions = AudioCore.GetActiveAudioSessions();
        if (sessions.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                var m = sessions.FirstOrDefault(s => s.ProcessName.Equals(alias, StringComparison.OrdinalIgnoreCase));
                if (m != null) { targetPid = m.ProcessId; appName = m.ProcessName; return true; }
            }

            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                var m = sessions.FirstOrDefault(s =>
                    !string.IsNullOrEmpty(s.ProcessName) && s.ProcessName.Length >= 3 &&
                    (s.ProcessName.Equals(cleaned, StringComparison.OrdinalIgnoreCase) ||
                     cleaned.Contains(s.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                     s.ProcessName.Contains(cleaned, StringComparison.OrdinalIgnoreCase)));
                if (m != null) { targetPid = m.ProcessId; appName = m.ProcessName; return true; }

                var d = sessions.FirstOrDefault(s =>
                    !string.IsNullOrEmpty(s.DisplayName) && s.DisplayName.Length >= 3 && !s.DisplayName.StartsWith("@") &&
                    (cleaned.Contains(s.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                     s.DisplayName.Contains(cleaned, StringComparison.OrdinalIgnoreCase)));
                if (d != null) { targetPid = d.ProcessId; appName = d.ProcessName; return true; }
            }
        }

        // 2. Janela de nível superior (inclusive minimizada) → PID real (a árvore de processos cobre o áudio)
        if (FindTopLevelWindowByTitle(cleaned, alias, out uint foundPid, out string foundProcess))
        {
            targetPid = foundPid;
            appName = foundProcess;
            return true;
        }

        // 3. Botões de app confirmados na barra mas sem áudio ativo no mixer (ex: Calculadora, Bloco de Notas)
        if (allowNameOnly)
        {
            if (!string.IsNullOrWhiteSpace(cleaned)) { appName = cleaned; return true; }
            if (!string.IsNullOrWhiteSpace(alias)) { appName = alias; return true; }
            if (!string.IsNullOrWhiteSpace(aidHint)) { appName = aidHint; return true; }
        }

        return false;
    }

    public static string CleanTaskbarButtonText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string s = text.Trim();
        var ic = System.Text.RegularExpressions.RegexOptions.IgnoreCase;

        // "Google Chrome - 2 janelas em execução", "Edge – 1 janela aberta", "App, 3 running windows"
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*[-–—,]\s*\d+\s+(janelas?|guias?|abas?|running\s+windows?|windows?|instâncias?)\b.*$", "", ic);
        // "Steam - executando", "App, fixado", "App - pinned"
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*[-–—,]\s*(executando|em\s+execução|fixad[oa]|pinned|running)\b.*$", "", ic);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(\d+\)$", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(em\s*execução\)$", "", ic);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*Free$", "", ic);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*Premium$", "", ic);

        return s.Trim();
    }

    public static string MapAppNameToProcessAlias(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string lower = name.Trim().ToLowerInvariant();

        if (lower.Contains("chrome")) return "chrome";
        if (lower.Contains("edge")) return "msedge";
        if (lower.Contains("firefox")) return "firefox";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("discord")) return "Discord";
        if (lower.Contains("steam")) return "steam";
        if (lower.Contains("telegram")) return "Telegram";
        if (lower.Contains("whatsapp")) return "WhatsApp";
        if (lower.Contains("visual studio code") || lower.Contains("code")) return "Code";
        if (lower.Contains("vlc")) return "vlc";
        if (lower.Contains("opera")) return "opera";
        if (lower.Contains("brave")) return "brave";
        if (lower.Contains("obs studio") || lower.Equals("obs")) return "obs64";
        if (lower.Contains("itunes")) return "iTunes";
        if (lower.Contains("foobar")) return "foobar2000";
        if (lower.Contains("tidal")) return "TIDAL";
        if (lower.Contains("apple music")) return "AppleMusic";
        if (lower.Contains("battle.net")) return "Battle.net";
        if (lower.Contains("epic games")) return "EpicGamesLauncher";
        if (lower.Contains("riot")) return "RiotClientServices";
        if (lower.Contains("league of legends")) return "LeagueClientUx";
        if (lower.Contains("valorant")) return "VALORANT-Win64-Shipping";

        return "";
    }

    public static bool IsGenericTaskbarName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string lower = name.Trim().ToLowerInvariant();

        if (lower.StartsWith("desktopwindowxaml") || lower.StartsWith("windows.ui.") ||
            lower.StartsWith("microsoft.ui.") || lower.StartsWith("mstask"))
            return true;

        return lower is "barra de tarefas" or "taskbar" or "desktopwindowcontentbridge" or
               "desktopwindowxamlsource" or "running applications" or "aplicativos em execução" or
               "aplicativos abertos" or "mostrar ícones ocultos" or "show hidden icons" or
               "mostrar área de trabalho" or "show desktop" or "central de notificações" or
               "notification center" or "configurações rápidas" or "quick settings" or
               "aplicativos fixados" or "pinned apps" or "user promoted notification area" or
               "shell_traywnd" or "iniciar" or "start" or "pesquisar" or "search" or
               "visão de tarefas" or "task view" or "widgets" or "notícias e interesses" or
               "bandeja do sistema" or "system tray" or "área de notificação" or "notification area" or
               "clock" or "relógio" or "rede" or "network" or "volume" or "reunir agora" or "meet now" or
               "copilot" or "chat";
    }

    private static bool FindTopLevelWindowByTitle(string titlePattern, string aliasPattern, out uint foundPid, out string foundProcessName)
    {
        uint bestPid = 0;
        string bestProcessName = "";
        int bestScore = 0;

        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
                if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
                    return true;

                IntPtr owner = GetWindow(hWnd, GW_OWNER);
                if (owner != IntPtr.Zero && (exStyle & WS_EX_APPWINDOW) == 0)
                    return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0 || IsExplorerProcess(pid))
                    return true;

                string pName = AudioCore.GetSafeProcessName(pid);

                // Prioridade máxima: alias direto do processo (ex: "Spotify", "msedge", "chrome", "Discord")
                if (!string.IsNullOrEmpty(aliasPattern) &&
                    pName.Equals(aliasPattern, StringComparison.OrdinalIgnoreCase))
                {
                    bestPid = pid;
                    bestProcessName = pName;
                    bestScore = 10000;
                    return false;
                }

                int score = 0;

                if (!string.IsNullOrEmpty(titlePattern) &&
                    pName.Equals(titlePattern, StringComparison.OrdinalIgnoreCase))
                {
                    score = 5000;
                }

                var sb = new StringBuilder(512);
                int titleLen = GetWindowText(hWnd, sb, 512);
                string wTitle = sb.ToString();

                if (titleLen > 0 && !string.IsNullOrWhiteSpace(titlePattern))
                {
                    if (string.Equals(titlePattern, wTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        score = Math.Max(score, titleLen * 2 + 100);
                    }
                    else if (titlePattern.Contains(wTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        score = Math.Max(score, titleLen + 50);
                    }
                    else if (wTitle.Contains(titlePattern, StringComparison.OrdinalIgnoreCase))
                    {
                        score = Math.Max(score, titlePattern.Length + 50);
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPid = pid;
                    bestProcessName = pName;
                }

                return true;
            }, IntPtr.Zero);
        }
        catch { }

        foundPid = bestPid;
        foundProcessName = bestProcessName;
        return bestPid > 0;
    }

    private void UpdateResolvedCache(uint pid, string name, int x, int y)
    {
        _cachedAppPid = pid;
        _cachedAppName = name;
        _lastResolvedX = x;
        _lastResolvedY = y;
        _lastResolvedTimestamp = Environment.TickCount64;
    }

    /// <summary>
    /// Verificação ultra-rápida e segura se as coordenadas do mouse estão genuinamente sobre a barra de tarefas.
    /// Proteção Crítica para Jogos em Janela Cheia / Tela Cheia / Sem Bordas (ex: Valorant, CS2):
    /// 1. Se um jogo ou app em tela cheia/sem bordas estiver em primeiro plano cobrindo a tela, NUNCA intercepta o scroll.
    /// 2. Além da checagem geométrica de área, verifica se a janela sob o cursor (WindowFromPoint) é de fato a barra de tarefas
    ///    (Shell_TrayWnd ou Shell_SecondaryTrayWnd), evitando que áreas inferiores de jogos sejam confundidas com a barra.
    /// </summary>
    private bool IsPointOverTaskbarFast(int x, int y)
    {
        // 1. Proteção de Jogos em Tela Cheia / Janela Sem Bordas (Valorant, CS2, etc.)
        if (_settings.DisableTaskbarVolumeInFullscreen && IsForegroundFullscreenOrGame(x, y))
        {
            return false;
        }

        // 2. Verificação geométrica rápida em cache (~5ns)
        long now = Environment.TickCount64;
        if (now - _lastTaskbarRectRefresh > 2500 || _cachedTaskbarRects.Length == 0)
        {
            RefreshTaskbarRectsCache();
        }

        var rects = _cachedTaskbarRects;
        bool inRect = false;
        for (int i = 0; i < rects.Length; i++)
        {
            var r = rects[i];
            if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom)
            {
                inRect = true;
                break;
            }
        }

        if (!inRect)
        {
            return false;
        }

        // 3. Verificação definitiva da janela sob o cursor (WindowFromPoint)
        // Garante que mesmo que o cursor esteja na faixa inferior de uma janela de jogo em modo janela/sem bordas,
        // a mensagem NÃO será interceptada a menos que a janela pertença de fato ao Windows Shell Taskbar.
        try
        {
            IntPtr hWndUnder = WindowFromPoint(new POINT { x = x, y = y });
            if (hWndUnder == IntPtr.Zero) return false;

            IntPtr hRoot = GetAncestor(hWndUnder, GA_ROOT);
            if (hRoot == IntPtr.Zero) hRoot = hWndUnder;

            return IsTaskbarWindow(hRoot);
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr _cachedFgWnd = IntPtr.Zero;
    private static bool _cachedFgIsFullscreen = false;
    private static long _lastFgCheckTick = 0;
    private static RECT _cachedFgRect;

    public static bool IsForegroundFullscreenOrGame(int cursorX = 0, int cursorY = 0)
    {
        long now = Environment.TickCount64;
        if (now - _lastFgCheckTick < 120 && _cachedFgWnd != IntPtr.Zero)
        {
            if (_cachedFgIsFullscreen)
            {
                if (cursorX == 0 && cursorY == 0) return true;
                if (cursorX >= _cachedFgRect.Left && cursorX <= _cachedFgRect.Right &&
                    cursorY >= _cachedFgRect.Top && cursorY <= _cachedFgRect.Bottom)
                {
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        _lastFgCheckTick = now;
        IntPtr fg = GetForegroundWindow();
        _cachedFgWnd = fg;

        if (fg == IntPtr.Zero)
        {
            _cachedFgIsFullscreen = false;
            return false;
        }

        // Se a janela ativa for o Desktop ou a própria barra de tarefas, não é jogo/fullscreen
        if (IsDesktopOrTaskbarWindow(fg))
        {
            _cachedFgIsFullscreen = false;
            return false;
        }

        // Janelas minimizadas ou invisíveis não estão em tela cheia ativa
        if (!IsWindowVisible(fg) || IsIconic(fg))
        {
            _cachedFgIsFullscreen = false;
            return false;
        }

        // Obter dimensões da janela
        if (!GetWindowRect(fg, out RECT rcFg))
        {
            _cachedFgIsFullscreen = false;
            return false;
        }

        _cachedFgRect = rcFg;

        // 1. Verificação por nome do processo (Jogos competitivos conhecidos, incluindo Valorant)
        GetWindowThreadProcessId(fg, out uint fgPid);
        if (fgPid > 0)
        {
            string pName = AudioCore.GetSafeProcessName(fgPid);
            if (IsKnownGameProcess(pName))
            {
                _cachedFgIsFullscreen = true;
                return true;
            }
        }

        // 2. Verificação de cobertura total do monitor (Modo Tela Cheia ou Janela Cheia Sem Bordas / Borderless)
        IntPtr hMon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        if (hMon != IntPtr.Zero)
        {
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(hMon, ref mi))
            {
                // Janelas em tela cheia ou borderless cobrem todo o monitor (rcMonitor),
                // diferentemente de janelas normais maximizadas que respeitam rcWork.
                bool coversMonitor = rcFg.Left <= (mi.rcMonitor.Left + 2) &&
                                     rcFg.Top <= (mi.rcMonitor.Top + 2) &&
                                     rcFg.Right >= (mi.rcMonitor.Right - 2) &&
                                     rcFg.Bottom >= (mi.rcMonitor.Bottom - 2);

                if (coversMonitor)
                {
                    _cachedFgIsFullscreen = true;
                    return true;
                }
            }
        }

        _cachedFgIsFullscreen = false;
        return false;
    }

    private static bool IsDesktopOrTaskbarWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        var sb = new StringBuilder(64);
        GetClassName(hWnd, sb, 64);
        string cls = sb.ToString();

        return cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsKnownGameProcess(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;

        return processName.Contains("VALORANT", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("cs2", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("csgo", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("Overwatch", StringComparison.OrdinalIgnoreCase) ||
               processName.StartsWith("Fortnite", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("r5apex", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("TslGame", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("RainbowSix", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("R6Valkyrie", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("GenshinImpact", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("GTA5", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("RocketLeague", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("Warhammer", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("Dota", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("Apex", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTaskbarClassName(string cls)
    {
        if (string.IsNullOrEmpty(cls)) return false;

        return cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("TaskListThumbnailWnd", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("tooltips_class32", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTaskbarWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        var sb = new StringBuilder(64);
        GetClassName(hWnd, sb, 64);
        return IsTaskbarClassName(sb.ToString());
    }

    private static void RefreshTaskbarRectsCache()
    {
        _lastTaskbarRectRefresh = Environment.TickCount64;
        var list = new List<RECT>(8);

        // 1. Coleta retângulos reais de janelas Win32
        try
        {
            IntPtr primary = FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero && GetWindowRect(primary, out RECT rcPrimary) && (rcPrimary.Right > rcPrimary.Left && rcPrimary.Bottom > rcPrimary.Top))
            {
                list.Add(rcPrimary);
            }

            IntPtr secondary = IntPtr.Zero;
            while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            {
                if (GetWindowRect(secondary, out RECT rcSec) && (rcSec.Right > rcSec.Left && rcSec.Bottom > rcSec.Top))
                {
                    list.Add(rcSec);
                }
            }
        }
        catch { }

        // 2. Coleta faixas de barra de tarefas de todos os monitores conectados (Screen.AllScreens)
        // Garante suporte total a monitores secundários em qualquer posição (topo, fundo, laterais, coordenadas negativas)
        try
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var bounds = screen.Bounds;
                var working = screen.WorkingArea;

                // Barra na parte inferior
                if (working.Bottom < bounds.Bottom)
                {
                    list.Add(new RECT { Left = bounds.Left, Top = working.Bottom, Right = bounds.Right, Bottom = bounds.Bottom });
                }
                // Barra na parte superior
                else if (working.Top > bounds.Top)
                {
                    list.Add(new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = working.Top });
                }
                // Barra na lateral esquerda
                else if (working.Left > bounds.Left)
                {
                    list.Add(new RECT { Left = bounds.Left, Top = bounds.Top, Right = working.Left, Bottom = bounds.Bottom });
                }
                // Barra na lateral direita
                else if (working.Right < bounds.Right)
                {
                    list.Add(new RECT { Left = working.Right, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom });
                }
            }
        }
        catch { }

        _cachedTaskbarRects = list.ToArray();
    }

    private static bool IsExplorerProcess(uint pid)
    {
        try
        {
            string name = AudioCore.GetSafeProcessName(pid);
            return name.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsModifierActive(string modKey)
    {
        return modKey switch
        {
            "Ctrl Esquerdo" or "Left Ctrl" => (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
            "Ctrl Direito" or "Right Ctrl" => (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
            "Ctrl" or "Control" => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0,
            "Shift" => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
            "Alt" => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0,
            "Win" => ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) || ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0),
            "Nenhum" => true,
            _ => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0
        };
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { StopHook(); } catch { }
        try { _workSignal.Dispose(); } catch { }
    }
}
