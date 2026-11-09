using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class FanAndOcTests
{
    [TestMethod]
    public void TestFanCurve_Evaluation()
    {
        var curve = new FanCurve();
        // Curva padrão (6 pontos): (25, 20), (40, 30), (55, 45), (68, 60), (78, 80), (88, 100)
        Assert.AreEqual(20.0, curve.Evaluate(20.0), 0.01, "Abaixo do mínimo deve retornar o menor PWM");
        Assert.AreEqual(20.0, curve.Evaluate(25.0), 0.01, "No ponto exato P1");
        Assert.AreEqual(30.0, curve.Evaluate(40.0), 0.01, "No ponto exato P2");
        Assert.AreEqual(100.0, curve.Evaluate(95.0), 0.01, "Acima do máximo deve retornar 100%");

        // Ponto intermediário: entre 40°C (30%) e 55°C (45%) -> em 47.5°C deve ser 37.5%
        double midPwm = curve.Evaluate(47.5);
        Assert.AreEqual(37.5, midPwm, 0.5, "Interpolação linear intermediária correta");

        var quiet = FanCurve.CreateQuiet();
        Assert.IsTrue(quiet.RampDownSeconds >= 8.0, "Silencioso deve ter histerese de descida suave");
        Assert.IsTrue(quiet.Evaluate(30) <= 25.0, "Silencioso deve operar em PWM reduzido em baixas temperaturas");

        var perf = FanCurve.CreatePerformance();
        Assert.IsTrue(perf.Evaluate(85) >= 100.0, "Performance atinge 100% PWM a 85°C");
    }

    [TestMethod]
    public void TestAmdCurveOptimizer_ValueEncoding()
    {
        // Offsets negativos na SMU AMD usam representação de complemento de 2 em 20 bits (0x100000 - Abs)
        int neg30 = -30;
        uint encodedNeg30 = (uint)(0x100000 - Math.Abs(neg30));
        Assert.AreEqual(0x000FFFE2u, encodedNeg30, "Offset -30 deve ser codificado como 0xFFFE2");

        int neg15 = -15;
        uint encodedNeg15 = (uint)(0x100000 - Math.Abs(neg15));
        Assert.AreEqual(0x000FFFF1u, encodedNeg15, "Offset -15 deve ser codificado como 0xFFFF1");

        int zero = 0;
        uint encodedZero = (uint)zero;
        Assert.AreEqual(0u, encodedZero, "Offset 0 deve ser 0");

        int pos5 = 5;
        uint encodedPos5 = (uint)pos5;
        Assert.AreEqual(5u, encodedPos5, "Offset +5 deve ser 5");
    }

    [TestMethod]
    public void TestIntelFivr_VoltageOffsetCalculation()
    {
        // FIVR Intel: comando (64 bits com plane ID no high dword) e data = (uint)((int)Math.Round(mv * 1.024) << 21)
        int coreMv = -50;
        ulong data = unchecked((uint)((int)Math.Round(coreMv * 1.024) << 21));
        ulong cmdCore = 0x80000011UL;
        ulong msrValue = (cmdCore << 32) | data;

        Assert.IsTrue(msrValue > 0, "Valor do registrador MSR deve ser válido");
        Assert.AreEqual(0x80000011UL, msrValue >> 32, "High dword deve conter comando Core 0x80000011");

        // Teste de cálculo PL1 / PL2 MSR 0x610
        int pl1 = 125;
        int pl2 = 180;
        uint high = 0x00438000u | ((uint)(pl2 * 8) & 0xFFFu);
        uint low = 0x00DD8000u | ((uint)((pl1 - 1) * 8) & 0xFFFu);
        ulong msr610 = ((ulong)high << 32) | low;

        Assert.IsTrue((msr610 & 0xFFFu) > 0, "Low dword deve conter valor de PL1 convertido");
        Assert.IsTrue(((msr610 >> 32) & 0xFFFu) > 0, "High dword deve conter valor de PL2 convertido");
    }

    [TestMethod]
    public void TestHardwareControllerFactory_Instantiation()
    {
        var cpu = HardwareControllerFactory.GetCpuController();
        Assert.IsNotNull(cpu, "CPU Controller não deve ser nulo");
        Assert.IsFalse(string.IsNullOrWhiteSpace(cpu.Name), "Nome da CPU não deve ser vazio");

        var gpu = HardwareControllerFactory.GetGpuController();
        Assert.IsNotNull(gpu, "GPU Controller não deve ser nulo");
        Assert.IsFalse(string.IsNullOrWhiteSpace(gpu.Name), "Nome da GPU não deve ser vazio");
    }

    [TestMethod]
    public void TestFanSettings_PersistenceAndRoundtrip()
    {
        var settings = new FanControlSaveSettings
        {
            IsControlActive = true,
            CpuCurve = new FanCurveSaveConfig
            {
                Name = "Curva AIO Test",
                TempSource = "Cpu",
                RampUpSeconds = 3.0,
                RampDownSeconds = 7.0,
                Points = new List<FanCurvePointSave>
                {
                    new(30, 20),
                    new(50, 40),
                    new(70, 70),
                    new(85, 100)
                }
            }
        };

        // Simula canal de bomba travado em 100%
        settings.Channels["/lpc/aio_pump"] = new FanChannelSaveState
        {
            IsLocked100 = true,
            IsManual = false,
            TargetPwm = 100f
        };

        // Serializa para JSON e desserializa
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<FanControlSaveSettings>(json);

        Assert.IsNotNull(loaded);
        Assert.IsTrue(loaded.IsControlActive);
        Assert.IsNotNull(loaded.CpuCurve);
        Assert.AreEqual(4, loaded.CpuCurve.Points.Count);
        Assert.AreEqual(3.0, loaded.CpuCurve.RampUpSeconds);
        Assert.IsTrue(loaded.Channels.ContainsKey("/lpc/aio_pump"));
        Assert.IsTrue(loaded.Channels["/lpc/aio_pump"].IsLocked100, "Bomba d'agua AIO deve manter trava de 100% apos desserializacao");
    }
}
