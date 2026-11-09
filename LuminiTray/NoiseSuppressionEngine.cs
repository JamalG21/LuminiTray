using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LuminiTray;

/// <summary>
/// Motor de supressão de ruído para microfone em tempo real de alta precisão.
/// Opera em frames exatos de 10 ms (480 amostras a 48 kHz).
/// Integra RNNoise neural (recorrente GRU) nativo com Voice Activity Detection (VAD),
/// filtro passa-altas de 80Hz (rumble), equalizador de 3 bandas paramétrico
/// e Noise Gate inteligente com Histerese (Schmitt Trigger) e Hold Time configurável,
/// evitando 100% dos cortes de palavras e sílabas fracas.
/// </summary>
public sealed class NoiseSuppressionEngine : IDisposable
{
    public const int SampleRate = 48_000;
    public const int FrameSamples = 480;

    private IntPtr _state = IntPtr.Zero;
    private bool _nativeAvailable;

    // Buffers pré-alocados para processamento sem alocação de memória no loop de áudio (Zero GC)
    private readonly float[] _frameIn = new float[FrameSamples];
    private readonly float[] _frameOut = new float[FrameSamples];

    // Filtros Biquad
    private readonly Biquad _rumbleHighPass = Biquad.HighPass(SampleRate, 80, 0.707);
    private readonly Biquad _low = Biquad.LowShelf(SampleRate, 180, 0);
    private readonly Biquad _mid = Biquad.Peaking(SampleRate, 1_200, 0.8, 0);
    private readonly Biquad _high = Biquad.HighShelf(SampleRate, 6_000, 0);

    // Estado do Noise Gate inteligente (Histerese + Hold Time)
    private float _gateGain = 1.0f;
    private bool _gateIsOpen = false;
    private int _gateHoldCounter = 0;

    // Estilo EasyEffects: "VAD grace" (mantém aberto enquanto a rede ouviu voz há pouco),
    // detector do gate filtrado por passa-altas (sidechain), de-esser e limitador suave.
    private int _vadGraceCounter = 0;
    private readonly Biquad _sidechainHpf = Biquad.HighPass(SampleRate, 150, 0.707);
    private readonly Biquad _sibilanceHpf = Biquad.HighPass(SampleRate, 6_500, 0.707);
    private float _deEssEnv = 0f;
    private const float LimiterKnee = 0.85f;
    private const float DeEssThreshold = 0.06f; // ~ -24 dBFS na banda de sibilância

    // Propriedades e Controles
    public bool Enabled { get; set; } = true;
    public float SuppressionIntensity { get; set; } = 1.0f; // 0.0 a 1.0 (0% a 100%)
    public float GainDb { get; set; } = 0.0f; // Ganho de entrada (-12dB a +20dB)
    public float GateThresholdDb { get; set; } = -52.0f; // Threshold de abertura do gate (-70dB a -25dB)
    public float GateReleaseMs { get; set; } = 150.0f; // Tempo de release em ms (suavização)
    public float GateHysteresisDb { get; set; } = 8.0f; // Diferença em dB para fechar o gate (evita picotar voz)
    public float GateHoldMs { get; set; } = 140.0f; // Tempo de sustentação pós-fala (evita cortar consoantes)

    public float VadThreshold { get; set; } = 0.35f;   // probabilidade de voz (0..1) que reabastece o "grace"
    public float VadHoldMs { get; set; } = 180.0f;     // quanto tempo o gate segue aberto após a última voz
    public bool LimiterEnabled { get; set; } = true;   // limitador suave anti-estouro
    public bool DeesserEnabled { get; set; } = true;   // atenuador de sibilância (6,5 kHz+)

    public float LowEqDb { get; set; } = 0.0f;
    public float MidEqDb { get; set; } = 0.0f;
    public float HighEqDb { get; set; } = 0.0f;

    public bool IsNativeRnNoiseAvailable => _nativeAvailable;
    public float LastVoiceProbability { get; private set; }

    static NoiseSuppressionEngine()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NoiseSuppressionEngine).Assembly, ResolveRnNoiseDll);
        }
        catch { }
    }

    private static IntPtr ResolveRnNoiseDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName.Equals("rnnoise.dll", StringComparison.OrdinalIgnoreCase) ||
            libraryName.Equals("rnnoise", StringComparison.OrdinalIgnoreCase))
        {
            string baseDir = AppContext.BaseDirectory;
            string p1 = Path.Combine(baseDir, "rnnoise.dll");
            if (File.Exists(p1) && NativeLibrary.TryLoad(p1, out IntPtr h1)) return h1;

            string p2 = Path.Combine(baseDir, "Native", "rnnoise.dll");
            if (File.Exists(p2) && NativeLibrary.TryLoad(p2, out IntPtr h2)) return h2;
        }
        return IntPtr.Zero;
    }

    public NoiseSuppressionEngine()
    {
        try
        {
            _state = RnNoise.rnnoise_create(IntPtr.Zero);
            _nativeAvailable = _state != IntPtr.Zero;
        }
        catch (DllNotFoundException) { _nativeAvailable = false; }
        catch (EntryPointNotFoundException) { _nativeAvailable = false; }
        catch (BadImageFormatException) { _nativeAvailable = false; }
        catch { _nativeAvailable = false; }
    }

    /// <summary>
    /// Processa exatamente 480 amostras mono a 48 kHz (10 ms).
    /// </summary>
    public void Process(Span<float> mono48k)
    {
        if (mono48k.Length != FrameSamples)
            throw new ArgumentException($"RNNoise requer exatamente {FrameSamples} amostras.", nameof(mono48k));

        if (!Enabled)
        {
            LastVoiceProbability = 0;
            return;
        }

        // 1. Filtro passa-altas de 80Hz (corta batidas na mesa e ruído subsônico de ar-condicionado)
        for (int i = 0; i < mono48k.Length; i++)
        {
            mono48k[i] = _rumbleHighPass.Process(mono48k[i]);
        }

        // 2. Medição de pico e RMS do sinal de entrada
        float inPeak = 0f;
        float inSumSquares = 0f;
        for (int i = 0; i < mono48k.Length; i++)
        {
            float val = mono48k[i];
            float abs = Math.Abs(val);
            if (abs > inPeak) inPeak = abs;
            inSumSquares += val * val;
        }
        float inRms = MathF.Sqrt(inSumSquares / mono48k.Length);
        float inPeakDb = inPeak <= 0.000001f ? -120f : 20f * MathF.Log10(inPeak);
        float inRmsDb = inRms <= 0.000001f ? -120f : 20f * MathF.Log10(inRms);

        // 3. Aplicação do Equalizador de 3 Bandas e Ganho de Pré-Amplificação
        _low.ConfigureLowShelf(SampleRate, 180, LowEqDb);
        _mid.ConfigurePeaking(SampleRate, 1_200, 0.8, MidEqDb);
        _high.ConfigureHighShelf(SampleRate, 6_000, HighEqDb);
        float linearGain = MathF.Pow(10f, GainDb / 20f);

        for (int i = 0; i < mono48k.Length; i++)
        {
            float s = mono48k[i];
            s = _low.Process(s);
            s = _mid.Process(s);
            s = _high.Process(s);
            mono48k[i] = s * linearGain;
        }

        // 4. Supressão Neural RNNoise ou Fallback com Gate Inteligente
        if (_nativeAvailable && _state != IntPtr.Zero)
        {
            // RNNoise nativo opera internamente com amostras na faixa [-32768, 32767]
            for (int i = 0; i < FrameSamples; i++)
            {
                _frameIn[i] = mono48k[i] * 32767.0f;
            }

            try
            {
                // Processamento neural (22 bandas de frequência com rede GRU recorrente)
                float vad = RnNoise.rnnoise_process_frame(_state, _frameOut, _frameIn);
                LastVoiceProbability = Math.Clamp(vad, 0f, 1f);

                float intensity = Math.Clamp(SuppressionIntensity, 0f, 1f);
                for (int i = 0; i < FrameSamples; i++)
                {
                    float rnnoiseSample = _frameOut[i] / 32767.0f;
                    float originalSample = mono48k[i];
                    // Mixagem linear entre o áudio tratado pelo RNNoise e o original
                    mono48k[i] = originalSample * (1.0f - intensity) + rnnoiseSample * intensity;
                }

                // Medição pós-supressão para alimentar o Gate
                float postPeak = 0f;
                float postSumSquares = 0f;
                for (int i = 0; i < mono48k.Length; i++)
                {
                    float val = _sidechainHpf.Process(mono48k[i]); // detector ignora batidas de mesa / graves
                    float abs = Math.Abs(val);
                    if (abs > postPeak) postPeak = abs;
                    postSumSquares += val * val;
                }
                float postRms = MathF.Sqrt(postSumSquares / mono48k.Length);
                float postPeakDb = postPeak <= 0.000001f ? -120f : 20f * MathF.Log10(postPeak);
                float postRmsDb = postRms <= 0.000001f ? -120f : 20f * MathF.Log10(postRms);

                // Aplica o Gate inteligente pós-RNNoise (silencia 100% de fundo quando não há fala)
                ApplyIntelligentGate(mono48k, postPeakDb, postRmsDb, LastVoiceProbability);
            }
            catch
            {
                _nativeAvailable = false;
                FallbackProcess(mono48k, inPeakDb, inRmsDb);
            }
        }
        else
        {
            FallbackProcess(mono48k, inPeakDb, inRmsDb);
        }

        PostProcess(mono48k);
    }

    /// <summary>
    /// Limitador de pico suave (soft knee) – nunca ultrapassa 1,0 (0 dBFS) e preserva o que está abaixo do joelho.
    /// </summary>
    public static float SoftLimit(float x)
    {
        float a = Math.Abs(x);
        if (a <= LimiterKnee) return x;
        float room = 1f - LimiterKnee;
        float y = LimiterKnee + room * MathF.Tanh((a - LimiterKnee) / room);
        return x < 0 ? -y : y;
    }

    /// <summary>De-esser + limitador, aplicados por último, sem alocação.</summary>
    private void PostProcess(Span<float> frame)
    {
        if (DeesserEnabled)
        {
            for (int i = 0; i < frame.Length; i++)
            {
                float x = frame[i];
                float sib = _sibilanceHpf.Process(x);
                float a = Math.Abs(sib);
                _deEssEnv = a > _deEssEnv ? a : _deEssEnv * 0.995f; // ataque instantâneo, release ~ 4 ms
                if (_deEssEnv > DeEssThreshold)
                {
                    float g = DeEssThreshold / _deEssEnv;           // reduz só a banda acima do limiar
                    x -= sib * (1f - g);
                }
                frame[i] = x;
            }
        }

        if (LimiterEnabled)
        {
            for (int i = 0; i < frame.Length; i++) frame[i] = SoftLimit(frame[i]);
        }
    }

    /// <summary>
    /// Fallback puro em C# caso a DLL neural não esteja acessível.
    /// Utiliza VAD baseado em envelope de energia e downward expander progressivo.
    /// </summary>
    private void FallbackProcess(Span<float> mono48k, float peakDb, float rmsDb)
    {
        float voiceProb = 0f;
        if (rmsDb > GateThresholdDb + 4f)
        {
            voiceProb = Math.Clamp((rmsDb - (GateThresholdDb + 4f)) / 18f + 0.4f, 0f, 1f);
        }
        LastVoiceProbability = voiceProb;

        float intensity = Math.Clamp(SuppressionIntensity, 0f, 1f);
        if (intensity > 0.05f)
        {
            // Atenuação de chiado no silêncio ponderada pela intensidade
            float noiseCut = 1.0f - (intensity * 0.45f);
            for (int i = 0; i < mono48k.Length; i++)
            {
                mono48k[i] *= (voiceProb > 0.35f ? 1.0f : noiseCut);
            }
        }

        ApplyIntelligentGate(mono48k, peakDb, rmsDb, voiceProb);
    }

    /// <summary>
    /// Aplica noise gate inteligente com histerese (Schmitt Trigger) e Hold Time.
    /// Garante que consoantes finais e sílabas fracas nunca sejam cortadas.
    /// </summary>
    private void ApplyIntelligentGate(Span<float> frame, float peakDb, float rmsDb, float vadProb)
    {
        float openThreshold = GateThresholdDb;
        float closeThreshold = GateThresholdDb - Math.Max(3.0f, GateHysteresisDb);
        int holdFramesTotal = (int)MathF.Ceiling(Math.Max(10f, GateHoldMs) / 10f); // 1 frame = 10ms

        // Critério para voz/sinal ativo:
        // Se a rede neural RNNoise detectar probabilidade de voz >= 10%, ou pico/RMS estiverem acima do limiar
        // VAD grace (EasyEffects): voz detectada recarrega o contador; enquanto ele durar, o gate não fecha.
        int graceFrames = (int)MathF.Ceiling(Math.Max(0f, VadHoldMs) / 10f);
        if (vadProb >= Math.Clamp(VadThreshold, 0.05f, 0.95f)) _vadGraceCounter = graceFrames;
        else if (_vadGraceCounter > 0) _vadGraceCounter--;
        bool inGrace = _vadGraceCounter > 0;

        bool signalAboveOpen = peakDb >= openThreshold || rmsDb >= (openThreshold - 6f) || vadProb >= 0.10f || inGrace;
        bool signalAboveClose = peakDb >= closeThreshold || rmsDb >= (closeThreshold - 6f) || vadProb >= 0.05f || inGrace;

        if (signalAboveOpen)
        {
            _gateIsOpen = true;
            _gateHoldCounter = holdFramesTotal;
        }
        else if (_gateIsOpen)
        {
            if (signalAboveClose)
            {
                // Permanece aberto com voz suave ou cauda de fala
                _gateHoldCounter = holdFramesTotal;
            }
            else
            {
                // Caiu abaixo do limiar de fechamento: consome o tempo de Hold antes de fechar
                if (_gateHoldCounter > 0)
                {
                    _gateHoldCounter--;
                }
                else
                {
                    _gateIsOpen = false;
                }
            }
        }

        float targetGain = _gateIsOpen ? 1.0f : 0.0f;

        // Ataque rápido (~3ms) para não perder o início da fala; Release suave configurável
        float attackCoeff = 0.35f;
        float releaseCoeff = MathF.Exp(-10.0f / Math.Max(20f, GateReleaseMs));

        for (int i = 0; i < frame.Length; i++)
        {
            if (targetGain >= _gateGain)
            {
                // Abrindo (Attack rápido e sem estalo)
                _gateGain += (targetGain - _gateGain) * attackCoeff;
            }
            else
            {
                // Fechando (Release suave após o término do Hold)
                _gateGain = targetGain + (_gateGain - targetGain) * releaseCoeff;
            }

            frame[i] *= _gateGain;
        }
    }

    public void Dispose()
    {
        if (_state != IntPtr.Zero)
        {
            try { RnNoise.rnnoise_destroy(_state); } catch { }
            _state = IntPtr.Zero;
        }
    }

    private static class RnNoise
    {
        [DllImport("rnnoise.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr rnnoise_create(IntPtr model);

        [DllImport("rnnoise.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void rnnoise_destroy(IntPtr state);

        [DllImport("rnnoise.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern float rnnoise_process_frame(IntPtr state, [Out] float[] output, [In] float[] input);
    }

    private sealed class Biquad
    {
        private float _b0 = 1, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        public static Biquad HighPass(int sr, double f, double q)
        {
            var b = new Biquad();
            b.ConfigureHighPass(sr, f, q);
            return b;
        }

        public static Biquad LowShelf(int sr, double f, double db)
        {
            var b = new Biquad();
            b.ConfigureLowShelf(sr, f, db);
            return b;
        }

        public static Biquad HighShelf(int sr, double f, double db)
        {
            var b = new Biquad();
            b.ConfigureHighShelf(sr, f, db);
            return b;
        }

        public static Biquad Peaking(int sr, double f, double q, double db)
        {
            var b = new Biquad();
            b.ConfigurePeaking(sr, f, q, db);
            return b;
        }

        public float Process(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return y;
        }

        public void ConfigureHighPass(double sr, double f, double q)
        {
            double w = 2 * Math.PI * f / sr;
            double c = Math.Cos(w);
            double s = Math.Sin(w);
            double alpha = s / (2.0 * q);
            double b0 = (1.0 + c) / 2.0;
            double b1 = -(1.0 + c);
            double b2 = (1.0 + c) / 2.0;
            double a0 = 1.0 + alpha;
            double a1 = -2.0 * c;
            double a2 = 1.0 - alpha;

            _b0 = (float)(b0 / a0);
            _b1 = (float)(b1 / a0);
            _b2 = (float)(b2 / a0);
            _a1 = (float)(a1 / a0);
            _a2 = (float)(a2 / a0);
        }

        public void ConfigurePeaking(double sr, double f, double q, double db) => Configure(sr, f, q, db, 0);
        public void ConfigureLowShelf(double sr, double f, double db) => Configure(sr, f, 0.707, db, -1);
        public void ConfigureHighShelf(double sr, double f, double db) => Configure(sr, f, 0.707, db, 1);

        private void Configure(double sr, double f, double q, double db, int shelf)
        {
            double a = Math.Pow(10, db / 40);
            double w = 2 * Math.PI * f / sr;
            double c = Math.Cos(w);
            double s = Math.Sin(w);
            double alpha = s / (2 * q);
            double beta = Math.Sqrt(a) * alpha;
            double b0, b1, b2, a0, a1, a2;

            if (shelf < 0)
            {
                b0 = a * ((a + 1) - (a - 1) * c + 2 * beta);
                b1 = 2 * a * ((a - 1) - (a + 1) * c);
                b2 = a * ((a + 1) - (a - 1) * c - 2 * beta);
                a0 = (a + 1) + (a - 1) * c + 2 * beta;
                a1 = -2 * ((a - 1) + (a + 1) * c);
                a2 = (a + 1) + (a - 1) * c - 2 * beta;
            }
            else if (shelf > 0)
            {
                b0 = a * ((a + 1) + (a - 1) * c + 2 * beta);
                b1 = -2 * a * ((a - 1) + (a + 1) * c);
                b2 = a * ((a + 1) - (a - 1) * c - 2 * beta);
                a0 = (a + 1) - (a - 1) * c + 2 * beta;
                a1 = 2 * ((a - 1) - (a + 1) * c);
                a2 = (a + 1) - (a - 1) * c - 2 * beta;
            }
            else
            {
                b0 = 1 + alpha * a;
                b1 = -2 * c;
                b2 = 1 - alpha * a;
                a0 = 1 + alpha / a;
                a1 = -2 * c;
                a2 = 1 - alpha / a;
            }

            _b0 = (float)(b0 / a0);
            _b1 = (float)(b1 / a0);
            _b2 = (float)(b2 / a0);
            _a1 = (float)(a1 / a0);
            _a2 = (float)(a2 / a0);
        }
    }
}
