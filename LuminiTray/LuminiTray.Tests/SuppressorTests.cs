using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuminiTray.Tests;

[TestClass]
public class SuppressorTests
{
    [TestMethod]
    public void RingBuffer_WriteAndRead_ReturnsCorrectData()
    {
        var ring = new RingBuffer<float>(100);
        float[] input = new float[] { 1f, 2f, 3f, 4f, 5f };
        int written = ring.Write(input);

        Assert.AreEqual(5, written);
        Assert.AreEqual(5, ring.Count);

        float[] output = new float[5];
        int read = ring.Read(output);

        Assert.AreEqual(5, read);
        Assert.AreEqual(0, ring.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(input[i], output[i], 0.0001f);
        }
    }

    [TestMethod]
    public void RingBuffer_WriteOverwrite_AdvancesReadPointerWhenFull()
    {
        var ring = new RingBuffer<int>(3);
        ring.WriteOverwrite(new int[] { 1, 2, 3 });
        Assert.AreEqual(3, ring.Count);

        // Escreve mais 2 itens, sobrescrevendo os mais antigos
        ring.WriteOverwrite(new int[] { 4, 5 });
        Assert.AreEqual(3, ring.Count);

        int[] output = new int[3];
        int read = ring.Read(output);
        Assert.AreEqual(3, read);
        CollectionAssert.AreEqual(new int[] { 3, 4, 5 }, output);
    }

    [TestMethod]
    public void RingBuffer_Clear_ResetsCount()
    {
        var ring = new RingBuffer<float>(50);
        ring.Write(new float[] { 0.1f, 0.2f, 0.3f });
        Assert.AreEqual(3, ring.Count);

        ring.Clear();
        Assert.AreEqual(0, ring.Count);
    }

    [TestMethod]
    public void Engine_InitializesWithoutCrashing_AndReportsFallbackOrNative()
    {
        using var engine = new NoiseSuppressionEngine();
        Assert.IsNotNull(engine);
        Assert.IsTrue(engine.IsNativeRnNoiseAvailable, "rnnoise.dll nativa deve estar disponível.");
    }

    [TestMethod]
    public void Engine_Process_WhenDisabled_PreservesSamples()
    {
        using var engine = new NoiseSuppressionEngine();
        engine.Enabled = false;

        float[] frame = new float[NoiseSuppressionEngine.FrameSamples];
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = 0.42f;
        }

        engine.Process(frame);

        for (int i = 0; i < frame.Length; i++)
        {
            Assert.AreEqual(0.42f, frame[i], 0.0001f);
        }
    }

    [TestMethod]
    public void Engine_Process_WhenEnabled_ProducesValidOutputs()
    {
        using var engine = new NoiseSuppressionEngine();
        engine.Enabled = true;
        engine.SuppressionIntensity = 1.0f;
        engine.GateThresholdDb = -50f;

        float[] frame = new float[NoiseSuppressionEngine.FrameSamples];
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = (float)Math.Sin(2 * Math.PI * 440 * i / NoiseSuppressionEngine.SampleRate) * 0.2f;
        }

        engine.Process(frame);

        bool hasNonZero = false;
        for (int i = 0; i < frame.Length; i++)
        {
            if (Math.Abs(frame[i]) > 0.0001f) hasNonZero = true;
        }
        Assert.IsTrue(hasNonZero, "Engine deve processar amostras preservando voz.");
    }

    [TestMethod]
    public void LinearResampler_Estimate_CalculatesExpectedSizes()
    {
        int est48to44 = LinearResampler.EstimateOutputCount(480, 48000, 44100);
        Assert.IsTrue(est48to44 >= 441, "Estimativa de 48k para 44.1k deve cobrir as amostras necessárias.");

        int est48to96 = LinearResampler.EstimateOutputCount(480, 48000, 96000);
        Assert.IsTrue(est48to96 >= 960, "Estimativa de 48k para 96k deve cobrir o upsampling.");
    }

    [TestMethod]
    public void LinearResampler_Resample_SameRate_CopiesIdentical()
    {
        var resampler = new LinearResampler();
        float[] src = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        float[] dst = new float[4];

        int written = resampler.Resample(src, dst, 48000, 48000);
        Assert.AreEqual(4, written);
        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual(src[i], dst[i], 0.0001f);
        }
    }

    [TestMethod]
    public void LinearResampler_Resample_48kTo44k_DownsamplesReasonably()
    {
        var resampler = new LinearResampler();
        float[] src = new float[480];
        for (int i = 0; i < src.Length; i++) src[i] = (float)Math.Sin(2 * Math.PI * 1000 * i / 48000);

        float[] dst = new float[500];
        int written = resampler.Resample(src, dst, 48000, 44100);

        Assert.IsTrue(written >= 440 && written <= 442, $"Esperado ~441 amostras, obtido {written}.");
    }

    [TestMethod]
    public void VirtualCableManager_IsCableDevice_DetectsProperly()
    {
        Assert.IsTrue(VirtualCableManager.IsCableDevice("CABLE Input (VB-Audio Virtual Cable)"));
        Assert.IsTrue(VirtualCableManager.IsCableDevice("CABLE Output (VB-Audio Virtual Cable)"));
        Assert.IsTrue(VirtualCableManager.IsCableDevice("VB-Audio Point"));
        Assert.IsTrue(VirtualCableManager.IsCableDevice("Virtual Audio Cable 1"));
        Assert.IsFalse(VirtualCableManager.IsCableDevice("Realtek High Definition Audio"));
        Assert.IsFalse(VirtualCableManager.IsCableDevice(null));
    }

    [TestMethod]
    public void Service_QuickTest_TransitionsStateCleanly()
    {
        var svc = MicrophoneSuppressorService.Instance;
        Assert.AreEqual(SuppressorQuickTestState.Idle, svc.TestState);

        svc.StartQuickTest(3);
        Assert.AreEqual(SuppressorQuickTestState.Recording, svc.TestState);

        svc.StopQuickTest();
        Assert.AreEqual(SuppressorQuickTestState.Idle, svc.TestState);
    }

    [TestMethod]
    public void Service_MonitorMode_TogglesCorrectly()
    {
        var svc = MicrophoneSuppressorService.Instance;
        svc.MonitorMode = SuppressorMonitorMode.Processed;
        Assert.AreEqual(SuppressorMonitorMode.Processed, svc.MonitorMode);

        svc.MonitorMode = SuppressorMonitorMode.Raw;
        Assert.AreEqual(SuppressorMonitorMode.Raw, svc.MonitorMode);
    }

    [TestMethod]
    public void Service_MonitoringIsolation_DisabledByDefault_AndTogglesCleanly()
    {
        var svc = MicrophoneSuppressorService.Instance;
        svc.IsMonitoringEnabled = false;
        Assert.IsFalse(svc.IsMonitoringEnabled, "Ouvir retorno de voz deve estar desativado quando a flag é falsa.");

        svc.IsMonitoringEnabled = true;
        Assert.IsTrue(svc.IsMonitoringEnabled, "Ouvir retorno de voz deve ativar apenas sob comando explícito.");
        svc.IsMonitoringEnabled = false;
    }

    [TestMethod]
    public void VirtualCableManager_RejectsPhysicalHeadphonesAndSpeakers()
    {
        Assert.IsFalse(VirtualCableManager.IsCableDevice("Alto-falantes (Realtek High Definition Audio)"));
        Assert.IsFalse(VirtualCableManager.IsCableDevice("Headphones (Realtek(R) Audio)"));
        Assert.IsFalse(VirtualCableManager.IsCableDevice("Speakers (USB Audio Device)"));
        Assert.IsFalse(VirtualCableManager.IsCableDevice("Fone de Ouvido (Bluetooth Audio)"));
        Assert.IsFalse(VirtualCableManager.IsCableDevice("Nvidia High Definition Audio"));
    }

    [TestMethod]
    public void VirtualCableManager_EnsureCableInputEnabled_ActivatesVirtualCable()
    {
        bool success = VirtualCableManager.EnsureCableInputEnabled();
        Assert.IsTrue(success, "EnsureCableInputEnabled deve garantir que o cabo virtual esteja ativo.");
        var cable = VirtualCableManager.GetCableInputDevice();
        Assert.IsNotNull(cable);
        Assert.IsTrue(cable.IsEnabled, "CABLE Input deve estar habilitado.");
    }
}
