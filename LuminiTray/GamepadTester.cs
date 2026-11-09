using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

public sealed class GamepadStateSnapshot
{
    public bool IsConnected { get; set; }
    public string ControllerName { get; set; } = "Nenhum controle detectado";
    public string DriverType { get; set; } = "SDL2";

    // Sticks: normalizados de -1.0 a +1.0
    public double LeftStickX { get; set; }
    public double LeftStickY { get; set; }
    public double RightStickX { get; set; }
    public double RightStickY { get; set; }

    // Drift / Desvio do centro em repouso (0 a 100%)
    public double LeftStickDriftPercent { get; set; }
    public double RightStickDriftPercent { get; set; }

    // Erro de circularidade em %
    public double LeftCircularityError { get; set; }
    public double RightCircularityError { get; set; }

    // Gatilhos analógicos (0.0 a 1.0)
    public double LeftTrigger { get; set; }
    public double RightTrigger { get; set; }

    // Botões
    public bool BtnA { get; set; }
    public bool BtnB { get; set; }
    public bool BtnX { get; set; }
    public bool BtnY { get; set; }
    public bool BtnLB { get; set; }
    public bool BtnRB { get; set; }
    public bool BtnL3 { get; set; }
    public bool BtnR3 { get; set; }
    public bool BtnStart { get; set; }
    public bool BtnBack { get; set; }
    public bool BtnGuide { get; set; }
    public bool DpadUp { get; set; }
    public bool DpadDown { get; set; }
    public bool DpadLeft { get; set; }
    public bool DpadRight { get; set; }

    // Desempenho
    public int PollingRateHz { get; set; }
    public double PollingLatencyMs { get; set; }
}

public sealed class GamepadTester : IDisposable
{
    #region Win32 / SDL2 Native Interop

    private static class Sdl2
    {
        private const string Dll = "Native/SDL2.dll";

        public const uint SDL_INIT_JOYSTICK = 0x00000200;
        public const uint SDL_INIT_HAPTIC = 0x00001000;
        public const uint SDL_INIT_GAMECONTROLLER = 0x00002000;

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_Init(uint flags);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_Quit();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_NumJoysticks();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_IsGameController(int joystick_index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr SDL_GameControllerOpen(int joystick_index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_GameControllerClose(IntPtr gamecontroller);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr SDL_GameControllerName(IntPtr gamecontroller);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_GameControllerUpdate();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern short SDL_GameControllerGetAxis(IntPtr gamecontroller, int axis);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte SDL_GameControllerGetButton(IntPtr gamecontroller, int button);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_GameControllerRumble(IntPtr gamecontroller, ushort low_freq, ushort high_freq, uint duration_ms);
    }

    #endregion

    #region Win32 / XInput Fallback Interop

    private static class XInput
    {
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState14(int dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState91(int dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        private static extern int XInputSetState14(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
        private static extern int XInputSetState91(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        public static int GetState(int index, out XINPUT_STATE state)
        {
            try { return XInputGetState14(index, out state); }
            catch { }
            try { return XInputGetState91(index, out state); }
            catch { }
            state = default;
            return -1;
        }

        public static int SetState(int index, ref XINPUT_VIBRATION vib)
        {
            try { return XInputSetState14(index, ref vib); }
            catch { }
            try { return XInputSetState91(index, ref vib); }
            catch { }
            return -1;
        }
    }

    #endregion

    private bool _running;
    private Thread? _pollThread;
    private IntPtr _sdlController = IntPtr.Zero;
    private bool _sdlInitialized;

    public GamepadStateSnapshot CurrentState { get; private set; } = new();
    public event Action<GamepadStateSnapshot>? StateUpdated;

    // Rastreamento de circularidade (16 fatias angulares)
    private readonly double[] _leftStickRadialDistances = new double[16];
    private readonly double[] _rightStickRadialDistances = new double[16];
    private bool _isTrackingCircularity;

    public void Start()
    {
        if (_running) return;
        _running = true;

        try
        {
            int init = Sdl2.SDL_Init(Sdl2.SDL_INIT_JOYSTICK | Sdl2.SDL_INIT_GAMECONTROLLER | Sdl2.SDL_INIT_HAPTIC);
            _sdlInitialized = (init == 0);
        }
        catch
        {
            _sdlInitialized = false;
        }

        _pollThread = new Thread(PollLoop)
        {
            Name = "LuminiTray.GamepadPollThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _pollThread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _pollThread?.Join(300); } catch { }
        _pollThread = null;

        if (_sdlController != IntPtr.Zero)
        {
            try { Sdl2.SDL_GameControllerClose(_sdlController); } catch { }
            _sdlController = IntPtr.Zero;
        }

        if (_sdlInitialized)
        {
            try { Sdl2.SDL_Quit(); } catch { }
            _sdlInitialized = false;
        }
    }

    public void ResetCircularityTest()
    {
        Array.Clear(_leftStickRadialDistances);
        Array.Clear(_rightStickRadialDistances);
        _isTrackingCircularity = true;
    }

    public void TestVibration(float lowFreqMotor, float highFreqMotor, int durationMs = 1000)
    {
        ushort lowVal = (ushort)(Math.Clamp(lowFreqMotor, 0f, 1f) * 65535);
        ushort highVal = (ushort)(Math.Clamp(highFreqMotor, 0f, 1f) * 65535);

        if (_sdlController != IntPtr.Zero)
        {
            try
            {
                Sdl2.SDL_GameControllerRumble(_sdlController, lowVal, highVal, (uint)durationMs);
                return;
            }
            catch { }
        }

        // Fallback XInput
        try
        {
            var vib = new XInput.XINPUT_VIBRATION { wLeftMotorSpeed = lowVal, wRightMotorSpeed = highVal };
            XInput.SetState(0, ref vib);

            Task.Run(async () =>
            {
                await Task.Delay(durationMs);
                var stopVib = new XInput.XINPUT_VIBRATION { wLeftMotorSpeed = 0, wRightMotorSpeed = 0 };
                XInput.SetState(0, ref stopVib);
            });
        }
        catch { }
    }

    private void PollLoop()
    {
        int pollCount = 0;
        long lastHzTime = Stopwatch.GetTimestamp();
        int currentHz = 0;
        long lastPollTime = Stopwatch.GetTimestamp();
        long lastUiNotifyMs = 0;

        while (_running)
        {
            long startTicks = Stopwatch.GetTimestamp();
            var snap = new GamepadStateSnapshot();

            // 1. Tentar leitura via SDL2
            bool polled = false;
            if (_sdlInitialized)
            {
                polled = PollSdl2(snap);
            }

            // 2. Se SDL2 não achou controle, usar XInput Fallback
            if (!polled)
            {
                polled = PollXInput(snap);
            }

            // 3. Cálculo de Polling Rate & Latência
            pollCount++;
            long now = Stopwatch.GetTimestamp();
            double elapsedSec = (double)(now - lastHzTime) / Stopwatch.Frequency;
            if (elapsedSec >= 0.5)
            {
                currentHz = (int)Math.Round(pollCount / elapsedSec);
                pollCount = 0;
                lastHzTime = now;
            }

            snap.PollingRateHz = currentHz;
            double loopLatency = (double)(now - lastPollTime) / Stopwatch.Frequency * 1000.0;
            snap.PollingLatencyMs = Math.Round(loopLatency, 1);
            lastPollTime = now;

            CurrentState = snap;

            // Throttle de notificação da UI a ~40Hz (25ms) para poupar Dispatcher do WPF
            long nowMs = Environment.TickCount64;
            if (nowMs - lastUiNotifyMs >= 25)
            {
                lastUiNotifyMs = nowMs;
                StateUpdated?.Invoke(snap);
            }

            // Intervalo suave de polling a ~120Hz (8ms)
            Thread.Sleep(8);
        }
    }

    private bool PollSdl2(GamepadStateSnapshot snap)
    {
        try
        {
            Sdl2.SDL_GameControllerUpdate();

            // Verificar se temos controle aberto válido
            if (_sdlController == IntPtr.Zero)
            {
                int num = Sdl2.SDL_NumJoysticks();
                for (int i = 0; i < num; i++)
                {
                    if (Sdl2.SDL_IsGameController(i) != 0)
                    {
                        _sdlController = Sdl2.SDL_GameControllerOpen(i);
                        break;
                    }
                }
            }

            if (_sdlController == IntPtr.Zero) return false;

            snap.IsConnected = true;
            snap.DriverType = "SDL2 (Nativo v2.32.8)";

            IntPtr namePtr = Sdl2.SDL_GameControllerName(_sdlController);
            snap.ControllerName = namePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(namePtr) ?? "Gamepad SDL2" : "Gamepad SDL2";

            // Analógicos: -32768 a 32767
            short lx = Sdl2.SDL_GameControllerGetAxis(_sdlController, 0); // SDL_CONTROLLER_AXIS_LEFTX
            short ly = Sdl2.SDL_GameControllerGetAxis(_sdlController, 1); // SDL_CONTROLLER_AXIS_LEFTY
            short rx = Sdl2.SDL_GameControllerGetAxis(_sdlController, 2); // SDL_CONTROLLER_AXIS_RIGHTX
            short ry = Sdl2.SDL_GameControllerGetAxis(_sdlController, 3); // SDL_CONTROLLER_AXIS_RIGHTY

            snap.LeftStickX = Math.Round(Math.Clamp((double)lx / 32767.0, -1.0, 1.0), 3);
            snap.LeftStickY = Math.Round(Math.Clamp((double)ly / 32767.0, -1.0, 1.0), 3);
            snap.RightStickX = Math.Round(Math.Clamp((double)rx / 32767.0, -1.0, 1.0), 3);
            snap.RightStickY = Math.Round(Math.Clamp((double)ry / 32767.0, -1.0, 1.0), 3);

            // Gatilhos analógicos: 0 a 32767
            short lt = Sdl2.SDL_GameControllerGetAxis(_sdlController, 4); // SDL_CONTROLLER_AXIS_TRIGGERLEFT
            short rt = Sdl2.SDL_GameControllerGetAxis(_sdlController, 5); // SDL_CONTROLLER_AXIS_TRIGGERRIGHT
            snap.LeftTrigger = Math.Round(Math.Clamp((double)lt / 32767.0, 0.0, 1.0), 2);
            snap.RightTrigger = Math.Round(Math.Clamp((double)rt / 32767.0, 0.0, 1.0), 2);

            // Botões
            snap.BtnA = Sdl2.SDL_GameControllerGetButton(_sdlController, 0) != 0;
            snap.BtnB = Sdl2.SDL_GameControllerGetButton(_sdlController, 1) != 0;
            snap.BtnX = Sdl2.SDL_GameControllerGetButton(_sdlController, 2) != 0;
            snap.BtnY = Sdl2.SDL_GameControllerGetButton(_sdlController, 3) != 0;
            snap.BtnBack = Sdl2.SDL_GameControllerGetButton(_sdlController, 4) != 0;
            snap.BtnGuide = Sdl2.SDL_GameControllerGetButton(_sdlController, 5) != 0;
            snap.BtnStart = Sdl2.SDL_GameControllerGetButton(_sdlController, 6) != 0;
            snap.BtnL3 = Sdl2.SDL_GameControllerGetButton(_sdlController, 7) != 0;
            snap.BtnR3 = Sdl2.SDL_GameControllerGetButton(_sdlController, 8) != 0;
            snap.BtnLB = Sdl2.SDL_GameControllerGetButton(_sdlController, 9) != 0;
            snap.BtnRB = Sdl2.SDL_GameControllerGetButton(_sdlController, 10) != 0;
            snap.DpadUp = Sdl2.SDL_GameControllerGetButton(_sdlController, 11) != 0;
            snap.DpadDown = Sdl2.SDL_GameControllerGetButton(_sdlController, 12) != 0;
            snap.DpadLeft = Sdl2.SDL_GameControllerGetButton(_sdlController, 13) != 0;
            snap.DpadRight = Sdl2.SDL_GameControllerGetButton(_sdlController, 14) != 0;

            ComputeDriftAndCircularity(snap);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool PollXInput(GamepadStateSnapshot snap)
    {
        try
        {
            if (XInput.GetState(0, out var state) != 0) return false;

            snap.IsConnected = true;
            snap.DriverType = "Windows XInput (Compatibilidade)";
            snap.ControllerName = "Xbox / XInput Compatible Controller";

            snap.LeftStickX = Math.Round(Math.Clamp((double)state.Gamepad.sThumbLX / 32767.0, -1.0, 1.0), 3);
            snap.LeftStickY = Math.Round(Math.Clamp(-(double)state.Gamepad.sThumbLY / 32767.0, -1.0, 1.0), 3); // Inverter Y do XInput para padrão cartesiano
            snap.RightStickX = Math.Round(Math.Clamp((double)state.Gamepad.sThumbRX / 32767.0, -1.0, 1.0), 3);
            snap.RightStickY = Math.Round(Math.Clamp(-(double)state.Gamepad.sThumbRY / 32767.0, -1.0, 1.0), 3);

            snap.LeftTrigger = Math.Round((double)state.Gamepad.bLeftTrigger / 255.0, 2);
            snap.RightTrigger = Math.Round((double)state.Gamepad.bRightTrigger / 255.0, 2);

            ushort b = state.Gamepad.wButtons;
            snap.DpadUp = (b & 0x0001) != 0;
            snap.DpadDown = (b & 0x0002) != 0;
            snap.DpadLeft = (b & 0x0004) != 0;
            snap.DpadRight = (b & 0x0008) != 0;
            snap.BtnStart = (b & 0x0010) != 0;
            snap.BtnBack = (b & 0x0020) != 0;
            snap.BtnL3 = (b & 0x0040) != 0;
            snap.BtnR3 = (b & 0x0080) != 0;
            snap.BtnLB = (b & 0x0100) != 0;
            snap.BtnRB = (b & 0x0200) != 0;
            snap.BtnA = (b & 0x1000) != 0;
            snap.BtnB = (b & 0x2000) != 0;
            snap.BtnX = (b & 0x4000) != 0;
            snap.BtnY = (b & 0x8000) != 0;

            ComputeDriftAndCircularity(snap);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ComputeDriftAndCircularity(GamepadStateSnapshot snap)
    {
        // Drift = distância euclidiana da origem (0,0)
        double leftDist = Math.Sqrt(snap.LeftStickX * snap.LeftStickX + snap.LeftStickY * snap.LeftStickY);
        double rightDist = Math.Sqrt(snap.RightStickX * snap.RightStickX + snap.RightStickY * snap.RightStickY);

        snap.LeftStickDriftPercent = Math.Round(leftDist * 100.0, 1);
        snap.RightStickDriftPercent = Math.Round(rightDist * 100.0, 1);

        // Circularidade
        if (_isTrackingCircularity)
        {
            if (leftDist > 0.4)
            {
                double angle = Math.Atan2(snap.LeftStickY, snap.LeftStickX);
                int sector = (int)Math.Floor(((angle + Math.PI) / (2 * Math.PI)) * 16) % 16;
                if (leftDist > _leftStickRadialDistances[sector])
                    _leftStickRadialDistances[sector] = leftDist;
            }

            if (rightDist > 0.4)
            {
                double angle = Math.Atan2(snap.RightStickY, snap.RightStickX);
                int sector = (int)Math.Floor(((angle + Math.PI) / (2 * Math.PI)) * 16) % 16;
                if (rightDist > _rightStickRadialDistances[sector])
                    _rightStickRadialDistances[sector] = rightDist;
            }

            // Média de erro em relação ao círculo perfeito (raio = 1.0)
            double leftErrorSum = 0;
            int leftCount = 0;
            foreach (var r in _leftStickRadialDistances)
            {
                if (r > 0.1) { leftErrorSum += Math.Abs(r - 1.0); leftCount++; }
            }
            if (leftCount > 0) snap.LeftCircularityError = Math.Round((leftErrorSum / leftCount) * 100.0, 1);

            double rightErrorSum = 0;
            int rightCount = 0;
            foreach (var r in _rightStickRadialDistances)
            {
                if (r > 0.1) { rightErrorSum += Math.Abs(r - 1.0); rightCount++; }
            }
            if (rightCount > 0) snap.RightCircularityError = Math.Round((rightErrorSum / rightCount) * 100.0, 1);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
