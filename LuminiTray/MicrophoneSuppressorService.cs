using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

public enum SuppressorMonitorMode
{
    Processed = 0, // Ouvir somente áudio limpo pós-processamento
    Raw = 1        // Ouvir áudio bruto / cru (bypass para teste A/B)
}

public enum SuppressorQuickTestState
{
    Idle,
    Recording,
    Playing
}

/// <summary>
/// Serviço Singleton em segundo plano para captura de microfone via WASAPI,
/// processamento de áudio neural de alta fidelidade (RNNoise / Gate / EQ),
/// reamostragem dinâmica de taxa de amostragem (Zero GC) e roteamento duplo:
/// 1) Microfone Virtual (CABLE Input -> CABLE Output para Discord/Jogos);
/// 2) Retorno no Fone de Ouvido (Pós-Processado vs Original e Teste de Voz).
/// </summary>
public sealed class MicrophoneSuppressorService : IDisposable
{
    private static readonly Lazy<MicrophoneSuppressorService> _instance =
        new(() => new MicrophoneSuppressorService());

    public static MicrophoneSuppressorService Instance => _instance.Value;

    public NoiseSuppressionEngine Engine { get; } = new();

    private Thread? _workerThread;
    private CancellationTokenSource? _cts;
    private readonly object _stateLock = new();

    public bool IsRunning { get; private set; }

    private bool _masterEnabled;
    public bool MasterEnabled
    {
        get => _masterEnabled;
        set
        {
            _masterEnabled = value;
            Engine.Enabled = value;
            if (value && !IsRunning)
            {
                Start();
            }
            else if (!value && !IsMonitoringEnabled && TestState == SuppressorQuickTestState.Idle)
            {
                Stop();
            }
        }
    }

    private bool _isMonitoringEnabled;
    public bool IsMonitoringEnabled
    {
        get => _isMonitoringEnabled;
        set
        {
            _isMonitoringEnabled = value;
            if (value && !IsRunning)
            {
                Start();
            }
            else if (!value && !_masterEnabled && TestState == SuppressorQuickTestState.Idle)
            {
                Stop();
            }
        }
    }

    public bool IsVirtualCableRoutingEnabled { get; set; } = true;
    public SuppressorMonitorMode MonitorMode { get; set; } = SuppressorMonitorMode.Processed;

    public string? SelectedInputDeviceId { get; set; }
    public string? SelectedVirtualCableDeviceId { get; set; }
    public string? SelectedOutputDeviceId { get; set; }

    // Métricas para a UI (Thread-safe leituras voláteis)
    private float _inputPeakDb = -120f;
    private float _outputPeakDb = -120f;
    private float _voiceProbability = 0f;

    public float InputPeakDb => Volatile.Read(ref _inputPeakDb);
    public float OutputPeakDb => Volatile.Read(ref _outputPeakDb);
    public float VoiceProbability => Volatile.Read(ref _voiceProbability);
    public bool IsVoiceDetected => VoiceProbability > 0.40f;

    // Teste Rápido de Voz (Gravação de 5s e Reprodução do Pós-Processado)
    private readonly object _testLock = new();
    private SuppressorQuickTestState _testState = SuppressorQuickTestState.Idle;
    public SuppressorQuickTestState TestState => _testState;
    public float TestProgress { get; private set; } // 0.0f a 1.0f

    private readonly List<float> _testSampleBuffer = new(48000 * 6);
    private int _testPlayPosition = 0;
    private int _testTargetFrames = 48000 * 5; // 5 segundos

    private MicrophoneSuppressorService()
    {
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _workerThread = new Thread(WorkerLoop)
            {
                Name = "LuminiTray_MicrophoneSuppressorWorker",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _workerThread.SetApartmentState(ApartmentState.MTA);
            IsRunning = true;
            _workerThread.Start(_cts.Token);
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!IsRunning) return;

            try
            {
                _cts?.Cancel();
                _workerThread?.Join(600);
            }
            catch { }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _workerThread = null;
                IsRunning = false;
                Volatile.Write(ref _inputPeakDb, -120f);
                Volatile.Write(ref _outputPeakDb, -120f);
                Volatile.Write(ref _voiceProbability, 0f);
            }
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void StartQuickTest(int seconds = 5)
    {
        lock (_testLock)
        {
            _testSampleBuffer.Clear();
            _testPlayPosition = 0;
            _testTargetFrames = 48000 * Math.Clamp(seconds, 2, 10);
            _testState = SuppressorQuickTestState.Recording;
            TestProgress = 0f;
            if (!IsRunning) Start();
        }
    }

    public void StopQuickTest()
    {
        lock (_testLock)
        {
            _testState = SuppressorQuickTestState.Idle;
            _testSampleBuffer.Clear();
            _testPlayPosition = 0;
            TestProgress = 0f;
        }
    }

    private void WorkerLoop(object? state)
    {
        var token = (CancellationToken)(state ?? CancellationToken.None);

        using var captureEvent = new AutoResetEvent(false);
        bool eventDriven = false;
        WaitHandle[] waitHandles = [captureEvent, token.WaitHandle];

        AudioInterop.IMMDeviceEnumerator? enumerator = null;
        AudioInterop.IMMDevice? micDevice = null;
        AudioInterop.IAudioClient? micAudioClient = null;
        AudioInterop.IAudioCaptureClient? captureClient = null;
        IntPtr pMixFormat = IntPtr.Zero;

        ushort micChannels = 1;
        int micSampleRate = 48000;
        ushort micBits = 16;
        bool micIsFloat = false;

        // Render: Microfone Virtual (CABLE Input)
        AudioInterop.IAudioClient? cableAudioClient = null;
        AudioInterop.IAudioRenderClient? cableRenderClient = null;
        IntPtr pCableMixFormat = IntPtr.Zero;
        ushort cableChannels = 2;
        int cableSampleRate = 48000;
        ushort cableBits = 32;
        bool cableIsFloat = true;

        // Render: Fones de Ouvido / Retorno (Monitor)
        AudioInterop.IAudioClient? monitorAudioClient = null;
        AudioInterop.IAudioRenderClient? monitorRenderClient = null;
        IntPtr pMonitorMixFormat = IntPtr.Zero;
        ushort monitorChannels = 2;
        int monitorSampleRate = 48000;
        ushort monitorBits = 32;
        bool monitorIsFloat = true;

        // Filas circulares e buffers pré-alocados (Zero GC)
        var captureRing = new RingBuffer<float>(48000 * 2);
        var cableRing = new RingBuffer<float>(48000 * 2);
        var monitorRing = new RingBuffer<float>(48000 * 2);

        float[] frameBuffer = new float[NoiseSuppressionEngine.FrameSamples];
        float[] rawFrameBuffer = new float[NoiseSuppressionEngine.FrameSamples];
        float[] tempCapturePacket = new float[4096];
        float[] resampleBuffer = new float[4096];
        float[] tempRenderBuffer = new float[4096];

        var captureResampler = new LinearResampler();
        var cableResampler = new LinearResampler();
        var monitorResampler = new LinearResampler();

        AudioInterop.TimeBeginPeriod(1);
        try
        {
            enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();

            // 1. Inicializar Microfone (Capture)
            if (!string.IsNullOrEmpty(SelectedInputDeviceId))
            {
                if (enumerator.GetDevice(SelectedInputDeviceId, out micDevice) != 0)
                    micDevice = null;
            }

            if (micDevice == null)
            {
                enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Capture, AudioInterop.ERole.Console, out micDevice);
            }

            if (micDevice != null)
            {
                Guid iidClient = new Guid(AudioInterop.IID_IAudioClient);
                if (micDevice.Activate(ref iidClient, 23, IntPtr.Zero, out var clientObj) == 0 && clientObj is AudioInterop.IAudioClient c)
                {
                    micAudioClient = c;
                    if (micAudioClient.GetMixFormat(out pMixFormat) == 0 && pMixFormat != IntPtr.Zero)
                    {
                        ushort tag = (ushort)Marshal.ReadInt16(pMixFormat, 0);
                        micChannels = (ushort)Marshal.ReadInt16(pMixFormat, 2);
                        micSampleRate = Marshal.ReadInt32(pMixFormat, 4);
                        micBits = (ushort)Marshal.ReadInt16(pMixFormat, 14);
                        if (tag == 3 || micBits == 32) micIsFloat = true;

                        // Período nativo de ultrabaixa latência (10ms a 15ms)
                        long hnsRequestedBuffer = 100000; // 10ms (100.000 hns)
                        if (micAudioClient.GetDevicePeriod(out long defPeriod, out _) == 0 && defPeriod > 0)
                        {
                            hnsRequestedBuffer = Math.Max(100000, defPeriod);
                        }

                        Guid session = Guid.Empty;
                        // Tentar primeiro modo Event-Driven (despertar instantâneo <0.1ms por interrupção de hardware)
                        int initHr = micAudioClient.Initialize(0, AudioInterop.AUDCLNT_STREAMFLAGS_EVENTCALLBACK, hnsRequestedBuffer, 0, pMixFormat, ref session);
                        if (initHr == 0 && micAudioClient.SetEventHandle(captureEvent.SafeWaitHandle.DangerousGetHandle()) == 0)
                        {
                            eventDriven = true;
                        }
                        else
                        {
                            // Fallback para modo compartilhado padrão caso o driver específico recuse flags de evento
                            try { Marshal.ReleaseComObject(micAudioClient); } catch { }
                            micAudioClient = null;
                            if (micDevice.Activate(ref iidClient, 23, IntPtr.Zero, out var clientObjRetry) == 0 && clientObjRetry is AudioInterop.IAudioClient cRetry)
                            {
                                micAudioClient = cRetry;
                                micAudioClient.Initialize(0, 0, hnsRequestedBuffer, 0, pMixFormat, ref session);
                            }
                        }

                        if (micAudioClient != null)
                        {
                            Guid iidCapture = new Guid(AudioInterop.IID_IAudioCaptureClient);
                            if (micAudioClient.GetService(ref iidCapture, out var capObj) == 0 && capObj is AudioInterop.IAudioCaptureClient cap)
                            {
                                captureClient = cap;
                                micAudioClient.Start();
                            }
                        }
                    }
                }
            }

            // 2. Loop principal de leitura e processamento
            while (!token.IsCancellationRequested)
            {
                bool readAny = false;

                // Captura pacotes do microfone
                if (captureClient != null)
                {
                    while (captureClient.GetNextPacketSize(out uint packetFrames) == 0 && packetFrames > 0 && !token.IsCancellationRequested)
                    {
                        int hr = captureClient.GetBuffer(out IntPtr pData, out uint framesToRead, out uint flags, out _, out _);
                        if (hr == 0 && pData != IntPtr.Zero && framesToRead > 0)
                        {
                            readAny = true;
                            bool isSilent = (flags & 2) != 0;

                            if (tempCapturePacket.Length < framesToRead)
                                tempCapturePacket = new float[framesToRead];

                            for (int f = 0; f < framesToRead; f++)
                            {
                                float sampleVal = 0f;
                                if (!isSilent)
                                {
                                    int srcOffset = f * micChannels;
                                    if (micIsFloat)
                                    {
                                        sampleVal = Marshal.PtrToStructure<float>(pData + srcOffset * 4);
                                    }
                                    else if (micBits == 16)
                                    {
                                        short s = Marshal.ReadInt16(pData + srcOffset * 2);
                                        sampleVal = s / 32768.0f;
                                    }
                                }
                                tempCapturePacket[f] = sampleVal;
                            }

                            // Reamostra para 48000 Hz se o microfone estiver em 44.1kHz ou outra taxa
                            if (micSampleRate != NoiseSuppressionEngine.SampleRate)
                            {
                                int needed = LinearResampler.EstimateOutputCount((int)framesToRead, micSampleRate, NoiseSuppressionEngine.SampleRate);
                                if (resampleBuffer.Length < needed) resampleBuffer = new float[needed];

                                int generated = captureResampler.Resample(
                                    tempCapturePacket.AsSpan(0, (int)framesToRead),
                                    resampleBuffer,
                                    micSampleRate,
                                    NoiseSuppressionEngine.SampleRate);

                                captureRing.WriteOverwrite(new ReadOnlySpan<float>(resampleBuffer, 0, generated));
                            }
                            else
                            {
                                captureRing.WriteOverwrite(new ReadOnlySpan<float>(tempCapturePacket, 0, (int)framesToRead));
                            }

                            captureClient.ReleaseBuffer(framesToRead);
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // Anti-Lag: Limita o backlog acumulado do microfone a 40ms (1920 amostras a 48kHz)
                // Se houver pacotes acumulados de pausas ou oscilações, descarta o áudio antigo para manter a voz em tempo real
                captureRing.TrimExcess(NoiseSuppressionEngine.FrameSamples * 4);

                // Processa em blocos de 480 amostras mono a 48 kHz (10 ms)
                while (captureRing.Count >= NoiseSuppressionEngine.FrameSamples && !token.IsCancellationRequested)
                {
                    captureRing.Read(frameBuffer);
                    Array.Copy(frameBuffer, rawFrameBuffer, NoiseSuppressionEngine.FrameSamples);

                    float inPeak = 0f;
                    for (int i = 0; i < NoiseSuppressionEngine.FrameSamples; i++)
                    {
                        float abs = Math.Abs(frameBuffer[i]);
                        if (abs > inPeak) inPeak = abs;
                    }

                    float inDb = inPeak <= 0.000001f ? -120f : 20f * MathF.Log10(inPeak);
                    Volatile.Write(ref _inputPeakDb, inDb);

                    // Processamento Neural (RNNoise + Rumble Filter + EQ + Intelligent Gate)
                    Engine.Process(frameBuffer);

                    float outPeak = 0f;
                    for (int i = 0; i < NoiseSuppressionEngine.FrameSamples; i++)
                    {
                        float abs = Math.Abs(frameBuffer[i]);
                        if (abs > outPeak) outPeak = abs;
                    }
                    float outDb = outPeak <= 0.000001f ? -120f : 20f * MathF.Log10(outPeak);
                    Volatile.Write(ref _outputPeakDb, outDb);
                    Volatile.Write(ref _voiceProbability, Engine.LastVoiceProbability);

                    // Teste Rápido: Modo Gravação
                    if (_testState == SuppressorQuickTestState.Recording)
                    {
                        lock (_testLock)
                        {
                            _testSampleBuffer.AddRange(frameBuffer);
                            TestProgress = (float)_testSampleBuffer.Count / _testTargetFrames;
                            if (_testSampleBuffer.Count >= _testTargetFrames)
                            {
                                _testState = SuppressorQuickTestState.Playing;
                                _testPlayPosition = 0;
                            }
                        }
                    }

                    // Envio para o Microfone Virtual (CABLE Input -> CABLE Output nos apps)
                    if (IsVirtualCableRoutingEnabled && _masterEnabled && cableAudioClient != null)
                    {
                        if (cableSampleRate != NoiseSuppressionEngine.SampleRate)
                        {
                            int needed = LinearResampler.EstimateOutputCount(NoiseSuppressionEngine.FrameSamples, NoiseSuppressionEngine.SampleRate, cableSampleRate);
                            if (resampleBuffer.Length < needed) resampleBuffer = new float[needed];

                            int gen = cableResampler.Resample(frameBuffer, resampleBuffer, NoiseSuppressionEngine.SampleRate, cableSampleRate);
                            cableRing.WriteOverwrite(new ReadOnlySpan<float>(resampleBuffer, 0, gen));
                        }
                        else
                        {
                            cableRing.WriteOverwrite(frameBuffer);
                        }
                    }
                    else if (!IsVirtualCableRoutingEnabled || !_masterEnabled || cableAudioClient == null)
                    {
                        cableRing.Clear();
                    }

                    // Envio para o Retorno no Fone de Ouvido
                    if (IsMonitoringEnabled && _testState != SuppressorQuickTestState.Playing)
                    {
                        ReadOnlySpan<float> monitorSrc = (MonitorMode == SuppressorMonitorMode.Processed)
                            ? frameBuffer
                            : rawFrameBuffer;

                        if (monitorSampleRate != NoiseSuppressionEngine.SampleRate)
                        {
                            int needed = LinearResampler.EstimateOutputCount(NoiseSuppressionEngine.FrameSamples, NoiseSuppressionEngine.SampleRate, monitorSampleRate);
                            if (resampleBuffer.Length < needed) resampleBuffer = new float[needed];

                            int gen = monitorResampler.Resample(monitorSrc, resampleBuffer, NoiseSuppressionEngine.SampleRate, monitorSampleRate);
                            monitorRing.WriteOverwrite(new ReadOnlySpan<float>(resampleBuffer, 0, gen));
                        }
                        else
                        {
                            monitorRing.WriteOverwrite(monitorSrc);
                        }
                    }
                    else if (!IsMonitoringEnabled && _testState == SuppressorQuickTestState.Idle)
                    {
                        monitorRing.Clear();
                    }
                }

                // Teste Rápido: Modo Reprodução do resultado gravado
                if (_testState == SuppressorQuickTestState.Playing)
                {
                    lock (_testLock)
                    {
                        int remaining = _testSampleBuffer.Count - _testPlayPosition;
                        if (remaining > 0)
                        {
                            int chunkSize = Math.Min(remaining, NoiseSuppressionEngine.FrameSamples);
                            var slice = CollectionsMarshal.AsSpan(_testSampleBuffer).Slice(_testPlayPosition, chunkSize);
                            _testPlayPosition += chunkSize;
                            TestProgress = 1.0f - (float)_testPlayPosition / _testSampleBuffer.Count;

                            if (monitorSampleRate != NoiseSuppressionEngine.SampleRate)
                            {
                                int needed = LinearResampler.EstimateOutputCount(chunkSize, NoiseSuppressionEngine.SampleRate, monitorSampleRate);
                                if (resampleBuffer.Length < needed) resampleBuffer = new float[needed];

                                int gen = monitorResampler.Resample(slice, resampleBuffer, NoiseSuppressionEngine.SampleRate, monitorSampleRate);
                                monitorRing.WriteOverwrite(new ReadOnlySpan<float>(resampleBuffer, 0, gen));
                            }
                            else
                            {
                                monitorRing.WriteOverwrite(slice);
                            }
                        }
                        else
                        {
                            _testState = SuppressorQuickTestState.Idle;
                            TestProgress = 0f;
                        }
                    }
                }

                // 3. Renderização contínua para Microfone Virtual (CABLE Input)
                if (IsVirtualCableRoutingEnabled && _masterEnabled && enumerator != null)
                {
                    if (cableAudioClient == null)
                    {
                        string? targetCableId = SelectedVirtualCableDeviceId;
                        if (string.IsNullOrEmpty(targetCableId))
                        {
                            var autoCable = VirtualCableManager.GetCableInputDevice();
                            targetCableId = autoCable?.Id;
                        }

                        // Proteção estrita: O microfone virtual NUNCA pode ser inicializado no fone de ouvido de monitoramento
                        // e NUNCA pode cair no dispositivo padrão do sistema (fones/alto-falantes físicos).
                        if (!string.IsNullOrEmpty(targetCableId) &&
                            (string.IsNullOrEmpty(SelectedOutputDeviceId) || !string.Equals(targetCableId, SelectedOutputDeviceId, StringComparison.OrdinalIgnoreCase)))
                        {
                            VirtualCableManager.EnsureCableInputEnabled();
                            TryInitRenderEndpoint(
                                enumerator, targetCableId,
                                ref cableAudioClient, ref cableRenderClient, ref pCableMixFormat,
                                out cableChannels, out cableSampleRate, out cableBits, out cableIsFloat,
                                allowDefaultFallback: false);
                        }
                    }

                    if (cableAudioClient != null && cableRenderClient != null)
                    {
                        PumpRenderRing(cableAudioClient, cableRenderClient, cableRing, cableChannels, cableBits, cableIsFloat, ref tempRenderBuffer);
                    }
                }
                else if (cableAudioClient != null)
                {
                    try { cableAudioClient.Stop(); } catch { }
                    cableAudioClient = null;
                    cableRenderClient = null;
                    cableRing.Clear();
                }

                // 4. Renderização contínua para Fone de Ouvido / Monitoramento
                if ((IsMonitoringEnabled || _testState == SuppressorQuickTestState.Playing) && enumerator != null)
                {
                    if (monitorAudioClient == null)
                    {
                        TryInitRenderEndpoint(
                            enumerator, SelectedOutputDeviceId,
                            ref monitorAudioClient, ref monitorRenderClient, ref pMonitorMixFormat,
                            out monitorChannels, out monitorSampleRate, out monitorBits, out monitorIsFloat,
                            allowDefaultFallback: true);
                    }

                    if (monitorAudioClient != null && monitorRenderClient != null)
                    {
                        PumpRenderRing(monitorAudioClient, monitorRenderClient, monitorRing, monitorChannels, monitorBits, monitorIsFloat, ref tempRenderBuffer);
                    }
                }
                else if (monitorAudioClient != null)
                {
                    try { monitorAudioClient.Stop(); } catch { }
                    monitorAudioClient = null;
                    monitorRenderClient = null;
                    monitorRing.Clear();
                }

                if (!readAny)
                {
                    if (eventDriven)
                    {
                        WaitHandle.WaitAny(waitHandles, 4);
                    }
                    else
                    {
                        token.WaitHandle.WaitOne(1);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Thread loop safe
        }
        finally
        {
            AudioInterop.TimeEndPeriod(1);
            try { micAudioClient?.Stop(); } catch { }
            try { cableAudioClient?.Stop(); } catch { }
            try { monitorAudioClient?.Stop(); } catch { }

            if (pMixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pMixFormat);
            if (pCableMixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pCableMixFormat);
            if (pMonitorMixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pMonitorMixFormat);

            micAudioClient = null;
            captureClient = null;
            micDevice = null;

            cableAudioClient = null;
            cableRenderClient = null;

            monitorAudioClient = null;
            monitorRenderClient = null;

            enumerator = null;
        }
    }

    private static bool TryInitRenderEndpoint(
        AudioInterop.IMMDeviceEnumerator enumerator,
        string? deviceId,
        ref AudioInterop.IAudioClient? audioClient,
        ref AudioInterop.IAudioRenderClient? renderClient,
        ref IntPtr pMixFormat,
        out ushort channels,
        out int sampleRate,
        out ushort bits,
        out bool isFloat,
        bool allowDefaultFallback = false)
    {
        channels = 2;
        sampleRate = 48000;
        bits = 32;
        isFloat = true;

        try
        {
            AudioInterop.IMMDevice? dev = null;
            if (!string.IsNullOrEmpty(deviceId))
            {
                if (enumerator.GetDevice(deviceId, out dev) != 0) dev = null;
            }
            if (dev == null && allowDefaultFallback)
            {
                enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Console, out dev);
            }
            if (dev == null) return false;

            Guid iidClient = new Guid(AudioInterop.IID_IAudioClient);
            if (dev.Activate(ref iidClient, 23, IntPtr.Zero, out var clientObj) != 0 || clientObj is not AudioInterop.IAudioClient activeClient)
                return false;

            AudioInterop.IAudioClient? client = activeClient;
            if (client.GetMixFormat(out pMixFormat) != 0 || pMixFormat == IntPtr.Zero)
            {
                return false;
            }

            channels = (ushort)Marshal.ReadInt16(pMixFormat, 2);
            sampleRate = Marshal.ReadInt32(pMixFormat, 4);
            bits = (ushort)Marshal.ReadInt16(pMixFormat, 14);
            isFloat = Marshal.ReadInt16(pMixFormat, 0) == 3 || bits == 32;

            // Buffer otimizado de baixa latência (10ms a 15ms)
            long hnsRenderBuffer = 120000; // 12ms (120.000 hns)
            if (client.GetDevicePeriod(out long defPeriod, out _) == 0 && defPeriod > 0)
            {
                hnsRenderBuffer = Math.Max(100000, defPeriod);
            }

            Guid session = Guid.Empty;
            if (client.Initialize(0, 0, hnsRenderBuffer, 0, pMixFormat, ref session) != 0)
            {
                // Fallback limpo reativando o client caso o endpoint exija buffer ligeiramente maior
                try { Marshal.ReleaseComObject(client); } catch { }
                client = null;
                if (dev.Activate(ref iidClient, 23, IntPtr.Zero, out var clientObjRetry) == 0 && clientObjRetry is AudioInterop.IAudioClient clientRetry)
                {
                    client = clientRetry;
                    if (client.Initialize(0, 0, 250000, 0, pMixFormat, ref session) != 0)
                        return false;
                }
                else
                {
                    return false;
                }
            }

            Guid iidRender = new Guid(AudioInterop.IID_IAudioRenderClient);
            if (client.GetService(ref iidRender, out var renObj) != 0 || renObj is not AudioInterop.IAudioRenderClient rCli)
                return false;

            audioClient = client;
            renderClient = rCli;
            audioClient.Start();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void PumpRenderRing(
        AudioInterop.IAudioClient audioClient,
        AudioInterop.IAudioRenderClient renderClient,
        RingBuffer<float> ring,
        ushort channels,
        ushort bits,
        bool isFloat,
        ref float[] tempBuffer)
    {
        // Anti-Lag: Mantém no máximo ~30ms de amostras na fila (1440 amostras a 48kHz)
        // Isso elimina acúmulo perpétuo de fila sem causar cortes no áudio.
        ring.TrimExcess(1440);

        if (ring.Count == 0) return;

        if (audioClient.GetCurrentPadding(out uint padding) != 0 ||
            audioClient.GetBufferSize(out uint bufferSize) != 0)
            return;

        uint availableFrames = bufferSize > padding ? bufferSize - padding : 0;
        int framesToWrite = Math.Min((int)availableFrames, ring.Count);
        if (framesToWrite <= 0) return;

        if (tempBuffer.Length < framesToWrite)
            tempBuffer = new float[framesToWrite];

        int readCount = ring.Read(tempBuffer.AsSpan(0, framesToWrite));
        if (readCount <= 0) return;

        if (renderClient.GetBuffer((uint)readCount, out IntPtr pData) != 0 || pData == IntPtr.Zero)
            return;

        for (int i = 0; i < readCount; i++)
        {
            float s = tempBuffer[i];
            for (int ch = 0; ch < channels; ch++)
            {
                int dstIdx = i * channels + ch;
                if (isFloat)
                {
                    Marshal.StructureToPtr(s, pData + dstIdx * 4, false);
                }
                else if (bits == 16)
                {
                    short val = (short)Math.Clamp(s * 32767.0f, -32768.0f, 32767.0f);
                    Marshal.WriteInt16(pData + dstIdx * 2, val);
                }
            }
        }
        renderClient.ReleaseBuffer((uint)readCount, 0);
    }

    public void Dispose()
    {
        Stop();
        Engine.Dispose();
    }
}
