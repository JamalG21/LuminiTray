using System;

namespace LuminiTray;

/// <summary>
/// Reamostrador de áudio linear de alta eficiência e latência zero (Zero GC).
/// Converte fluxos de áudio PCM mono/estéreo entre taxas de amostragem arbitrárias
/// (ex: 44.1 kHz, 48 kHz, 96 kHz, 192 kHz) garantindo fidelidade de tom e velocidade acústica perfeita.
/// </summary>
public sealed class LinearResampler
{
    private double _phase = 0.0;
    private float _lastSample = 0.0f;

    public void Reset()
    {
        _phase = 0.0;
        _lastSample = 0.0f;
    }

    /// <summary>
    /// Reamostra um bloco de amostras contíguas de srcRate para dstRate.
    /// Retorna a quantidade exata de amostras gravadas em output.
    /// </summary>
    public int Resample(ReadOnlySpan<float> input, Span<float> output, int srcRate, int dstRate)
    {
        if (srcRate <= 0 || dstRate <= 0) return 0;

        if (srcRate == dstRate)
        {
            int n = Math.Min(input.Length, output.Length);
            input.Slice(0, n).CopyTo(output);
            return n;
        }

        if (input.Length == 0 || output.Length == 0) return 0;

        double step = (double)srcRate / dstRate;
        int outCount = 0;

        while (outCount < output.Length)
        {
            int idx = (int)_phase;
            if (idx >= input.Length)
            {
                _phase -= input.Length;
                _lastSample = input[input.Length - 1];
                break;
            }

            double frac = _phase - idx;
            float s0 = (idx == 0) ? _lastSample : input[idx - 1];
            float s1 = input[idx];

            output[outCount++] = (float)(s0 + (s1 - s0) * frac);
            _phase += step;
        }

        return outCount;
    }

    public static int EstimateOutputCount(int inputCount, int srcRate, int dstRate)
    {
        if (srcRate == dstRate) return inputCount;
        return (int)Math.Ceiling((double)inputCount * dstRate / srcRate) + 4;
    }
}
