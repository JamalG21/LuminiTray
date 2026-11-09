using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LuminiTray;

public enum KeySoundType
{
    Switch, // Som de Switch Mecânico (Click + Thock)
    Bubble  // Som de Bolha d'Água (Bubble Pop / Water Droplet)
}

/// <summary>
/// Motor de feedback sonoro de baixa latência (< 5ms) via WASAPI (Core Audio).
/// Mantém uma sessão de áudio ativa para o Lumini Tray no Mixer de Volume do Windows,
/// permitindo controle independente de volume e suporte a polifonia em digitação rápida.
/// </summary>
public static class KeySoundEngine
{
    private static readonly object _lock = new();
    private static bool _initialized;
    private static bool _disposed;
    private static Thread? _renderThread;

    // Buffers PCM Float normalizados (-1.0 a +1.0) mono
    private static float[]? _switchSamples;
    private static float[]? _bubbleSamples;
    private static byte[]? _switchWavBytes;
    private static byte[]? _bubbleWavBytes;

    // Estado da sessão WASAPI
    private static AudioInterop.IAudioClient? _audioClient;
    private static AudioInterop.IAudioRenderClient? _renderClient;
    private static uint _bufferFrameCount;
    private static int _sampleRate = 48000;
    private static int _channels = 2;
    private static bool _isFloat = true;
    private static bool _useWasapi;

    // Vozes ativas para mixagem simultânea (polifonia)
    private sealed class Voice
    {
        public float[] Samples = Array.Empty<float>();
        public int Position;
        public float Volume;
    }

    private static readonly List<Voice> _activeVoices = new(16);

    [DllImport("winmm.dll", EntryPoint = "PlaySound", SetLastError = true)]
    private static extern bool PlaySoundNative(byte[] ptrToSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_MEMORY = 0x0004;

    /// <summary>
    /// Garante que o motor de som e a sessão do Windows Volume Mixer estejam inicializados.
    /// </summary>
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                LoadOrSynthesizeAudio();
                InitWasapiSession();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KeySoundEngine] Falha ao iniciar WASAPI, usando fallback: {ex.Message}");
                _useWasapi = false;
            }
        }
    }

    /// <summary>
    /// Toca o som de tecla selecionado (Switch ou Bolha) com baixíssima latência.
    /// </summary>
    public static void Play(KeySoundType type)
    {
        if (!_initialized)
        {
            Initialize();
        }

        float[]? samples = type == KeySoundType.Bubble ? _bubbleSamples : _switchSamples;
        byte[]? wavBytes = type == KeySoundType.Bubble ? _bubbleWavBytes : _switchWavBytes;

        if (samples == null || samples.Length == 0) return;

        if (_useWasapi && _renderClient != null)
        {
            lock (_activeVoices)
            {
                // Limite de 12 vozes simultâneas para evitar sobrecarga em testes extremos de rollover
                if (_activeVoices.Count >= 12)
                {
                    _activeVoices.RemoveAt(0);
                }

                _activeVoices.Add(new Voice
                {
                    Samples = samples,
                    Position = 0,
                    Volume = 0.85f
                });
            }
        }
        else if (wavBytes != null && wavBytes.Length > 0)
        {
            // Fallback nativo rápido WinMM
            try
            {
                PlaySoundNative(wavBytes, IntPtr.Zero, SND_ASYNC | SND_MEMORY);
            }
            catch { }
        }
    }

    /// <summary>
    /// Encerra com segurança o motor de áudio e a thread WASAPI.
    /// </summary>
    public static void Shutdown()
    {
        lock (_lock)
        {
            _disposed = true;
            try { _audioClient?.Stop(); } catch { }
        }
    }

    private static void LoadOrSynthesizeAudio()
    {
        string assetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Audio");
        string switchPath = Path.Combine(assetsDir, "switch.wav");
        string bubblePath = Path.Combine(assetsDir, "bubble.wav");

        // 1. Som de Switch Mecânico
        if (File.Exists(switchPath))
        {
            try
            {
                _switchWavBytes = File.ReadAllBytes(switchPath);
                _switchSamples = DecodeWavToFloat(_switchWavBytes, out _);
            }
            catch { }
        }

        if (_switchSamples == null || _switchSamples.Length == 0)
        {
            _switchSamples = SynthesizeSwitchSamples(_sampleRate);
            _switchWavBytes = EncodeFloatToWav(_switchSamples, _sampleRate);

            try
            {
                Directory.CreateDirectory(assetsDir);
                File.WriteAllBytes(switchPath, _switchWavBytes);
            }
            catch { }
        }

        // 2. Som de Bolha d'Água
        if (File.Exists(bubblePath))
        {
            try
            {
                _bubbleWavBytes = File.ReadAllBytes(bubblePath);
                _bubbleSamples = DecodeWavToFloat(_bubbleWavBytes, out _);
            }
            catch { }
        }

        if (_bubbleSamples == null || _bubbleSamples.Length == 0)
        {
            _bubbleSamples = SynthesizeBubbleSamples(_sampleRate);
            _bubbleWavBytes = EncodeFloatToWav(_bubbleSamples, _sampleRate);

            try
            {
                Directory.CreateDirectory(assetsDir);
                File.WriteAllBytes(bubblePath, _bubbleWavBytes);
            }
            catch { }
        }
    }

    private static void InitWasapiSession()
    {
        AudioInterop.IMMDeviceEnumerator? enumerator = null;
        AudioInterop.IMMDevice? defaultDev = null;
        IntPtr pMixFormat = IntPtr.Zero;

        try
        {
            enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, AudioInterop.ERole.Console, out defaultDev) != 0 || defaultDev == null)
            {
                _useWasapi = false;
                return;
            }

            Guid iidClient = new Guid(AudioInterop.IID_IAudioClient);
            if (defaultDev.Activate(ref iidClient, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var clientObj) != 0 || clientObj is not AudioInterop.IAudioClient client)
            {
                _useWasapi = false;
                return;
            }

            _audioClient = client;

            if (_audioClient.GetMixFormat(out pMixFormat) != 0 || pMixFormat == IntPtr.Zero)
            {
                _useWasapi = false;
                return;
            }

            ushort wFormatTag = (ushort)Marshal.ReadInt16(pMixFormat, 0);
            _channels = Marshal.ReadInt16(pMixFormat, 2);
            _sampleRate = Marshal.ReadInt32(pMixFormat, 4);
            ushort bitsPerSample = (ushort)Marshal.ReadInt16(pMixFormat, 14);

            _isFloat = wFormatTag == 3 || bitsPerSample == 32;

            Guid sessionGuid = Guid.Empty;
            // Buffer de 60ms para latência mínima e estabilidade contra buffer underrun
            long bufferDurationHns = 600000; // 60ms em unidades de 100ns

            if (_audioClient.Initialize(0 /* AUDCLNT_SHAREMODE_SHARED */, 0, bufferDurationHns, 0, pMixFormat, ref sessionGuid) != 0)
            {
                _useWasapi = false;
                return;
            }

            if (_audioClient.GetBufferSize(out _bufferFrameCount) != 0)
            {
                _useWasapi = false;
                return;
            }

            Guid iidRender = new Guid(AudioInterop.IID_IAudioRenderClient);
            if (_audioClient.GetService(ref iidRender, out var rObj) != 0 || rObj is not AudioInterop.IAudioRenderClient renderClient)
            {
                _useWasapi = false;
                return;
            }

            _renderClient = renderClient;

            // Registrar o nome amigável "Lumini Tray" na sessão do Windows Volume Mixer
            try
            {
                Guid iidSession = new Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD");
                if (_audioClient.GetService(ref iidSession, out var sObj) == 0 && sObj is AudioInterop.IAudioSessionControl sessionCtrl)
                {
                    Guid eventCtx = Guid.Empty;
                    sessionCtrl.SetDisplayName("Lumini Tray", ref eventCtx);
                    string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        sessionCtrl.SetIconPath(exePath, ref eventCtx);
                    }
                }
            }
            catch { }

            // Pré-carregar buffer inicial com silêncio para registrar a sessão como ativa
            if (_renderClient.GetBuffer(_bufferFrameCount, out IntPtr initialBuffer) == 0)
            {
                _renderClient.ReleaseBuffer(_bufferFrameCount, 2 /* AUDCLNT_BUFFERFLAGS_SILENT */);
            }

            _audioClient.Start();
            _useWasapi = true;

            // Iniciar thread dedicada de renderização com prioridade alta
            _renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "LuminiTray_AudioMixerThread",
                Priority = ThreadPriority.AboveNormal
            };
            _renderThread.Start();
        }
        catch
        {
            _useWasapi = false;
        }
        finally
        {
            if (pMixFormat != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pMixFormat);
            }
            if (defaultDev != null)
            {
                Marshal.ReleaseComObject(defaultDev);
            }
            if (enumerator != null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    private static void RenderLoop()
    {
        while (!_disposed && _useWasapi && _audioClient != null && _renderClient != null)
        {
            try
            {
                if (_audioClient.GetCurrentPadding(out uint paddingFrames) != 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                uint framesNeeded = _bufferFrameCount > paddingFrames ? _bufferFrameCount - paddingFrames : 0;
                if (framesNeeded == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                if (_renderClient.GetBuffer(framesNeeded, out IntPtr pData) != 0 || pData == IntPtr.Zero)
                {
                    Thread.Sleep(5);
                    continue;
                }

                bool hasAudio = false;

                lock (_activeVoices)
                {
                    if (_activeVoices.Count > 0)
                    {
                        hasAudio = true;
                        MixVoicesToBuffer(pData, (int)framesNeeded);
                    }
                }

                if (hasAudio)
                {
                    _renderClient.ReleaseBuffer(framesNeeded, 0);
                }
                else
                {
                    // Libera buffer silencioso de forma ultraleve sem operações de memória
                    _renderClient.ReleaseBuffer(framesNeeded, 2 /* AUDCLNT_BUFFERFLAGS_SILENT */);
                }

                Thread.Sleep(6);
            }
            catch
            {
                Thread.Sleep(10);
            }
        }
    }

    private static unsafe void MixVoicesToBuffer(IntPtr pData, int frameCount)
    {
        int ch = _channels <= 0 ? 2 : _channels;

        if (_isFloat)
        {
            float* buffer = (float*)pData.ToPointer();
            // Zera o buffer antes da mixagem
            for (int i = 0; i < frameCount * ch; i++)
            {
                buffer[i] = 0f;
            }

            for (int v = _activeVoices.Count - 1; v >= 0; v--)
            {
                var voice = _activeVoices[v];
                int framesToMix = Math.Min(frameCount, voice.Samples.Length - voice.Position);

                for (int f = 0; f < framesToMix; f++)
                {
                    float s = voice.Samples[voice.Position + f] * voice.Volume;
                    int bufIdx = f * ch;
                    for (int c = 0; c < ch; c++)
                    {
                        buffer[bufIdx + c] += s;
                    }
                }

                voice.Position += framesToMix;
                if (voice.Position >= voice.Samples.Length)
                {
                    _activeVoices.RemoveAt(v);
                }
            }

            // Clamping para evitar distorção
            for (int i = 0; i < frameCount * ch; i++)
            {
                if (buffer[i] > 1.0f) buffer[i] = 1.0f;
                else if (buffer[i] < -1.0f) buffer[i] = -1.0f;
            }
        }
        else
        {
            short* buffer = (short*)pData.ToPointer();
            for (int i = 0; i < frameCount * ch; i++)
            {
                buffer[i] = 0;
            }

            for (int v = _activeVoices.Count - 1; v >= 0; v--)
            {
                var voice = _activeVoices[v];
                int framesToMix = Math.Min(frameCount, voice.Samples.Length - voice.Position);

                for (int f = 0; f < framesToMix; f++)
                {
                    float s = voice.Samples[voice.Position + f] * voice.Volume;
                    short val = (short)(Math.Clamp(s, -1.0f, 1.0f) * 32767);
                    int bufIdx = f * ch;
                    for (int c = 0; c < ch; c++)
                    {
                        int mixed = buffer[bufIdx + c] + val;
                        buffer[bufIdx + c] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
                    }
                }

                voice.Position += framesToMix;
                if (voice.Position >= voice.Samples.Length)
                {
                    _activeVoices.RemoveAt(v);
                }
            }
        }
    }

    /// <summary>
    /// Síntese acústica de alta precisão para Switch Mecânico (Click tátil + Ressonância acústica 'Thock').
    /// </summary>
    public static float[] SynthesizeSwitchSamples(int sampleRate = 48000)
    {
        int length = (int)(sampleRate * 0.050); // 50ms de duração total
        float[] samples = new float[length];
        var rand = new Random(1337);

        for (int i = 0; i < length; i++)
        {
            double t = (double)i / sampleRate;

            // 1. Click seco do contato/folha de metal (3200Hz - 4200Hz com decaimento súbito de 3.5ms)
            double clickEnv = Math.Exp(-t / 0.0035);
            double clickNoise = (rand.NextDouble() * 2.0 - 1.0) * 0.40;
            double clickSine = Math.Sin(2.0 * Math.PI * 3600.0 * t);
            double click = (clickSine * 0.6 + clickNoise) * clickEnv;

            // 2. Ressonância mecânica do stem/haste (2100Hz amortecido, 10ms)
            double stemEnv = Math.Exp(-t / 0.008);
            double stem = Math.Sin(2.0 * Math.PI * 2150.0 * t) * stemEnv * 0.35;

            // 3. Batida de fim de curso na carcaça do teclado - 'Thock' (420Hz + harmônico 840Hz, 30ms)
            double thockEnv = Math.Exp(-t / 0.015);
            double thock = (Math.Sin(2.0 * Math.PI * 440.0 * t) * 0.75 + Math.Sin(2.0 * Math.PI * 880.0 * t) * 0.25) * thockEnv * 0.60;

            samples[i] = (float)Math.Clamp((click * 0.55 + stem * 0.35 + thock * 0.70) * 0.85, -1.0, 1.0);
        }

        return samples;
    }

    /// <summary>
    /// Síntese acústica orgânica de Bolha d'Água (Frequency sweep ascendente com envelope suave).
    /// </summary>
    public static float[] SynthesizeBubbleSamples(int sampleRate = 48000)
    {
        int length = (int)(sampleRate * 0.080); // 80ms de duração
        float[] samples = new float[length];

        for (int i = 0; i < length; i++)
        {
            double t = (double)i / sampleRate;

            // Variação de frequência da bolha: sobe de 420Hz até 1420Hz de forma natural
            double phase = 2.0 * Math.PI * (420.0 * t + 1000.0 * (0.080 / 1.7) * Math.Pow(t / 0.080, 1.7));

            // Envelope: ataque rápido de 2ms e decaimento exponencial orgânico
            double env;
            if (t < 0.002)
                env = t / 0.002;
            else
                env = Math.Exp(-(t - 0.002) / 0.024);

            // Fundamental senoidal pura + 15% de segundo harmônico para textura líquida
            double sig = Math.Sin(phase) * 0.85 + Math.Sin(phase * 2.0) * 0.15;
            samples[i] = (float)Math.Clamp(sig * env * 0.90, -1.0, 1.0);
        }

        return samples;
    }

    private static float[] DecodeWavToFloat(byte[] wavBytes, out int sampleRate)
    {
        using var ms = new MemoryStream(wavBytes);
        using var br = new BinaryReader(ms);

        string riff = new string(br.ReadChars(4));
        br.ReadInt32(); // fileSize
        string wave = new string(br.ReadChars(4));
        if (riff != "RIFF" || wave != "WAVE")
        {
            sampleRate = 48000;
            return Array.Empty<float>();
        }

        short audioFormat = 1;
        short channels = 1;
        int rate = 44100;
        short bitsPerSample = 16;
        int dataSize = 0;

        while (ms.Position < ms.Length)
        {
            string chunkId = new string(br.ReadChars(4));
            int chunkSize = br.ReadInt32();

            if (chunkId == "fmt ")
            {
                audioFormat = br.ReadInt16();
                channels = br.ReadInt16();
                rate = br.ReadInt32();
                br.ReadInt32(); // byteRate
                br.ReadInt16(); // blockAlign
                bitsPerSample = br.ReadInt16();
                if (chunkSize > 16) br.ReadBytes(chunkSize - 16);
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
                break;
            }
            else
            {
                br.ReadBytes(chunkSize);
            }
        }

        sampleRate = rate;
        int bytesPerSample = bitsPerSample / 8;
        int totalSamples = dataSize / bytesPerSample;
        int frames = totalSamples / channels;
        float[] output = new float[frames];

        for (int i = 0; i < frames; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                if (audioFormat == 3 && bitsPerSample == 32)
                {
                    sum += br.ReadSingle();
                }
                else if (bitsPerSample == 16)
                {
                    sum += br.ReadInt16() / 32768.0f;
                }
                else if (bitsPerSample == 8)
                {
                    sum += (br.ReadByte() - 128) / 128.0f;
                }
            }
            output[i] = sum / channels;
        }

        return output;
    }

    private static byte[] EncodeFloatToWav(float[] samples, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int channels = 1;
        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels * (bitsPerSample / 8);
        int subChunk2Size = samples.Length * (bitsPerSample / 8);

        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + subChunk2Size);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);

        // data chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(subChunk2Size);
        for (int i = 0; i < samples.Length; i++)
        {
            short val = (short)(Math.Clamp(samples[i], -1.0f, 1.0f) * 32767);
            bw.Write(val);
        }

        return ms.ToArray();
    }
}
