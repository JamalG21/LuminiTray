using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class EasyEffectsSuppressorTests
{
    [TestMethod]
    public void SoftLimit_NeverExceedsFullScale()
    {
        foreach (float x in new[] { 0.9f, 1.0f, 1.5f, 4f, 100f, -1.5f, -100f })
            Assert.IsTrue(Math.Abs(NoiseSuppressionEngine.SoftLimit(x)) <= 1.0f, $"SoftLimit({x}) passou de 1.0");
    }

    [TestMethod]
    public void SoftLimit_KeepsQuietSignalUntouched()
    {
        foreach (float x in new[] { 0f, 0.1f, -0.5f, 0.85f })
            Assert.AreEqual(x, NoiseSuppressionEngine.SoftLimit(x), 1e-6f);
    }

    [TestMethod]
    public void SoftLimit_IsMonotonic()
    {
        float prev = NoiseSuppressionEngine.SoftLimit(0f);
        for (float x = 0.01f; x < 3f; x += 0.01f)
        {
            float y = NoiseSuppressionEngine.SoftLimit(x);
            Assert.IsTrue(y >= prev - 1e-6f, $"Não monotônico em {x}");
            prev = y;
        }
    }

    [TestMethod]
    public void Engine_LoudInput_StaysWithinFullScale()
    {
        using var eng = new NoiseSuppressionEngine { LimiterEnabled = true, SuppressionIntensity = 0f, GainDb = 20f };
        var frame = new float[NoiseSuppressionEngine.FrameSamples];
        for (int n = 0; n < 20; n++)
        {
            for (int i = 0; i < frame.Length; i++) frame[i] = 0.9f * MathF.Sin(2 * MathF.PI * 440 * (n * frame.Length + i) / NoiseSuppressionEngine.SampleRate);
            eng.Process(frame);
            foreach (var s in frame) Assert.IsTrue(Math.Abs(s) <= 1.0f);
        }
    }

    [TestMethod]
    public void Engine_VadGrace_KeepsGateOpenBrieflyAfterSignal()
    {
        using var eng = new NoiseSuppressionEngine { VadHoldMs = 400f, GateThresholdDb = -40f, SuppressionIntensity = 0f };
        var frame = new float[NoiseSuppressionEngine.FrameSamples];
        // fala sintética forte
        for (int n = 0; n < 30; n++)
        {
            for (int i = 0; i < frame.Length; i++) frame[i] = 0.3f * MathF.Sin(2 * MathF.PI * 300 * (n * frame.Length + i) / NoiseSuppressionEngine.SampleRate);
            eng.Process(frame);
        }
        // silêncio logo depois: nos primeiros 100 ms o gate ainda não deve ter fechado totalmente
        float tail = 0;
        for (int n = 0; n < 10; n++)
        {
            Array.Clear(frame);
            for (int i = 0; i < frame.Length; i++) frame[i] = 0.002f; // sussurro bem baixo, abaixo do threshold
            eng.Process(frame);
            tail += frame.Max(Math.Abs);
        }
        Assert.IsTrue(tail > 0f, "O gate fechou instantaneamente, sem sustentação de VAD");
    }

    [TestMethod]
    public void OverlayMetrics_ContainsRamOptions()
    {
        var keys = OverlayMetrics.All.Select(m => m.Key).ToList();
        CollectionAssert.Contains(keys, "proc_ram");
        CollectionAssert.Contains(keys, "app_ram");
        Assert.IsFalse(OverlayMetrics.Defaults().Contains("proc_ram"));
        Assert.IsFalse(OverlayMetrics.Defaults().Contains("app_ram"));
    }

    [TestMethod]
    public void AppSettings_SuppressorEasyEffectsDefaults()
    {
        var s = new AppSettings();
        Assert.IsTrue(s.SuppressorLimiterEnabled);
        Assert.IsTrue(s.SuppressorDeesserEnabled);
        Assert.IsTrue(s.SuppressorVadHoldMs > 0);
        Assert.IsTrue(s.SuppressorVadThreshold > 0 && s.SuppressorVadThreshold < 1);
    }
}
