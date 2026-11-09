using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace LuminiTray;

/// <summary>
/// Serviço de alto desempenho para papéis de parede animados baseado no motor nativo MPV.
/// Suporta execução por monitor específico, duplicação em todas as telas ou modo estendido (Span),
/// controle por canal IPC Named Pipe persistente, detecção de tela cheia/jogos por monitor (Modo Gamer),
/// expurgo inteligente de memória RAM quando pausado, agrupamento no Gerenciador de Tarefas via Job Object e recuperação graciosa.
/// </summary>
public sealed class LivelyWallpaperService : IDisposable
{
    private static readonly Lazy<LivelyWallpaperService> _instance = new(() => new LivelyWallpaperService());
    public static LivelyWallpaperService Instance => _instance.Value;

    internal struct ScreenArea
    {
        public int Left;
        public int Top;
        public int Width;
        public int Height;
    }

    private sealed class WallpaperInstance : IDisposable
    {
        public LivelyWallpaperForm Form { get; }
        public Process Process { get; }
        public string PipeName { get; }
        public ScreenArea Screen { get; }
        public string MonitorDeviceId { get; }
        public string? CurrentVideoPath { get; set; }
        public bool IsAutoPaused { get; private set; }

        private NamedPipeClientStream? _pipeClient;
        private StreamWriter? _pipeWriter;
        private readonly object _lock = new();
        private long _lastTrimTick = 0;

        public WallpaperInstance(LivelyWallpaperForm form, Process process, string pipeName, ScreenArea screen, string monitorDeviceId, string? currentVideoPath, bool startPaused = false)
        {
            Form = form;
            Process = process;
            PipeName = pipeName;
            Screen = screen;
            MonitorDeviceId = monitorDeviceId;
            CurrentVideoPath = currentVideoPath;
            IsAutoPaused = startPaused;

            if (startPaused)
            {
                ScheduleMemoryTrim(600);
            }
        }

        public void Pause()
        {
            if (!IsAutoPaused)
            {
                if (SendCommand("{\"command\": [\"set_property\", \"pause\", true]}\n"))
                {
                    IsAutoPaused = true;
                    SendCommand("{\"command\": [\"drop-buffers\"]}\n");
                    ScheduleMemoryTrim(180);
                }
            }
            else
            {
                // Mantém o Working Set enxuto enquanto permanecer em pausa (a cada 3 segundos)
                long now = Environment.TickCount64;
                if (now - _lastTrimTick >= 3000)
                {
                    TrimProcessWorkingSet();
                }
            }
        }

        public void Resume()
        {
            if (IsAutoPaused)
            {
                if (SendCommand("{\"command\": [\"set_property\", \"pause\", false]}\n"))
                {
                    IsAutoPaused = false;
                }
            }
        }

        public void ScheduleMemoryTrim(int delayMs = 180)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    if (IsAutoPaused)
                    {
                        SendCommand("{\"command\": [\"drop-buffers\"]}\n");
                        await Task.Delay(80).ConfigureAwait(false);
                    }
                    TrimProcessWorkingSet();
                }
                catch { }
            });
        }

        public void TrimProcessWorkingSet()
        {
            _lastTrimTick = Environment.TickCount64;
            try
            {
                if (Process != null && !Process.HasExited)
                {
                    IntPtr handle = Process.Handle;
                    if (handle != IntPtr.Zero)
                    {
                        try { EmptyWorkingSet(handle); } catch { }
                        try { SetProcessWorkingSetSize(handle, new IntPtr(-1), new IntPtr(-1)); } catch { }
                    }

                    // Limpa também todos os subprocessos filhos (ex: mpv.exe sob o wrapper LuminiTray.Wallpaper.exe)
                    foreach (int childPid in ProcessTree.GetDescendantPids(Process.Id))
                    {
                        try
                        {
                            using var childProc = Process.GetProcessById(childPid);
                            if (!childProc.HasExited && childProc.Handle != IntPtr.Zero)
                            {
                                EmptyWorkingSet(childProc.Handle);
                                SetProcessWorkingSetSize(childProc.Handle, new IntPtr(-1), new IntPtr(-1));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public bool SendCommand(string json)
        {
            lock (_lock)
            {
                try
                {
                    if (_pipeWriter == null || _pipeClient == null || !_pipeClient.IsConnected)
                    {
                        try { _pipeWriter?.Dispose(); } catch { }
                        try { _pipeClient?.Dispose(); } catch { }

                        _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                        _pipeClient.Connect(600);
                        _pipeWriter = new StreamWriter(_pipeClient, new UTF8Encoding(false)) { AutoFlush = true };
                    }

                    _pipeWriter.WriteLine(json);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WallpaperInstance] Erro ao enviar comando ({PipeName}): {ex.Message}");
                    try { _pipeWriter?.Dispose(); } catch { }
                    _pipeWriter = null;
                    try { _pipeClient?.Dispose(); } catch { }
                    _pipeClient = null;
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    if (_pipeWriter != null && _pipeClient != null && _pipeClient.IsConnected)
                    {
                        _pipeWriter.WriteLine("{\"command\": [\"quit\"]}");
                    }
                }
                catch { }

                try { _pipeWriter?.Dispose(); } catch { }
                try { _pipeClient?.Dispose(); } catch { }
                _pipeWriter = null;
                _pipeClient = null;

                try
                {
                    if (!Process.HasExited)
                    {
                        if (!Process.WaitForExit(120))
                        {
                            Process.Kill();
                        }
                    }
                }
                catch { }

                try { Process.Dispose(); } catch { }

                try
                {
                    if (Form.IsHandleCreated && Form.Handle != IntPtr.Zero)
                    {
                        LivelyDesktopCore.Instance.DetachFromDesktop(Form.Handle);
                    }
                }
                catch { }

                try
                {
                    Form.Close();
                    Form.Dispose();
                }
                catch { }
            }
        }
    }

    private readonly List<WallpaperInstance> _instances = new();
    private readonly object _instancesLock = new();
    private DispatcherTimer? _rotationTimer;
    private System.Threading.Timer? _gameDetectionTimer;

    private AppSettings? _settings;
    private SystemTrayManager? _trayManager;

    private bool _isEnabled = false;
    private bool _isManualPaused = false;
    private bool _isGameAutoPaused = false;
    private int _playlistIndex = 0;

    public bool IsEnabled => _isEnabled && _instances.Count > 0;
    public bool IsPaused => _isManualPaused || _isGameAutoPaused;
    public string? CurrentPath { get; set; }
    public List<string> Playlist { get; } = new();
    public int IntervalMinutes { get; private set; } = 0;
    public int Volume { get; private set; } = 0;
    public bool AutoPauseGames { get; set; } = true;
    public bool PauseOnFocusedApp { get; set; } = false;

    public event Action<string>? MediaErrorOccurred;
    public event Action<string>? WallpaperChanged;

    #region Win32 Helpers for Game / Fullscreen Detection & Memory Trimming

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    private const int DWMWA_CLOAKED = 14;

    private enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    private const uint GA_PARENT = 1;
    private const uint GA_ROOT = 2;
    private const uint GA_ROOTOWNER = 3;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_CHILD = 0x40000000L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_TRANSPARENT = 0x00000020L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    private static long GetWindowLongVal(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex).ToInt64() : GetWindowLong32(hWnd, nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static readonly HashSet<string> DesktopClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkerW",
        "SHELLDLL_DefView",
        "SysListView32",
        "WindowsDashboard",
        "RainmeterMeterWindow",
        "_cls_desk_",
        "LuminiTray_SubtaskHost",
        "Progman",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow"
    };

    #endregion

    private LivelyWallpaperService() { }

    /// <summary>
    /// Localiza o binário do MPV (priorizando LuminiTray.Wallpaper.exe para agrupamento perfeito no Gerenciador).
    /// </summary>
    public static string? GetMpvExecutablePath()
    {
        string? provisioned = WallpaperPlayerProvisioner.GetOrProvisionPlayer();
        if (!string.IsNullOrEmpty(provisioned) && File.Exists(provisioned))
        {
            return provisioned;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string[] candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "Plugins", "mpv", "LuminiTray.Wallpaper.exe"),
            Path.Combine(baseDir, "plugins", "mpv", "LuminiTray.Wallpaper.exe"),
            Path.Combine(baseDir, "LuminiTray.Wallpaper.exe"),
            Path.Combine(appData, "LuminiTray", "plugins", "mpv", "LuminiTray.Wallpaper.exe"),
            Path.Combine(baseDir, "Assets", "Plugins", "mpv", "mpv.exe"),
            Path.Combine(baseDir, "plugins", "mpv", "mpv.exe"),
            Path.Combine(baseDir, "mpv.exe"),
            Path.Combine(appData, "LuminiTray", "plugins", "mpv", "mpv.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    public void Initialize(AppSettings settings, SystemTrayManager? trayManager)
    {
        _settings = settings;
        _trayManager = trayManager;

        // Migração de configuração: assegura que o Modo Gamer venha ATIVADO por padrão (versão >= 2)
        if (_settings.LivelyWallpaperSettingsVersion < 2)
        {
            _settings.LivelyWallpaperAutoPauseGames = true;
            _settings.LivelyWallpaperPauseOnFocusedApp = false;
            _settings.LivelyWallpaperSettingsVersion = 2;
            try { _settings.Save(); } catch { }
        }

        AutoPauseGames = _settings.LivelyWallpaperAutoPauseGames;
        PauseOnFocusedApp = _settings.LivelyWallpaperPauseOnFocusedApp;
        Volume = _settings.LivelyWallpaperVolume;
        IntervalMinutes = _settings.LivelyWallpaperIntervalMinutes;

        // Restaura playlist completa salva pelo usuário
        if (_settings.LivelyWallpaperPlaylist != null && _settings.LivelyWallpaperPlaylist.Count > 0)
        {
            foreach (var p in _settings.LivelyWallpaperPlaylist)
            {
                if (File.Exists(p) && !Playlist.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    Playlist.Add(p);
                }
            }
        }

        // Escaneia automaticamente a pasta de wallpapers configurada para incorporar todos os vídeos novos
        string folder = _settings.WallpaperFolder ?? WallpaperManager.DefaultLibraryFolder;
        if (Directory.Exists(folder))
        {
            try
            {
                var videoFiles = Directory.GetFiles(folder)
                    .Where(f => WallpaperManager.VideoExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);

                foreach (var vf in videoFiles)
                {
                    if (!Playlist.Contains(vf, StringComparer.OrdinalIgnoreCase))
                    {
                        Playlist.Add(vf);
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(_settings.LivelyWallpaperCurrentPath) && File.Exists(_settings.LivelyWallpaperCurrentPath))
        {
            CurrentPath = _settings.LivelyWallpaperCurrentPath;
            if (!Playlist.Contains(CurrentPath, StringComparer.OrdinalIgnoreCase))
            {
                Playlist.Add(CurrentPath);
            }
        }

        // Conecta ações com o menu da bandeja do sistema
        _trayManager?.InitializeWallpaperControls(
            onTogglePlayPause: TogglePlayPause,
            onNext: NextWallpaper,
            onPrev: PreviousWallpaper,
            onSetInterval: SetInterval,
            onDeactivate: () => Disable(saveSettings: true)
        );

        // Se o usuário deixou o papel de parede ativo anteriormente, restaura automaticamente
        if (_settings.LivelyWallpaperEnabled && !string.IsNullOrEmpty(CurrentPath) && File.Exists(CurrentPath))
        {
            Enable();
        }
    }

    /// <summary>
    /// Ativa o motor nativo MPV acoplado ao Desktop nas telas configuradas.
    /// </summary>
    public void Enable()
    {
        lock (_instancesLock)
        {
            foreach (var inst in _instances)
            {
                try { inst.Dispose(); } catch { }
            }
            _instances.Clear();
        }

        string? mpvPath = GetMpvExecutablePath();
        if (string.IsNullOrEmpty(mpvPath))
        {
            MediaErrorOccurred?.Invoke("O executável do MPV Player (LuminiTray.Wallpaper.exe) não foi encontrado.");
            return;
        }

        try
        {
            _isEnabled = true;
            _isManualPaused = false;
            _isGameAutoPaused = false;

            if (_settings != null)
            {
                _settings.LivelyWallpaperEnabled = true;
                _settings.Save();
            }

            var monitors = WallpaperManager.GetConnectedMonitors();
            if (monitors.Count == 0) return;

            // Mapeia quais vídeos devem ser executados em quais monitores
            var monitorToVideo = new Dictionary<WallpaperMonitorInfo, string>();

            if (_settings != null && _settings.LivelyWallpaperMonitorPaths.Count > 0)
            {
                foreach (var mon in monitors)
                {
                    if (_settings.LivelyWallpaperMonitorPaths.TryGetValue(mon.DeviceId, out var vPath) && File.Exists(vPath))
                    {
                        monitorToVideo[mon] = vPath;
                    }
                }
            }

            // Fallback: se nenhum monitor tiver mapeamento individual, utiliza CurrentPath
            if (monitorToVideo.Count == 0)
            {
                string? fallbackVideo = !string.IsNullOrEmpty(CurrentPath) && File.Exists(CurrentPath)
                    ? CurrentPath
                    : (Playlist.Count > 0 && File.Exists(Playlist[0]) ? Playlist[0] : null);

                if (fallbackVideo != null)
                {
                    CurrentPath = fallbackVideo;
                    string selected = _settings?.SelectedWallpaperMonitorId ?? "ALL";
                    if (!string.Equals(selected, "ALL", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetMon = monitors.FirstOrDefault(m => string.Equals(m.DeviceId, selected, StringComparison.OrdinalIgnoreCase)) ?? monitors[0];
                        monitorToVideo[targetMon] = fallbackVideo;
                        if (_settings != null) _settings.LivelyWallpaperMonitorPaths[targetMon.DeviceId] = fallbackVideo;
                    }
                    else
                    {
                        foreach (var m in monitors)
                        {
                            monitorToVideo[m] = fallbackVideo;
                            if (_settings != null) _settings.LivelyWallpaperMonitorPaths[m.DeviceId] = fallbackVideo;
                        }
                    }
                    _settings?.Save();
                }
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                lock (_instancesLock)
                {
                    foreach (var kvp in monitorToVideo)
                    {
                        StartInstanceForMonitor(kvp.Key, kvp.Value, mpvPath);
                    }
                    _isGameAutoPaused = _instances.Count > 0 && _instances.All(i => i.IsAutoPaused);
                }
            });

            // Inicia o timer de detecção de tela cheia/jogos (Modo Gamer) - primeira verificação em 250ms, depois a cada 500ms
            _gameDetectionTimer?.Dispose();
            _gameDetectionTimer = new System.Threading.Timer(GameDetectionCheck, null, 250, 500);

            // Inicia o timer de rotação periódica
            UpdateRotationTimer();

            // Atualiza a bandeja com o estado real (ativo ou já pausado)
            UpdateTray(IsPaused);

            SubtaskManager.RegisterSubtask("wallpaper", "Lumini Tray - Papel de Parede Animado", () => Disable(saveSettings: false));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LivelyWallpaperService] Falha ao ativar: {ex.Message}");
            MediaErrorOccurred?.Invoke($"Falha ao iniciar player MPV: {ex.Message}");
        }
    }

    /// <summary>
    /// Inicia uma instância isolada do MPV Player para um monitor específico sem interferir nos demais.
    /// </summary>
    private void StartInstanceForMonitor(WallpaperMonitorInfo mon, string videoPath, string mpvPath)
    {
        var scr = new ScreenArea { Left = mon.Left, Top = mon.Top, Width = mon.Width, Height = mon.Height };
        var hostForm = new LivelyWallpaperForm();
        IntPtr hwnd = hostForm.Handle;
        hostForm.Show();

        // Posiciona e acopla a janela filha diretamente sob Progman/WorkerW
        LivelyDesktopCore.Instance.AttachToDesktop(hwnd, scr.Left, scr.Top, scr.Width, scr.Height);

        bool initialGlobal = false;
        var initialOccludedRects = AutoPauseGames ? CollectOccludingScreenRects(out initialGlobal) : new List<RECT>();
        bool startPaused = AutoPauseGames && (initialGlobal || IsScreenCoveredByAny(scr, initialOccludedRects));
        string pauseArg = startPaused ? "--pause=yes " : "";

        string pipeName = $"lumini_mpv_{Guid.NewGuid():N}";
        string audioArg = Volume <= 0 ? "--no-audio" : $"--volume={Volume}";
        string videoArg = $"\"{videoPath.Replace('\\', '/')}\"";

        var psi = new ProcessStartInfo
        {
            FileName = mpvPath,
            Arguments = $"--wid={hwnd.ToInt64()} --loop-file=inf --keep-open=yes --media-controls=no --stop-screensaver=no " +
                        $"{pauseArg}{audioArg} --no-sub --no-osc --no-input-default-bindings --no-input-cursor --vf= --af= --profile=fast " +
                        $"--cache=no --demuxer-max-bytes=8MiB --demuxer-max-back-bytes=2MiB --demuxer-readahead-secs=0.5 --vd-queue-enable=no --ad-queue-enable=no " +
                        $"--hwdec=auto-copy --gpu-context=d3d11 --gpu-api=d3d11 --hwdec-extra-frames=2 " +
                        $"--video-latency-hacks=yes --gpu-shader-cache=no --icc-cache=no " +
                        $"--input-ipc-server={pipeName} {videoArg}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(mpvPath)
        };

        var proc = Process.Start(psi);
        if (proc != null)
        {
            JobObjectManager.AssignProcess(proc);
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    if (_isEnabled)
                    {
                        Debug.WriteLine("[LivelyWallpaperService] Subtarefa MPV encerrada manualmente.");
                        Disable(saveSettings: false);
                    }
                });
            };

            var inst = new WallpaperInstance(hostForm, proc, pipeName, scr, mon.DeviceId, videoPath, startPaused);
            _instances.Add(inst);
        }
    }

    /// <summary>
    /// Desativa a reprodução de papéis de parede animados.
    /// </summary>
    /// <param name="saveSettings">Se true (ação do usuário), desativa no AppSettings. Se false (fechamento/queda), preserva a escolha do usuário.</param>
    public void Disable(bool saveSettings = true)
    {
        _isEnabled = false;
        _isManualPaused = false;
        _isGameAutoPaused = false;

        if (saveSettings && _settings != null)
        {
            _settings.LivelyWallpaperEnabled = false;
            _settings.LivelyWallpaperMonitorPaths.Clear();
            _settings.Save();
        }

        _gameDetectionTimer?.Dispose();
        _gameDetectionTimer = null;

        _rotationTimer?.Stop();
        _rotationTimer = null;

        List<WallpaperInstance> toDispose;
        lock (_instancesLock)
        {
            toDispose = new List<WallpaperInstance>(_instances);
            _instances.Clear();
        }

        foreach (var inst in toDispose)
        {
            try { inst.Dispose(); } catch { }
        }

        // Restaura a Área de Trabalho do Windows
        SubtaskManager.UnregisterSubtask("wallpaper");
        LivelyDesktopCore.Instance.RefreshDesktop();

        // Oculta da bandeja do sistema
        if (_trayManager != null)
        {
            _trayManager.ShowWallpaperIcon = false;
            _trayManager.UpdateWallpaper(false, null);
        }

        // Notifica a interface para atualizar status
        try
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                (Application.Current.MainWindow as MainWindow)?.UpdateLivelyWallpaperUi();
            });
        }
        catch { }
    }

    /// <summary>
    /// Encerramento do serviço ao fechar o programa (preserva a configuração do usuário).
    /// </summary>
    public void StopPlayback()
    {
        Disable(saveSettings: false);
    }

    /// <summary>
    /// Alterna entre Play e Pause via canal IPC persistente.
    /// </summary>
    public void TogglePlayPause()
    {
        if (!_isEnabled) return;

        _isManualPaused = !_isManualPaused;
        SendMpvCommand("{\"command\": [\"set_property\", \"pause\", " + (_isManualPaused ? "true" : "false") + "]}\n");

        if (_isManualPaused)
        {
            SendMpvCommand("{\"command\": [\"drop-buffers\"]}\n");
            lock (_instancesLock)
            {
                foreach (var inst in _instances)
                {
                    inst.ScheduleMemoryTrim(180);
                }
            }
        }

        UpdateTray(IsPaused);
        UpdateRotationTimer();
    }

    /// <summary>
    /// Sincroniza a playlist com a pasta de vídeos de papel de parede detectada.
    /// </summary>
    public void SyncPlaylistWithFolder(IEnumerable<string> videoFiles)
    {
        bool changed = false;
        foreach (var vf in videoFiles)
        {
            if (!string.IsNullOrWhiteSpace(vf) && File.Exists(vf) && !Playlist.Contains(vf, StringComparer.OrdinalIgnoreCase))
            {
                Playlist.Add(vf);
                changed = true;
            }
        }

        if (string.IsNullOrEmpty(CurrentPath) && Playlist.Count > 0)
        {
            CurrentPath = Playlist[0];
            changed = true;
        }

        if (changed && _settings != null)
        {
            _settings.LivelyWallpaperPlaylist = new List<string>(Playlist);
            _settings.Save();
        }

        UpdateRotationTimer();
    }

    /// <summary>
    /// Adiciona um vídeo específico à playlist e persiste nas configurações.
    /// </summary>
    public void AddToPlaylist(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        if (!Playlist.Contains(filePath, StringComparer.OrdinalIgnoreCase))
        {
            Playlist.Insert(0, filePath);
            if (_settings != null)
            {
                _settings.LivelyWallpaperPlaylist = new List<string>(Playlist);
                _settings.Save();
            }
            UpdateRotationTimer();
        }
    }

    /// <summary>
    /// Avança para o próximo vídeo da playlist.
    /// </summary>
    public void NextWallpaper()
    {
        if (Playlist.Count == 0) return;
        _playlistIndex = (_playlistIndex + 1) % Playlist.Count;
        PlayFile(Playlist[_playlistIndex]);
        WallpaperChanged?.Invoke(Playlist[_playlistIndex]);
    }

    /// <summary>
    /// Retorna para o vídeo anterior da playlist.
    /// </summary>
    public void PreviousWallpaper()
    {
        if (Playlist.Count == 0) return;
        _playlistIndex = (_playlistIndex - 1 + Playlist.Count) % Playlist.Count;
        PlayFile(Playlist[_playlistIndex]);
        WallpaperChanged?.Invoke(Playlist[_playlistIndex]);
    }

    /// <summary>
    /// Retorna o caminho do vídeo configurado ou em execução para determinado monitor.
    /// </summary>
    public string? GetVideoPathForMonitor(string? monitorDeviceId)
    {
        if (string.IsNullOrEmpty(monitorDeviceId) || string.Equals(monitorDeviceId, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            lock (_instancesLock)
            {
                var first = _instances.FirstOrDefault();
                if (first != null && !string.IsNullOrEmpty(first.CurrentVideoPath)) return first.CurrentVideoPath;
            }
            return CurrentPath;
        }

        lock (_instancesLock)
        {
            var match = _instances.FirstOrDefault(i => string.Equals(i.MonitorDeviceId, monitorDeviceId, StringComparison.OrdinalIgnoreCase));
            if (match != null && !string.IsNullOrEmpty(match.CurrentVideoPath))
            {
                return match.CurrentVideoPath;
            }
        }

        if (_settings != null && _settings.LivelyWallpaperMonitorPaths.TryGetValue(monitorDeviceId, out var p) && File.Exists(p))
        {
            return p;
        }

        return null;
    }

    /// <summary>
    /// Encerra a reprodução de wallpaper animado em um monitor específico.
    /// </summary>
    public void StopMonitor(string? monitorDeviceId)
    {
        if (string.IsNullOrEmpty(monitorDeviceId) || string.Equals(monitorDeviceId, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            Disable(saveSettings: true);
            return;
        }

        lock (_instancesLock)
        {
            var target = _instances.FirstOrDefault(i => string.Equals(i.MonitorDeviceId, monitorDeviceId, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                try { target.Dispose(); } catch { }
                _instances.Remove(target);
            }

            if (_settings != null)
            {
                _settings.LivelyWallpaperMonitorPaths.Remove(monitorDeviceId);
                _settings.Save();
            }

            if (_instances.Count == 0)
            {
                _isEnabled = false;
                if (_settings != null)
                {
                    _settings.LivelyWallpaperEnabled = false;
                    _settings.Save();
                }
                SubtaskManager.UnregisterSubtask("wallpaper");
                UpdateTray(false);
            }
        }
    }

    /// <summary>
    /// Aplica e reproduz um arquivo de vídeo exclusivamente no monitor especificado (ou em todos se ALL).
    /// </summary>
    public void PlayFileOnMonitor(string filePath, string targetDeviceId)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        string? mpvPath = GetMpvExecutablePath();
        if (string.IsNullOrEmpty(mpvPath))
        {
            MediaErrorOccurred?.Invoke("O executável do MPV Player (LuminiTray.Wallpaper.exe) não foi encontrado.");
            return;
        }

        CurrentPath = filePath;
        if (!Playlist.Contains(filePath, StringComparer.OrdinalIgnoreCase))
        {
            Playlist.Add(filePath);
        }
        _playlistIndex = Playlist.FindIndex(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));

        _isEnabled = true;
        if (_settings != null)
        {
            _settings.LivelyWallpaperEnabled = true;
            _settings.LivelyWallpaperCurrentPath = filePath;
        }

        var monitors = WallpaperManager.GetConnectedMonitors();
        var targetMonitors = new List<WallpaperMonitorInfo>();

        if (string.Equals(targetDeviceId, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            targetMonitors.AddRange(monitors);
        }
        else
        {
            var matched = monitors.FirstOrDefault(m => string.Equals(m.DeviceId, targetDeviceId, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                targetMonitors.Add(matched);
            }
            else
            {
                targetMonitors.AddRange(monitors);
            }
        }

        string normalizedPath = filePath.Replace('\\', '/');
        var loadCmd = new { command = new object[] { "loadfile", normalizedPath, "replace" } };
        string json = System.Text.Json.JsonSerializer.Serialize(loadCmd);

        Application.Current?.Dispatcher?.Invoke(() =>
        {
            lock (_instancesLock)
            {
                foreach (var mon in targetMonitors)
                {
                    if (_settings != null)
                    {
                        _settings.LivelyWallpaperMonitorPaths[mon.DeviceId] = filePath;
                    }

                    var existing = _instances.FirstOrDefault(i => string.Equals(i.MonitorDeviceId, mon.DeviceId, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.CurrentVideoPath = filePath;
                        existing.SendCommand(json + "\n");
                        bool shouldStayPaused = _isManualPaused || existing.IsAutoPaused;
                        existing.SendCommand("{\"command\": [\"set_property\", \"pause\", " + (shouldStayPaused ? "true" : "false") + "]}\n");
                        if (shouldStayPaused)
                        {
                            existing.ScheduleMemoryTrim(400);
                        }
                    }
                    else
                    {
                        StartInstanceForMonitor(mon, filePath, mpvPath);
                    }
                }
            }
        });

        _settings?.Save();
        SubtaskManager.RegisterSubtask("wallpaper", "Lumini Tray - Papel de Parede Animado", () => Disable(saveSettings: false));
        UpdateTray(IsPaused);
        UpdateRotationTimer();
    }

    /// <summary>
    /// Executa um arquivo de vídeo no monitor atualmente selecionado (ou em todos caso ALL).
    /// </summary>
    public void PlayFile(string filePath)
    {
        string target = _settings?.SelectedWallpaperMonitorId ?? "ALL";
        PlayFileOnMonitor(filePath, target);
    }

    /// <summary>
    /// Define o volume do vídeo (0 a 100) em tempo real.
    /// </summary>
    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        if (_settings != null)
        {
            _settings.LivelyWallpaperVolume = Volume;
            _settings.Save();
        }
        SendMpvCommand("{\"command\": [\"set_property\", \"volume\", " + Volume + "]}\n");
    }

    /// <summary>
    /// Configura o intervalo de troca automática de vídeos.
    /// </summary>
    public void SetInterval(int minutes)
    {
        IntervalMinutes = Math.Max(0, minutes);
        if (_settings != null)
        {
            _settings.LivelyWallpaperIntervalMinutes = IntervalMinutes;
            _settings.Save();
        }
        UpdateRotationTimer();
    }

    private void UpdateRotationTimer()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _rotationTimer?.Stop();
            _rotationTimer = null;

            if (_isEnabled && IntervalMinutes > 0 && Playlist.Count > 1)
            {
                _rotationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(IntervalMinutes)
                };
                _rotationTimer.Tick += (_, _) =>
                {
                    if (!IsPaused)
                    {
                        NextWallpaper();
                    }
                };
                _rotationTimer.Start();
            }
        });
    }

    /// <summary>
    /// Retorna a soma do WorkingSet (memória física) de todos os processos do player de papel de parede ativos
    /// (incluindo processos filhos como mpv.exe caso executados sob o wrapper).
    /// </summary>
    public long GetTotalProcessesWorkingSet()
    {
        long sum = 0;
        lock (_instancesLock)
        {
            foreach (var inst in _instances)
            {
                try
                {
                    if (inst.Process != null && !inst.Process.HasExited)
                    {
                        inst.Process.Refresh();
                        sum += inst.Process.WorkingSet64;

                        // Inclui processos filhos (ex: mpv.exe executado pelo wrapper)
                        foreach (int childPid in ProcessTree.GetDescendantPids(inst.Process.Id))
                        {
                            try
                            {
                                using var childProc = Process.GetProcessById(childPid);
                                if (!childProc.HasExited)
                                {
                                    childProc.Refresh();
                                    sum += childProc.WorkingSet64;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }
        return sum;
    }

    /// <summary>
    /// Verifica se determinado PID pertence ao ecossistema do player de wallpaper ativo.
    /// </summary>
    public bool ContainsProcess(int pid)
    {
        lock (_instancesLock)
        {
            foreach (var inst in _instances)
            {
                if (inst.Process != null && !inst.Process.HasExited)
                {
                    if (inst.Process.Id == pid) return true;
                    if (ProcessTree.GetDescendantPids(inst.Process.Id).Contains(pid)) return true;
                }
            }
        }
        return false;
    }

    private void SendMpvCommand(string commandJson)
    {
        List<WallpaperInstance> snapshot;
        lock (_instancesLock)
        {
            snapshot = new List<WallpaperInstance>(_instances);
        }

        foreach (var inst in snapshot)
        {
            inst.SendCommand(commandJson);
        }
    }

    /// <summary>
    /// Verifica se uma janela está oculta pelo DWM (ex: apps UWP suspensos ou em outras Áreas de Trabalho Virtuais).
    /// </summary>
    private static bool IsWindowCloaked(IntPtr hWnd)
    {
        try
        {
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
            {
                return cloaked != 0;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Varre todas as janelas visíveis de nível superior em todos os monitores via EnumWindows
    /// e retorna os retângulos dos monitores que estão cobertos por janelas maximizadas ou em tela cheia.
    /// </summary>
    private List<RECT> CollectOccludingScreenRects(out bool globalFullScreen)
    {
        var occludedMonitors = new List<RECT>();
        globalFullScreen = false;

        // 1. Verifica tela cheia exclusiva Direct3D / Modo Apresentação
        try
        {
            if (SHQueryUserNotificationState(out var quns) == 0)
            {
                if (quns == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                    quns == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
                {
                    globalFullScreen = true;
                }
            }
        }
        catch { }

        // Conjunto de handles das nossas janelas de wallpaper para ignorá-las
        var wallpaperHwnds = new HashSet<IntPtr>();
        lock (_instancesLock)
        {
            foreach (var inst in _instances)
            {
                try
                {
                    if (inst.Form != null && inst.Form.IsHandleCreated)
                    {
                        wallpaperHwnds.Add(inst.Form.Handle);
                    }
                }
                catch { }
            }
        }

        IntPtr shellWnd = GetShellWindow();
        IntPtr desktopWnd = GetDesktopWindow();
        uint currentPid = (uint)Environment.ProcessId;

        var sb = new StringBuilder(256);

        EnumWindows((hWnd, _) =>
        {
            try
            {
                if (hWnd == IntPtr.Zero || hWnd == shellWnd || hWnd == desktopWnd)
                    return true;

                if (wallpaperHwnds.Contains(hWnd))
                    return true;

                if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                    return true;

                if (IsWindowCloaked(hWnd))
                    return true;

                long style = GetWindowLongVal(hWnd, GWL_STYLE);
                if ((style & WS_CHILD) != 0)
                    return true;

                long exStyle = GetWindowLongVal(hWnd, GWL_EXSTYLE);
                // Ignora overlays transparentes a cliques ou toolwindows flutuantes
                if ((exStyle & WS_EX_TRANSPARENT) != 0 || (exStyle & WS_EX_TOOLWINDOW) != 0 || (exStyle & WS_EX_NOACTIVATE) != 0)
                    return true;

                sb.Clear();
                GetClassName(hWnd, sb, 256);
                string cls = sb.ToString();
                if (string.IsNullOrEmpty(cls) || DesktopClasses.Contains(cls))
                    return true;

                // Se a janela pertencer ao próprio Lumini Tray, só conta como oclusão se a janela principal estiver Maximizada
                GetWindowThreadProcessId(hWnd, out uint wndPid);
                if (wndPid == currentPid && !IsZoomed(hWnd))
                    return true;

                IntPtr hMon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                if (hMon == IntPtr.Zero)
                    return true;

                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(hMon, ref mi))
                    return true;

                bool coversMonitor = false;

                if (IsZoomed(hWnd))
                {
                    coversMonitor = true;
                }
                else if (GetWindowRect(hWnd, out RECT rect))
                {
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;
                    if (w > 200 && h > 200)
                    {
                        // Tela cheia / Borderless cobrindo o monitor inteiro (tolerância de 12px para bordas DWM)
                        bool coversFull = rect.Left <= mi.rcMonitor.Left + 12 &&
                                          rect.Top <= mi.rcMonitor.Top + 12 &&
                                          rect.Right >= mi.rcMonitor.Right - 12 &&
                                          rect.Bottom >= mi.rcMonitor.Bottom - 12;

                        // Ou cobrindo toda a área útil de trabalho do monitor (janela maximizada customizada sem WS_MAXIMIZE, ex: Chrome/Discord/Electron)
                        bool coversWork = rect.Left <= mi.rcWork.Left + 10 &&
                                          rect.Top <= mi.rcWork.Top + 10 &&
                                          rect.Right >= mi.rcWork.Right - 10 &&
                                          rect.Bottom >= mi.rcWork.Bottom - 10;

                        if (coversFull || coversWork)
                        {
                            coversMonitor = true;
                        }
                    }
                }

                if (coversMonitor)
                {
                    occludedMonitors.Add(mi.rcMonitor);
                }
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        return occludedMonitors;
    }

    /// <summary>
    /// Verifica se a área de tela de uma instância de wallpaper está coberta por algum dos monitores ocluídos.
    /// </summary>
    private static bool IsScreenCoveredByAny(ScreenArea scr, List<RECT> occludedMonitors)
    {
        if (occludedMonitors.Count == 0) return false;

        int scrRight = scr.Left + scr.Width;
        int scrBottom = scr.Top + scr.Height;
        long scrArea = Math.Max(1L, (long)scr.Width * scr.Height);

        foreach (var mon in occludedMonitors)
        {
            int interLeft = Math.Max(scr.Left, mon.Left);
            int interTop = Math.Max(scr.Top, mon.Top);
            int interRight = Math.Min(scrRight, mon.Right);
            int interBottom = Math.Min(scrBottom, mon.Bottom);

            if (interRight > interLeft && interBottom > interTop)
            {
                long interArea = (long)(interRight - interLeft) * (interBottom - interTop);
                long monArea = Math.Max(1L, (long)(mon.Right - mon.Left) * (mon.Bottom - mon.Top));

                // Se a interseção cobre >= 50% da instância (Single/Duplicate) ou >= 80% de um monitor inteiro
                if (interArea * 100 / scrArea >= 50 || interArea * 100 / monArea >= 80)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Verifica se há algum aplicativo normal focado (para a opção PauseOnFocusedApp).
    /// </summary>
    private static bool HasNonDesktopForegroundApp()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == GetDesktopWindow() || fg == GetShellWindow())
            return false;

        IntPtr topWnd = GetAncestor(fg, GA_ROOT);
        if (topWnd == IntPtr.Zero) topWnd = fg;

        if (IsIconic(topWnd) || !IsWindowVisible(topWnd))
            return false;

        var sb = new StringBuilder(256);
        GetClassName(topWnd, sb, 256);
        string cls = sb.ToString();

        return !DesktopClasses.Contains(cls);
    }

    /// <summary>
    /// Monitor contínuo de janelas em todos os monitores para pausar vídeos durante jogos e janelas maximizadas/tela cheia.
    /// </summary>
    private void GameDetectionCheck(object? state)
    {
        if (!_isEnabled || _isManualPaused || !AutoPauseGames)
            return;

        try
        {
            var occludedMonitors = CollectOccludingScreenRects(out bool globalFullScreen);
            bool focusPause = PauseOnFocusedApp && HasNonDesktopForegroundApp();

            List<WallpaperInstance> snapshot;
            lock (_instancesLock)
            {
                snapshot = new List<WallpaperInstance>(_instances);
            }

            if (snapshot.Count == 0) return;

            bool allPaused = true;

            foreach (var inst in snapshot)
            {
                bool shouldPauseInst = globalFullScreen || focusPause || IsScreenCoveredByAny(inst.Screen, occludedMonitors);

                if (shouldPauseInst)
                {
                    inst.Pause();
                }
                else
                {
                    inst.Resume();
                    allPaused = false;
                }
            }

            bool prevGameAutoPaused;
            lock (_instancesLock)
            {
                prevGameAutoPaused = _isGameAutoPaused;
                _isGameAutoPaused = allPaused;
            }

            if (prevGameAutoPaused != allPaused)
            {
                Application.Current?.Dispatcher?.BeginInvoke(() => UpdateTray(IsPaused));
            }
        }
        catch { }
    }

    private void PauseAllInstances()
    {
        List<WallpaperInstance> snapshot;
        bool stateChanged = false;

        lock (_instancesLock)
        {
            if (!_isGameAutoPaused)
            {
                _isGameAutoPaused = true;
                stateChanged = true;
            }
            snapshot = new List<WallpaperInstance>(_instances);
        }

        foreach (var inst in snapshot)
        {
            inst.Pause();
        }

        if (stateChanged)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() => UpdateTray(true));
        }
    }

    private void ResumeAllInstances()
    {
        List<WallpaperInstance> snapshot;
        bool stateChanged = false;

        lock (_instancesLock)
        {
            if (_isGameAutoPaused)
            {
                _isGameAutoPaused = false;
                stateChanged = true;
            }
            snapshot = new List<WallpaperInstance>(_instances);
        }

        foreach (var inst in snapshot)
        {
            inst.Resume();
        }

        if (stateChanged)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() => UpdateTray(false));
        }
    }

    private void ResumeIfPaused()
    {
        ResumeAllInstances();
    }

    private void UpdateTray(bool isPaused)
    {
        if (_trayManager == null) return;
        _trayManager.ShowWallpaperIcon = _isEnabled;
        string name = !string.IsNullOrEmpty(CurrentPath) ? Path.GetFileName(CurrentPath) : "Papel de Parede";
        _trayManager.UpdateWallpaper(isPaused, name);
    }

    public void Dispose()
    {
        StopPlayback();
    }
}
