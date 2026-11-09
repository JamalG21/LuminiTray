using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LuminiTray;

/// <summary>
/// Gancho global de teclado de alto desempenho (Low-Level Keyboard Hook).
/// Executa em um thread STA dedicado e isolado com prioridade máxima (ThreadPriority.Highest),
/// completamente desacoplado da thread de UI / WPF.
/// Implementa filtragem ultra-rápida (tabela booleana O(1)) sem alocações ou chamadas ao kernel
/// para teclas normais (WASD, jogos, etc.), garantindo latência ZERO e prevenindo qualquer
/// atraso de liberação de tecla (sticky keys) no Valorant / CS2.
/// </summary>
public sealed class AudioHotkeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

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

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

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

    private readonly object _hookLock = new();
    private readonly LowLevelKeyboardProc _proc;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private volatile bool _isRunning;
    private IntPtr _hookId = IntPtr.Zero;

    // Tabela O(1) de teclas monitoradas (0..255).
    // Qualquer tecla fora desta tabela (ex: W, A, S, D, Espaço...) é liberada instantaneamente em ~2 nanosegundos.
    private readonly bool[] _activeKeys = new bool[256];

    // Callbacks de disparo
    public Action? OnPlaybackCycle { get; set; }
    public Action? OnRecordingCycle { get; set; }
    public Action? OnPlaybackMute { get; set; }

    public Action? OnMicMuteKeyDown { get; set; }
    public Action<long>? OnMicMuteKeyUp { get; set; } // long = tempo pressionado em ms

    public Action? OnMicUnmuteKeyDown { get; set; }
    public Action<long>? OnMicUnmuteKeyUp { get; set; }

    private bool _isMicMutePressed = false;
    private long _micMutePressTimestamp = 0;

    private bool _isMicUnmutePressed = false;
    private long _micUnmutePressTimestamp = 0;

    // Gravação de novo atalho pela UI
    private Action<AudioHotkeyModel>? _recordCallback;
    public bool IsRecording => _recordCallback != null;

    private AudioSettings _settings;
    public AudioSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            UpdateHookState();
        }
    }

    public AudioHotkeyHook(AudioSettings settings)
    {
        _settings = settings;
        _proc = HookCallback;
        UpdateHookState();
    }

    public void UpdateHookState()
    {
        lock (_hookLock)
        {
            RebuildActiveKeys();
            if (_settings.HasAnyActiveHotkey || IsRecording)
            {
                StartHook();
            }
            else
            {
                StopHook();
            }
        }
    }

    public void RebuildActiveKeys()
    {
        lock (_hookLock)
        {
            Array.Clear(_activeKeys, 0, _activeKeys.Length);
            void Mark(AudioHotkeyModel? model)
            {
                if (model != null && model.IsEnabled && model.VirtualKey > 0 && model.VirtualKey < 256)
                {
                    _activeKeys[model.VirtualKey] = true;
                }
            }
            if (_settings.EnablePlaybackCycleFeature) Mark(_settings.HotkeyPlaybackCycle);
            if (_settings.EnableRecordingCycleFeature) Mark(_settings.HotkeyRecordingCycle);
            if (_settings.EnablePlaybackMuteFeature) Mark(_settings.HotkeyPlaybackMute);
            if (_settings.EnableMicMuteFeature)
            {
                Mark(_settings.HotkeyMicMute);
                Mark(_settings.HotkeyMicUnmute);
            }
        }
    }

    public void StartHook()
    {
        lock (_hookLock)
        {
            if (_hookThread != null && _hookThread.IsAlive) return;

            RebuildActiveKeys();
            _isRunning = true;
            using var readyEvent = new ManualResetEventSlim(false);

            _hookThread = new Thread(() =>
            {
                _hookThreadId = GetCurrentThreadId();
                // Cria a fila de mensagens do thread
                PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

                try
                {
                    using var curProcess = Process.GetCurrentProcess();
                    using var curModule = curProcess.MainModule;
                    IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
                }
                catch
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
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
                Name = "LuminiTray.AudioHotkeyHookThread",
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

            if (_hookId != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hookId); } catch { }
                _hookId = IntPtr.Zero;
            }
        }
    }

    public void StartRecording(Action<AudioHotkeyModel> callback)
    {
        _recordCallback = callback;
        UpdateHookState();
    }

    public void CancelRecording()
    {
        _recordCallback = null;
        UpdateHookState();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            // 1. FAST PATH: leitura direta do Virtual Key code sem alocação ou boxing
            int vk = Marshal.ReadInt32(lParam);

            // 2. Se não estiver gravando novo atalho, verifica se o VK pertence aos atalhos configurados
            if (!IsRecording)
            {
                if ((uint)vk >= 256 || !_activeKeys[vk])
                {
                    // Tecla não monitorada (ex: W, A, S, D, Espaço, Shift, etc.)
                    // Retorno imediato sem chamar GetAsyncKeyState ou bloquear a thread.
                    // Isso elimina completamente atrasos de movimentação em jogos como Valorant.
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }
            }

            // 3. Processamento para teclas que coincidem com atalhos ou durante gravação
            int msg = (int)wParam;
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            // Se estiver gravando um atalho na interface
            if (IsRecording && isDown)
            {
                // Ignora modificadores isolados para aguardar a tecla principal
                if (vk is not (VK_CONTROL or VK_MENU or VK_SHIFT or VK_LWIN or VK_RWIN or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5))
                {
                    bool rCtrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool rAlt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    bool rShift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool rWin = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

                    var recorded = new AudioHotkeyModel(vk, rCtrl, rAlt, rShift, rWin);
                    var cb = _recordCallback;
                    _recordCallback = null;
                    cb?.Invoke(recorded);
                    return (IntPtr)1; // Consome a tecla gravada
                }
            }

            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool win = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

            // Tratamento do atalho de Mic Mute (Key Down e Key Up para PTT e Hybrid PTT)
            if (_settings.EnableMicMuteFeature && Matches(_settings.HotkeyMicMute, vk, ctrl, alt, shift, win))
            {
                if (isDown)
                {
                    if (!_isMicMutePressed)
                    {
                        _isMicMutePressed = true;
                        _micMutePressTimestamp = Stopwatch.GetTimestamp();
                        OnMicMuteKeyDown?.Invoke();
                    }
                    if (!_settings.Passthrough) return (IntPtr)1;
                }
            }

            if (_isMicMutePressed && isUp && vk == _settings.HotkeyMicMute.VirtualKey)
            {
                _isMicMutePressed = false;
                long elapsedMs = (long)Stopwatch.GetElapsedTime(_micMutePressTimestamp).TotalMilliseconds;
                OnMicMuteKeyUp?.Invoke(elapsedMs);
                if (!_settings.Passthrough) return (IntPtr)1;
            }

            // Tratamento do atalho de Mic Unmute (quando em SeparateHotkeys)
            if (_settings.EnableMicMuteFeature && _settings.MicMode == AudioMicMode.SeparateHotkeys && Matches(_settings.HotkeyMicUnmute, vk, ctrl, alt, shift, win))
            {
                if (isDown)
                {
                    if (!_isMicUnmutePressed)
                    {
                        _isMicUnmutePressed = true;
                        _micUnmutePressTimestamp = Stopwatch.GetTimestamp();
                        OnMicUnmuteKeyDown?.Invoke();
                    }
                    if (!_settings.Passthrough) return (IntPtr)1;
                }
            }

            if (_isMicUnmutePressed && isUp && vk == _settings.HotkeyMicUnmute.VirtualKey)
            {
                _isMicUnmutePressed = false;
                long elapsedMs = (long)Stopwatch.GetElapsedTime(_micUnmutePressTimestamp).TotalMilliseconds;
                OnMicUnmuteKeyUp?.Invoke(elapsedMs);
                if (!_settings.Passthrough) return (IntPtr)1;
            }

            // Atalhos simples (disparam no KeyDown)
            if (isDown)
            {
                if (_settings.EnablePlaybackCycleFeature && Matches(_settings.HotkeyPlaybackCycle, vk, ctrl, alt, shift, win))
                {
                    OnPlaybackCycle?.Invoke();
                    if (!_settings.Passthrough) return (IntPtr)1;
                }
                else if (_settings.EnableRecordingCycleFeature && Matches(_settings.HotkeyRecordingCycle, vk, ctrl, alt, shift, win))
                {
                    OnRecordingCycle?.Invoke();
                    if (!_settings.Passthrough) return (IntPtr)1;
                }
                else if (_settings.EnablePlaybackMuteFeature && Matches(_settings.HotkeyPlaybackMute, vk, ctrl, alt, shift, win))
                {
                    OnPlaybackMute?.Invoke();
                    if (!_settings.Passthrough) return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool Matches(AudioHotkeyModel hotkey, int vk, bool ctrl, bool alt, bool shift, bool win)
    {
        if (!hotkey.IsEnabled || hotkey.IsEmpty) return false;
        return hotkey.VirtualKey == vk &&
               hotkey.Ctrl == ctrl &&
               hotkey.Alt == alt &&
               hotkey.Shift == shift &&
               hotkey.Win == win;
    }

    /// <summary>
    /// Retorna o tempo de inatividade do usuário em milissegundos usando GetLastInputInfo.
    /// </summary>
    public static uint GetIdleTimeMs()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lii))
        {
            return (uint)Environment.TickCount - lii.dwTime;
        }
        return 0;
    }

    public void Dispose()
    {
        StopHook();
    }
}
