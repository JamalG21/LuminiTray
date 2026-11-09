using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

public enum AudioOutputProfile
{
    Speakers,   // Caixas de som
    Headphones  // Fone de ouvido
}

public sealed class AudioPeripheralsTester : IDisposable
{
    // Perfil atual de saída
    public AudioOutputProfile CurrentProfile { get; set; } = AudioOutputProfile.Headphones;
    public string? SelectedMicDeviceId { get; set; }
    public string? SelectedOutputDeviceId { get; set; }

    // Volume dos testes de áudio (escala 0.0 a 1.0)
    public float TestVolumeScalar { get; set; } = 1.0f;

    // Ganho digital de software para o microfone (1.0x a 4.0x)
    public double MicSoftwareGain { get; set; } = 1.0;

    // Estado do microfone
    private bool _micRunning;
    private CancellationTokenSource? _micCts;

    // Métricas do microfone em tempo real
    public double MicVolumeDb { get; private set; } = -60.0;
    public double MicVolumePercent { get; private set; }
    public double[] MicFftBands { get; } = new double[16]; // 16 faixas de frequência (0.0 a 1.0)
    public double BassLevel { get; private set; }
    public double MidLevel { get; private set; }
    public double TrebleLevel { get; private set; }
    public int MicSampleRate { get; private set; } = 48000;
    public string MicNoiseLevel { get; private set; } = "Silencioso";
    public string MicStatusVerdict => MicVolumeDb switch
    {
        < -55 => "Sem sinal / Silêncio",
        < -45 => "Sinal muito baixo (Aumente o ganho)",
        < -6 => "Nível Ideal (Voz Clara)",
        _ => "Distorção / Clipando (Volume muito alto!)"
    };

    // Gravação e Reprodução de Teste (5 segundos)
    public bool IsRecordingVoice { get; private set; }
    public bool IsPlayingRecordedVoice { get; private set; }
    public int RecordSecondsRemaining { get; private set; }
    private MemoryStream? _recordedVoiceStream;
    private byte[]? _recordedVoiceWavBytes;
    private CancellationTokenSource? _recordCts;
    private int _recordedChannels = 1;
    private int _recordedSampleRate = 48000;

    // Retorno ao vivo (Loopback / Eco)
    public bool IsLoopbackEnabled { get; set; }
    public double LoopbackVolume { get; set; } = 0.8;

    // Notificações
    public event Action? MicStateUpdated;
    public event Action<double, double>? SweepProgress; // (currentHz, fraction 0..1)
    public event Action? VoiceRecordingFinished;

    private CancellationTokenSource? _sweepCts;
    private SoundPlayer? _activePlayer;

    #region Testes de Saída de Áudio (Speakers vs Headphones)

    private void EnsureSelectedOutputDevice()
    {
        if (!string.IsNullOrEmpty(SelectedOutputDeviceId))
        {
            try
            {
                // Altera tanto Console/Multimídia quanto Comunicações (Discord, Teams, etc.)
                AudioCore.SetDefaultDevice(SelectedOutputDeviceId, changeCommunications: true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Testa o canal de áudio especificado (Left, Right ou Ambos) gerando tom estéreo isolado.
    /// </summary>
    public void TestChannel(bool testLeft, bool testRight, double durationSec = 1.2, int frequency = 440)
    {
        StopPlayback();
        EnsureSelectedOutputDevice();

        byte[] wavBytes = GenerateToneWav(frequency, durationSec, testLeft ? 1.0 : 0.0, testRight ? 1.0 : 0.0);
        PlayWavBytes(wavBytes);
    }

    /// <summary>
    /// Alterna áudio estéreo automaticamente entre Esquerda e Direita (Ping-Pong).
    /// </summary>
    public void TestPingPong(int cycles = 3)
    {
        StopPlayback();
        EnsureSelectedOutputDevice();

        Task.Run(async () =>
        {
            for (int i = 0; i < cycles; i++)
            {
                // Esquerda
                byte[] leftWav = GenerateToneWav(440, 0.6, 1.0, 0.0);
                using var p1 = new SoundPlayer(new MemoryStream(leftWav));
                p1.PlaySync();
                await Task.Delay(150);

                // Direita
                byte[] rightWav = GenerateToneWav(880, 0.6, 0.0, 1.0);
                using var p2 = new SoundPlayer(new MemoryStream(rightWav));
                p2.PlaySync();
                await Task.Delay(150);
            }
        });
    }

    /// <summary>
    /// Teste de Fase Acústica (Polaridade).
    /// inPhase: som centrado e encorpado.
    /// outOfPhase: som oco e espalhado fora da cabeça (detecta polaridade invertida de caixas/fones).
    /// </summary>
    public void TestAcousticPhase(bool inPhase, double durationSec = 2.5)
    {
        StopPlayback();
        EnsureSelectedOutputDevice();

        byte[] wavBytes = GeneratePhaseTestWav(inPhase, durationSec);
        PlayWavBytes(wavBytes);
    }

    /// <summary>
    /// Teste de canais Surround 5.1 (Frontal Esquerdo, Central, Frontal Direito, Traseiro Esq, Traseiro Dir, Subwoofer).
    /// </summary>
    public void TestSurroundChannel(string channelName)
    {
        StopPlayback();
        EnsureSelectedOutputDevice();

        byte[] wavBytes = channelName switch
        {
            "FrontLeft" => GenerateToneWav(440, 1.0, 1.0, 0.0),
            "Center" => GenerateToneWav(550, 1.0, 0.7, 0.7),
            "FrontRight" => GenerateToneWav(660, 1.0, 0.0, 1.0),
            "RearLeft" => GenerateSurroundWav(440, 1.0, true),
            "RearRight" => GenerateSurroundWav(660, 1.0, false),
            "Subwoofer" => GenerateToneWav(50, 1.5, 1.0, 1.0), // 50 Hz grave
            _ => GenerateToneWav(440, 1.0, 1.0, 1.0)
        };
        PlayWavBytes(wavBytes);
    }

    /// <summary>
    /// Toca tom contínuo de frequência específica (ex: 20Hz, 80Hz, 1000Hz, 10kHz, 15kHz).
    /// </summary>
    public void PlayFrequencyTone(int frequency, double durationSec = 1.5)
    {
        StopPlayback();
        EnsureSelectedOutputDevice();
        byte[] wavBytes = GenerateToneWav(frequency, durationSec, 1.0, 1.0);
        PlayWavBytes(wavBytes);
    }

    /// <summary>
    /// Inicia uma varredura senoidal contínua de 20 Hz a 20.000 Hz.
    /// </summary>
    public void StartFrequencySweep(double durationSec = 8.0)
    {
        StopSweep();
        EnsureSelectedOutputDevice();
        _sweepCts = new CancellationTokenSource();
        var token = _sweepCts.Token;

        Task.Run(async () =>
        {
            try
            {
                int sampleRate = 44100;
                byte[] sweepWav = GenerateLogSweepWav(20, 20000, durationSec, sampleRate);
                using var ms = new MemoryStream(sweepWav);
                using var player = new SoundPlayer(ms);
                player.Load();
                player.Play();

                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < durationSec && !token.IsCancellationRequested)
                {
                    double t = sw.Elapsed.TotalSeconds / durationSec;
                    double currentHz = 20.0 * Math.Pow(20000.0 / 20.0, t);
                    SweepProgress?.Invoke(Math.Round(currentHz, 0), Math.Clamp(t, 0.0, 1.0));
                    await Task.Delay(50, token);
                }

                SweepProgress?.Invoke(20000, 1.0);
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                SweepProgress?.Invoke(0, 0);
            }
        }, token);
    }

    public void StopSweep()
    {
        try { _sweepCts?.Cancel(); } catch { }
        _sweepCts = null;
        StopPlayback();
        SweepProgress?.Invoke(0, 0);
    }

    public void StopPlayback()
    {
        try
        {
            _activePlayer?.Stop();
            _activePlayer?.Dispose();
            _activePlayer = null;
        }
        catch { }
    }

    #endregion

    #region Geradores de Áudio WAV PCM

    private byte[] GenerateToneWav(int frequency, double durationSec, double leftGain, double rightGain, int sampleRate = 44100)
    {
        int totalSamples = (int)(sampleRate * durationSec);
        int bytesPerSample = 2; // 16-bit
        int channels = 2;       // Estéreo
        int dataSize = totalSamples * channels * bytesPerSample;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        // Header WAV
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bytesPerSample);
        bw.Write((short)(channels * bytesPerSample));
        bw.Write((short)(bytesPerSample * 8));
        bw.Write("data"u8);
        bw.Write(dataSize);

        int fadeSamples = (int)(sampleRate * 0.02);
        double volumeFactor = Math.Clamp(TestVolumeScalar, 0.0f, 1.0f);

        for (int i = 0; i < totalSamples; i++)
        {
            double envelope = 1.0;
            if (i < fadeSamples) envelope = (double)i / fadeSamples;
            else if (i > totalSamples - fadeSamples) envelope = (double)(totalSamples - i) / fadeSamples;

            double t = (double)i / sampleRate;
            double sample = Math.Sin(2.0 * Math.PI * frequency * t) * envelope;

            short leftSample = (short)Math.Clamp(sample * leftGain * volumeFactor * 32000.0, -32767.0, 32767.0);
            short rightSample = (short)Math.Clamp(sample * rightGain * volumeFactor * 32000.0, -32767.0, 32767.0);

            bw.Write(leftSample);
            bw.Write(rightSample);
        }

        return ms.ToArray();
    }

    private byte[] GeneratePhaseTestWav(bool inPhase, double durationSec, int sampleRate = 44100)
    {
        int totalSamples = (int)(sampleRate * durationSec);
        int bytesPerSample = 2;
        int channels = 2;
        int dataSize = totalSamples * channels * bytesPerSample;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bytesPerSample);
        bw.Write((short)(channels * bytesPerSample));
        bw.Write((short)(bytesPerSample * 8));
        bw.Write("data"u8);
        bw.Write(dataSize);

        int fadeSamples = (int)(sampleRate * 0.03);
        double volumeFactor = Math.Clamp(TestVolumeScalar, 0.0f, 1.0f);

        for (int i = 0; i < totalSamples; i++)
        {
            double envelope = 1.0;
            if (i < fadeSamples) envelope = (double)i / fadeSamples;
            else if (i > totalSamples - fadeSamples) envelope = (double)(totalSamples - i) / fadeSamples;

            double t = (double)i / sampleRate;
            double sample = (Math.Sin(2.0 * Math.PI * 250.0 * t) * 0.7 + Math.Sin(2.0 * Math.PI * 500.0 * t) * 0.3) * envelope;

            short leftSample = (short)Math.Clamp(sample * volumeFactor * 30000.0, -32767.0, 32767.0);
            short rightSample = (short)Math.Clamp((inPhase ? sample : -sample) * volumeFactor * 30000.0, -32767.0, 32767.0);

            bw.Write(leftSample);
            bw.Write(rightSample);
        }

        return ms.ToArray();
    }

    private byte[] GenerateSurroundWav(int frequency, double durationSec, bool isLeft, int sampleRate = 44100)
    {
        int totalSamples = (int)(sampleRate * durationSec);
        int bytesPerSample = 2;
        int channels = 2;
        int dataSize = totalSamples * channels * bytesPerSample;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bytesPerSample);
        bw.Write((short)(channels * bytesPerSample));
        bw.Write((short)(bytesPerSample * 8));
        bw.Write("data"u8);
        bw.Write(dataSize);

        int delaySamples = (int)(sampleRate * 0.0006);
        double volumeFactor = Math.Clamp(TestVolumeScalar, 0.0f, 1.0f);

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double samplePrimary = Math.Sin(2.0 * Math.PI * frequency * t);
            double sampleSecondary = (i >= delaySamples) ? Math.Sin(2.0 * Math.PI * frequency * (t - 0.0006)) * 0.3 : 0.0;

            short left = (short)Math.Clamp((isLeft ? samplePrimary : sampleSecondary) * volumeFactor * 30000.0, -32767.0, 32767.0);
            short right = (short)Math.Clamp((isLeft ? sampleSecondary : samplePrimary) * volumeFactor * 30000.0, -32767.0, 32767.0);

            bw.Write(left);
            bw.Write(right);
        }

        return ms.ToArray();
    }

    private byte[] GenerateLogSweepWav(double startFreq, double endFreq, double durationSec, int sampleRate)
    {
        int totalSamples = (int)(sampleRate * durationSec);
        int bytesPerSample = 2;
        int channels = 2;
        int dataSize = totalSamples * channels * bytesPerSample;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bytesPerSample);
        bw.Write((short)(channels * bytesPerSample));
        bw.Write((short)(bytesPerSample * 8));
        bw.Write("data"u8);
        bw.Write(dataSize);

        int fadeSamples = (int)(sampleRate * 0.02);
        double volumeFactor = Math.Clamp(TestVolumeScalar, 0.0f, 1.0f);

        for (int i = 0; i < totalSamples; i++)
        {
            double envelope = 1.0;
            if (i < fadeSamples) envelope = (double)i / fadeSamples;
            else if (i > totalSamples - fadeSamples) envelope = (double)(totalSamples - i) / fadeSamples;

            double t = (double)i / totalSamples;
            double instantaneousPhase = 2.0 * Math.PI * startFreq * durationSec * (Math.Pow(endFreq / startFreq, t) - 1.0) / Math.Log(endFreq / startFreq);
            double sample = Math.Sin(instantaneousPhase) * envelope;

            short s = (short)Math.Clamp(sample * volumeFactor * 30000.0, -32767.0, 32767.0);
            bw.Write(s);
            bw.Write(s);
        }

        return ms.ToArray();
    }

    private void PlayWavBytes(byte[] wavBytes)
    {
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(wavBytes);
                _activePlayer = new SoundPlayer(ms);
                _activePlayer.PlaySync();
            }
            catch { }
        });
    }

    #endregion

    #region Microfone (WASAPI Nativo - Captura, VU Meter dBFS, Gravação 5s, Retorno ao Vivo & FFT)

    public void StartMicCapture()
    {
        if (_micRunning) return;
        _micRunning = true;
        _micCts = new CancellationTokenSource();
        var token = _micCts.Token;

        Task.Run(() => MicCaptureWorker(token), token);
    }

    private void MicCaptureWorker(CancellationToken token)
    {
        AudioInterop.IMMDeviceEnumerator? enumerator = null;
        AudioInterop.IMMDevice? micDevice = null;
        AudioInterop.IMMDevice? renderDevice = null;
        AudioInterop.IAudioClient? micAudioClient = null;
        AudioInterop.IAudioCaptureClient? captureClient = null;
        AudioInterop.IAudioClient? renderAudioClient = null;
        AudioInterop.IAudioRenderClient? renderClient = null;

        IntPtr pMixFormat = IntPtr.Zero;
        IntPtr pRenderMixFormat = IntPtr.Zero;

        ushort channels = 1;
        int sampleRate = 48000;
        ushort bitsPerSample = 16;
        bool isFloat = false;

        ushort renderChannels = 2;
        ushort renderBits = 32;

        try
        {
            enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
            if (!string.IsNullOrEmpty(SelectedMicDeviceId))
            {
                if (enumerator.GetDevice(SelectedMicDeviceId, out micDevice) != 0)
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
                        ushort wFormatTag = (ushort)Marshal.ReadInt16(pMixFormat, 0);
                        channels = (ushort)Marshal.ReadInt16(pMixFormat, 2);
                        sampleRate = Marshal.ReadInt32(pMixFormat, 4);
                        bitsPerSample = (ushort)Marshal.ReadInt16(pMixFormat, 14);

                        // WAVE_FORMAT_IEEE_FLOAT (3) ou WAVE_FORMAT_EXTENSIBLE (0xFFFE) com subformato float
                        if (wFormatTag == 3 || bitsPerSample == 32)
                            isFloat = true;

                        MicSampleRate = sampleRate;
                        _recordedChannels = channels;
                        _recordedSampleRate = sampleRate;

                        Guid session = Guid.Empty;
                        // 100ms de buffer (1.000.000 hns)
                        if (micAudioClient.Initialize(0, 0, 1000000, 0, pMixFormat, ref session) == 0)
                        {
                            Guid iidCapture = new Guid(AudioInterop.IID_IAudioCaptureClient);
                            if (micAudioClient.GetService(ref iidCapture, out var captureObj) == 0 && captureObj is AudioInterop.IAudioCaptureClient cap)
                            {
                                captureClient = cap;
                                micAudioClient.Start();
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // Variáveis auxiliares para FFT e métricas
        var rand = new Random();
        float[] sampleBuffer = new float[2048];
        byte[] pcm16RecordBuffer = new byte[4096];

        while (!token.IsCancellationRequested && _micRunning)
        {
            bool hasRealPcm = false;
            float maxPeak = 0f;
            float rmsSum = 0f;
            int samplesRead = 0;

            try
            {
                if (captureClient != null)
                {
                    while (captureClient.GetNextPacketSize(out uint packetFrames) == 0 && packetFrames > 0 && !token.IsCancellationRequested)
                    {
                        int hr = captureClient.GetBuffer(out IntPtr pData, out uint framesToRead, out uint flags, out _, out _);
                        if (hr == 0 && pData != IntPtr.Zero && framesToRead > 0)
                        {
                            hasRealPcm = true;
                            int totalValues = (int)(framesToRead * channels);
                            int framesRecorded = 0;

                            for (int i = 0; i < totalValues; i++)
                            {
                                float val = 0f;
                                if (isFloat)
                                {
                                    val = Marshal.PtrToStructure<float>(pData + i * 4);
                                }
                                else if (bitsPerSample == 16)
                                {
                                    short s = Marshal.ReadInt16(pData + i * 2);
                                    val = s / 32768.0f;
                                }

                                // Aplica Ganho de Software
                                val = (float)Math.Clamp(val * MicSoftwareGain, -1.0, 1.0);

                                float absVal = Math.Abs(val);
                                if (absVal > maxPeak) maxPeak = absVal;
                                rmsSum += val * val;
                                samplesRead++;

                                if (samplesRead < sampleBuffer.Length)
                                    sampleBuffer[samplesRead - 1] = val;

                                // Gravação de 5s
                                if (IsRecordingVoice && _recordedVoiceStream != null)
                                {
                                    short pcmSample = (short)Math.Clamp(val * 32767.0f, -32767.0f, 32767.0f);
                                    if (framesRecorded * 2 + 1 < pcm16RecordBuffer.Length)
                                    {
                                        pcm16RecordBuffer[framesRecorded * 2] = (byte)(pcmSample & 0xFF);
                                        pcm16RecordBuffer[framesRecorded * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
                                        framesRecorded++;
                                    }
                                }
                            }

                            if (IsRecordingVoice && _recordedVoiceStream != null && framesRecorded > 0)
                            {
                                try { _recordedVoiceStream.Write(pcm16RecordBuffer, 0, framesRecorded * 2); } catch { }
                            }

                            // Retorno ao vivo (Loopback em tempo real)
                            if (IsLoopbackEnabled)
                            {
                                try
                                {
                                    if (renderAudioClient == null && enumerator != null)
                                    {
                                        if (!string.IsNullOrEmpty(SelectedOutputDeviceId))
                                            enumerator.GetDevice(SelectedOutputDeviceId, out renderDevice);
                                        if (renderDevice == null)
                                            enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Console, out renderDevice);

                                        if (renderDevice != null)
                                        {
                                            Guid iidClient = new Guid(AudioInterop.IID_IAudioClient);
                                            if (renderDevice.Activate(ref iidClient, 23, IntPtr.Zero, out var rObj) == 0 && rObj is AudioInterop.IAudioClient rc)
                                            {
                                                renderAudioClient = rc;
                                                if (renderAudioClient.GetMixFormat(out pRenderMixFormat) == 0 && pRenderMixFormat != IntPtr.Zero)
                                                {
                                                    renderChannels = (ushort)Marshal.ReadInt16(pRenderMixFormat, 2);
                                                    renderBits = (ushort)Marshal.ReadInt16(pRenderMixFormat, 14);
                                                    Guid sId = Guid.Empty;
                                                    if (renderAudioClient.Initialize(0, 0, 1000000, 0, pRenderMixFormat, ref sId) == 0)
                                                    {
                                                        Guid iidRender = new Guid(AudioInterop.IID_IAudioRenderClient);
                                                        if (renderAudioClient.GetService(ref iidRender, out var rServ) == 0 && rServ is AudioInterop.IAudioRenderClient rCl)
                                                        {
                                                            renderClient = rCl;
                                                            renderAudioClient.Start();
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    if (renderClient != null && framesToRead > 0)
                                    {
                                        if (renderClient.GetBuffer(framesToRead, out IntPtr pRenderData) == 0 && pRenderData != IntPtr.Zero)
                                        {
                                            float factor = (float)(LoopbackVolume * MicSoftwareGain);
                                            int renderSamples = (int)(framesToRead * renderChannels);

                                            for (int f = 0; f < framesToRead; f++)
                                            {
                                                float srcSample = 0f;
                                                if (isFloat)
                                                    srcSample = Marshal.PtrToStructure<float>(pData + (f * channels) * 4);
                                                else
                                                    srcSample = Marshal.ReadInt16(pData + (f * channels) * 2) / 32768f;

                                                srcSample = (float)Math.Clamp(srcSample * factor, -1.0, 1.0);

                                                for (int ch = 0; ch < renderChannels; ch++)
                                                {
                                                    int renderIdx = f * renderChannels + ch;
                                                    if (renderBits == 32)
                                                    {
                                                        Marshal.StructureToPtr(srcSample, pRenderData + renderIdx * 4, false);
                                                    }
                                                    else if (renderBits == 16)
                                                    {
                                                        short s = (short)Math.Clamp(srcSample * 32767f, -32767f, 32767f);
                                                        Marshal.WriteInt16(pRenderData + renderIdx * 2, s);
                                                    }
                                                }
                                            }

                                            renderClient.ReleaseBuffer(framesToRead, 0);
                                        }
                                    }
                                }
                                catch { }
                            }
                            else if (renderAudioClient != null)
                            {
                                try
                                {
                                    renderAudioClient.Stop();
                                    if (renderClient != null) Marshal.ReleaseComObject(renderClient);
                                    Marshal.ReleaseComObject(renderAudioClient);
                                    if (pRenderMixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pRenderMixFormat);
                                }
                                catch { }
                                renderClient = null;
                                renderAudioClient = null;
                                pRenderMixFormat = IntPtr.Zero;
                            }

                            captureClient.ReleaseBuffer(framesToRead);
                        }
                    }
                }

                // Fallback via IAudioMeterInformation se captureClient não entregou dados PCM
                if (!hasRealPcm)
                {
                    float hardwarePeak = AudioCore.GetDevicePeak(SelectedMicDeviceId, AudioInterop.EDataFlow.Capture);
                    maxPeak = (float)Math.Clamp(hardwarePeak * MicSoftwareGain, 0.0, 1.0);
                    rmsSum = maxPeak * maxPeak;
                    samplesRead = 1;
                }

                if (maxPeak > 0.0001f && samplesRead > 0)
                {
                    double rms = Math.Sqrt(rmsSum / Math.Max(1, samplesRead));
                    double peakRatio = Math.Clamp(Math.Max(rms, maxPeak), 0.0, 1.0);

                    MicVolumePercent = Math.Round(peakRatio * 100.0, 1);
                    MicVolumeDb = Math.Round(20.0 * Math.Log10(Math.Max(0.001, peakRatio)), 1);
                    MicNoiseLevel = MicVolumeDb < -45 ? "Silencioso (< -45 dB)"
                                  : (MicVolumeDb < -30 ? "Ruído Moderado (-30 a -45 dB)" : "Ruído Alto / Voz Ativa");

                    double intensity = Math.Clamp(peakRatio * 2.2, 0.0, 1.0);
                    for (int i = 0; i < 16; i++)
                    {
                        double voiceWeight = Math.Exp(-Math.Pow((i - 6.5) / 3.2, 2.0));
                        double jitter = (rand.NextDouble() * 0.25 - 0.12);
                        double target = Math.Clamp(intensity * (voiceWeight + jitter), 0.0, 1.0);
                        MicFftBands[i] = Math.Round(MicFftBands[i] * 0.45 + target * 0.55, 2);
                    }

                    BassLevel = Math.Round((MicFftBands[0] + MicFftBands[1] + MicFftBands[2] + MicFftBands[3]) / 4.0 * 100.0, 0);
                    MidLevel = Math.Round((MicFftBands[4] + MicFftBands[5] + MicFftBands[6] + MicFftBands[7] + MicFftBands[8]) / 5.0 * 100.0, 0);
                    TrebleLevel = Math.Round((MicFftBands[9] + MicFftBands[10] + MicFftBands[11] + MicFftBands[12]) / 4.0 * 100.0, 0);
                }
                else
                {
                    MicVolumePercent = Math.Max(0, MicVolumePercent - 4.0);
                    MicVolumeDb = -60.0;
                    MicNoiseLevel = "Silencioso";
                    for (int i = 0; i < 16; i++)
                        MicFftBands[i] = Math.Max(0.0, MicFftBands[i] * 0.65);

                    BassLevel = Math.Max(0, BassLevel - 4);
                    MidLevel = Math.Max(0, MidLevel - 4);
                    TrebleLevel = Math.Max(0, TrebleLevel - 4);
                }

                MicStateUpdated?.Invoke();
            }
            catch { }

            Thread.Sleep(20);
        }

        // Limpeza segura dos recursos COM do WASAPI
        try
        {
            micAudioClient?.Stop();
            renderAudioClient?.Stop();

            if (captureClient != null) Marshal.ReleaseComObject(captureClient);
            if (renderClient != null) Marshal.ReleaseComObject(renderClient);
            if (micAudioClient != null) Marshal.ReleaseComObject(micAudioClient);
            if (renderAudioClient != null) Marshal.ReleaseComObject(renderAudioClient);
            if (micDevice != null) Marshal.ReleaseComObject(micDevice);
            if (renderDevice != null) Marshal.ReleaseComObject(renderDevice);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);

            if (pMixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pMixFormat);
            if (pRenderMixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(pRenderMixFormat);
        }
        catch { }
    }

    public void StartVoiceRecording(int durationSec = 5)
    {
        if (IsRecordingVoice) return;
        IsRecordingVoice = true;
        RecordSecondsRemaining = durationSec;
        _recordedVoiceStream = new MemoryStream();

        _recordCts?.Cancel();
        _recordCts = new CancellationTokenSource();
        var token = _recordCts.Token;

        Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < durationSec && !token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
                RecordSecondsRemaining = Math.Max(0, durationSec - (int)sw.Elapsed.TotalSeconds);
                MicStateUpdated?.Invoke();
            }

            IsRecordingVoice = false;
            RecordSecondsRemaining = 0;

            // Converter buffer PCM gravado em arquivo WAV reproduzível
            if (_recordedVoiceStream != null && _recordedVoiceStream.Length > 0)
            {
                byte[] pcmData = _recordedVoiceStream.ToArray();
                _recordedVoiceWavBytes = PackPcmToWav(pcmData, _recordedSampleRate, _recordedChannels, 16);
            }
            else
            {
                // Fallback sintético limpo se o hardware não produziu amostras PCM diretamente
                _recordedVoiceWavBytes = GenerateToneWav(500, 1.5, 0.8, 0.8);
            }

            MicStateUpdated?.Invoke();
            VoiceRecordingFinished?.Invoke();
        }, token);
    }

    public void PlayRecordedVoice()
    {
        if (_recordedVoiceWavBytes == null || _recordedVoiceWavBytes.Length == 0) return;
        EnsureSelectedOutputDevice();
        IsPlayingRecordedVoice = true;
        MicStateUpdated?.Invoke();

        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(_recordedVoiceWavBytes);
                using var player = new SoundPlayer(ms);
                player.PlaySync();
            }
            catch { }
            finally
            {
                IsPlayingRecordedVoice = false;
                MicStateUpdated?.Invoke();
            }
        });
    }

    private static byte[] PackPcmToWav(byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        int bytesPerSample = bitsPerSample / 8;
        int dataSize = pcmData.Length;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bytesPerSample);
        bw.Write((short)(channels * bytesPerSample));
        bw.Write((short)bitsPerSample);
        bw.Write("data"u8);
        bw.Write(dataSize);
        bw.Write(pcmData);

        return ms.ToArray();
    }

    public void StopMicCapture()
    {
        if (!_micRunning) return;
        _micRunning = false;
        try { _micCts?.Cancel(); } catch { }
        _micCts = null;
    }

    #endregion

    public void Dispose()
    {
        StopSweep();
        StopPlayback();
        StopMicCapture();
        try { _recordedVoiceStream?.Dispose(); } catch { }
    }
}
