using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class WindowsDebloatTests
{
    [TestMethod]
    public void TestCatalog_LoadsAndParses162Apps()
    {
        var catalog = WindowsDebloatService.LoadCatalog();

        Assert.IsNotNull(catalog, "O catálogo de bloatwares não deve ser nulo.");
        Assert.AreEqual(162, catalog.Count, "O catálogo deve conter exatamente os 162 identificadores de Win11Debloat.");

        var clipchamp = catalog.FirstOrDefault(a => a.AppId == "Clipchamp.Clipchamp");
        Assert.IsNotNull(clipchamp, "Clipchamp deve estar no catálogo.");
        Assert.IsTrue(clipchamp.FriendlyName.Contains("Clipchamp", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("safe", clipchamp.Recommendation.ToLowerInvariant());

        var weather = catalog.FirstOrDefault(a => a.AppId == "Microsoft.BingWeather");
        Assert.IsNotNull(weather, "BingWeather deve estar no catálogo.");
        Assert.IsTrue(weather.FriendlyName.Contains("Clima", StringComparison.OrdinalIgnoreCase) ||
                      weather.FriendlyName.Contains("Weather", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TestCatalog_OemDetection()
    {
        // Validação de identificadores de fabricantes OEM
        Assert.IsTrue(WindowsDebloatService.DetectIsOem("DellInc.DellSupportAssistforPCs"));
        Assert.AreEqual("Dell", WindowsDebloatService.DetectVendor("DellInc.DellSupportAssistforPCs", true));

        Assert.IsTrue(WindowsDebloatService.DetectIsOem("AD2F1837.HPSupportAssistant"));
        Assert.AreEqual("HP", WindowsDebloatService.DetectVendor("AD2F1837.HPSupportAssistant", true));

        Assert.IsTrue(WindowsDebloatService.DetectIsOem("LenovoCompanyLimited.LenovoVantageService"));
        Assert.AreEqual("Lenovo", WindowsDebloatService.DetectVendor("LenovoCompanyLimited.LenovoVantageService", true));

        Assert.IsTrue(WindowsDebloatService.DetectIsOem("B9ECED6F.ASUSPCAssistant"));
        Assert.AreEqual("ASUS", WindowsDebloatService.DetectVendor("B9ECED6F.ASUSPCAssistant", true));

        Assert.IsTrue(WindowsDebloatService.DetectIsOem("AcerIncorporated.AcerCareCenterS"));
        Assert.AreEqual("Acer", WindowsDebloatService.DetectVendor("AcerIncorporated.AcerCareCenterS", true));

        Assert.IsTrue(WindowsDebloatService.DetectIsOem("SAMSUNGELECTRONICSCO.LTD.SmartThingsWindows"));
        Assert.AreEqual("Samsung", WindowsDebloatService.DetectVendor("SAMSUNGELECTRONICSCO.LTD.SmartThingsWindows", true));

        Assert.IsTrue(WindowsDebloatService.DetectIsOem("9426MICRO-STARINTERNATION.MSICenter"));
        Assert.AreEqual("MSI", WindowsDebloatService.DetectVendor("9426MICRO-STARINTERNATION.MSICenter", true));

        // Softwares comuns não devem ser classificados como OEM
        Assert.IsFalse(WindowsDebloatService.DetectIsOem("Clipchamp.Clipchamp"));
        Assert.IsFalse(WindowsDebloatService.DetectIsOem("Microsoft.BingNews"));
        Assert.AreEqual("Microsoft", WindowsDebloatService.DetectVendor("Microsoft.BingNews", false));
    }

    [TestMethod]
    public void TestCatalog_RecommendationBadges()
    {
        var safeItem = new DebloatAppItem { Recommendation = "safe", IsOem = false };
        Assert.AreEqual("SEGURO", safeItem.RecommendationBadge);
        Assert.AreEqual("#10B981", safeItem.BadgeColor);

        var oemItem = new DebloatAppItem { Recommendation = "optional", IsOem = true };
        Assert.AreEqual("BLOATWARE OEM", oemItem.RecommendationBadge);
        Assert.AreEqual("#3B82F6", oemItem.BadgeColor);

        var optionalItem = new DebloatAppItem { Recommendation = "optional", IsOem = false };
        Assert.AreEqual("OPCIONAL", optionalItem.RecommendationBadge);
        Assert.AreEqual("#F59E0B", optionalItem.BadgeColor);

        var unsafeItem = new DebloatAppItem { Recommendation = "unsafe", IsOem = false };
        Assert.AreEqual("AVANÇADO", unsafeItem.RecommendationBadge);
        Assert.AreEqual("#EF4444", unsafeItem.BadgeColor);
    }

    [TestMethod]
    public void TestHardwareProfile_IntelHybridHighRam()
    {
        var profile = WindowsDebloatService.CalculateProfile(
            "13th Gen Intel(R) Core(TM) i7-13700K",
            cores: 16,
            threads: 24,
            gpuName: "NVIDIA GeForce RTX 4070 Ti",
            ramGb: 32.0);

        Assert.IsTrue(profile.IsIntelHybrid, "i7-13700K deve ser identificado como arquitetura híbrida (P+E cores)");
        Assert.IsFalse(profile.IsAmdRyzen);
        Assert.IsTrue(profile.IsNvidiaGpu, "RTX 4070 deve ser identificada como GPU NVIDIA");
        Assert.IsTrue(profile.HasHighRam, "32 GB de RAM deve ser classificado como High RAM (>= 16 GB)");

        Assert.IsTrue(profile.RecommendedTweakIds.Contains("cpu_system_responsiveness"));
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("cpu_game_priority"));
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("cpu_power_throttling"));
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("gpu_hags"));
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("gpu_frame_latency"));
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("sys_disable_paging_executive"), "Sistemas com >= 16 GB devem ter DisablePagingExecutive recomendado");
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("sys_telemetry"));
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("sys_copilot"));
    }

    [TestMethod]
    public void TestHardwareProfile_AmdRyzenLowRam()
    {
        var profile = WindowsDebloatService.CalculateProfile(
            "AMD Ryzen 5 5600 6-Core Processor",
            cores: 6,
            threads: 12,
            gpuName: "AMD Radeon RX 6600",
            ramGb: 8.0);

        Assert.IsTrue(profile.IsAmdRyzen, "Ryzen 5 5600 deve ser identificado como processador AMD Ryzen");
        Assert.IsFalse(profile.IsIntelHybrid);
        Assert.IsTrue(profile.IsAmdGpu, "RX 6600 deve ser identificada como GPU AMD");
        Assert.IsFalse(profile.HasHighRam, "8 GB de RAM deve ser classificado como HasHighRam = false");

        Assert.IsTrue(profile.RecommendedTweakIds.Contains("cpu_system_responsiveness"));
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("gpu_hags"));
        Assert.IsFalse(profile.RecommendedTweakIds.Contains("sys_disable_paging_executive"), "Sistemas com <= 8 GB NÃO devem forçar DisablePagingExecutive para evitar estouro de memória");
    }

    [TestMethod]
    public void TestTweaks_AllTweaksMetadata()
    {
        var tweaks = WindowsDebloatService.GetAllTweaks();

        Assert.IsTrue(tweaks.Count >= 15, "Deve haver ao menos 15 ajustes categorizados");

        var ids = new HashSet<string>();
        foreach (var t in tweaks)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Id), "Id não pode ser vazio");
            Assert.IsTrue(ids.Add(t.Id), $"Id duplicado encontrado: {t.Id}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Title), $"Título vazio no tweak {t.Id}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Description), $"Descrição vazia no tweak {t.Id}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Category), $"Categoria vazia no tweak {t.Id}");
            Assert.IsNotNull(t.Read, $"Delegate Read nulo no tweak {t.Id}");
            Assert.IsNotNull(t.Write, $"Delegate Write nulo no tweak {t.Id}");
            Assert.IsNotNull(t.Restore, $"Delegate Restore nulo no tweak {t.Id}");
        }

        Assert.IsTrue(tweaks.Any(t => t.Category.Contains("CPU")), "Deve conter categoria CPU");
        Assert.IsTrue(tweaks.Any(t => t.Category.Contains("GPU")), "Deve conter categoria GPU");
        Assert.IsTrue(tweaks.Any(t => t.Category.Contains("Latência")), "Deve conter categoria Latência");
        Assert.IsTrue(tweaks.Any(t => t.Category.Contains("Telemetria")), "Deve conter categoria Telemetria / Sistema");
    }

    [TestMethod]
    public void TestTweaks_ReversibilityContract()
    {
        var settings = new AppSettings();
        Assert.AreEqual(0, settings.TweakBackup.Count);

        // Testa reversibilidade real com um tweak de HKCU (não requer privilégios de Admin em testes)
        var tweaks = WindowsDebloatService.GetAllTweaks();
        var frameLatency = tweaks.FirstOrDefault(t => t.Id == "gpu_frame_latency");
        Assert.IsNotNull(frameLatency);

        // Chamada Write guarda estado prévio em settings.TweakBackup e aplica o valor
        frameLatency.Write(true, settings);
        Assert.IsTrue(settings.TweakBackup.ContainsKey("gpu_frame_latency"), "Backup do valor original de gpu_frame_latency deve ser criado na primeira alteração");

        // Chamada Restore reverte o valor e restaura estado
        frameLatency.Restore(settings);
    }

    [TestMethod]
    public void TestCatalog_CorruptedOrEmptyJson_ReturnsEmptyGracefully()
    {
        var empty = WindowsDebloatService.ParseCatalog("", "");
        Assert.AreEqual(0, empty.Count, "JSON vazio deve retornar lista vazia sem lançar exceção");

        var corrupted = WindowsDebloatService.ParseCatalog("{ invalid-json ", "{ broken");
        Assert.AreEqual(0, corrupted.Count, "JSON corrompido deve ser tratado com segurança");

        var missingFields = WindowsDebloatService.ParseCatalog(
            "{\"Apps\": [{\"AppId\": \"App.Sem.Descricao\"}, {\"AppId\": null}]}",
            "");
        Assert.AreEqual(1, missingFields.Count, "Deve tolerar campos ausentes e descartar entradas inválidas");
        Assert.AreEqual("App.Sem.Descricao", missingFields[0].AppId);
    }

    [TestMethod]
    public void TestHardwareProfile_IntelLegacy_NotHybrid()
    {
        // Core i7-7700K: 4C/8T -> não é híbrido
        var profile = WindowsDebloatService.CalculateProfile(
            "Intel(R) Core(TM) i7-7700K CPU @ 4.20GHz",
            cores: 4,
            threads: 8,
            gpuName: "Intel(R) HD Graphics 630",
            ramGb: 16.0);

        Assert.IsFalse(profile.IsIntelHybrid, "Processadores anteriores à 12ª geração sem P/E cores não são híbridos");
        Assert.IsFalse(profile.IsAmdRyzen);
        Assert.IsTrue(profile.IsIntelGpu);
        Assert.IsTrue(profile.HasHighRam);
    }

    [TestMethod]
    public void TestHardwareProfile_IntelUltra_IsHybrid()
    {
        // Core Ultra 7 155H: 16C/22T
        var profile = WindowsDebloatService.CalculateProfile(
            "Intel(R) Core(TM) Ultra 7 155H",
            cores: 16,
            threads: 22,
            gpuName: "Intel(R) Arc(TM) Graphics",
            ramGb: 32.0);

        Assert.IsTrue(profile.IsIntelHybrid, "Intel Core Ultra deve ser reconhecido como híbrido");
        Assert.IsTrue(profile.IsIntelGpu);
        Assert.IsTrue(profile.HasHighRam);
    }

    [TestMethod]
    public void TestHardwareProfile_BoundaryValues()
    {
        // Valores nulos ou limite (0 RAM, 0 núcleos) não devem falhar
        var profile = WindowsDebloatService.CalculateProfile(
            "",
            cores: 0,
            threads: 0,
            gpuName: "",
            ramGb: 0.0);

        Assert.IsNotNull(profile);
        Assert.IsFalse(profile.HasHighRam);
        Assert.IsFalse(profile.IsIntelHybrid);
        Assert.IsFalse(profile.IsAmdRyzen);
        Assert.IsTrue(profile.RecommendedTweakIds.Count > 0, "Deve sugerir ao menos os ajustes universais");
    }

    [TestMethod]
    public void TestHardwareProfile_IntelXeon12_NotHybrid()
    {
        // Xeon com número "12" no nome: não deve ser falso positivo de Intel Híbrido
        var profile = WindowsDebloatService.CalculateProfile(
            "Intel(R) Xeon(R) CPU E5-2612 v2 @ 2.40GHz",
            cores: 8,
            threads: 16,
            gpuName: "NVIDIA Quadro K2200",
            ramGb: 64.0);

        Assert.IsFalse(profile.IsIntelHybrid, "Xeon E5-2612 v2 possui '12' no nome mas não é arquitetura híbrida (8C/16T simétrico)");
        Assert.IsTrue(profile.IsNvidiaGpu);
        Assert.IsTrue(profile.HasHighRam);
    }

    [TestMethod]
    public void TestHardwareProfile_ApuReservedRam_IsHighRam()
    {
        // Notebooks de 16 GB com APU/iGPU normalmente têm ~15.2 GB de memória visível
        var profile = WindowsDebloatService.CalculateProfile(
            "AMD Ryzen 7 5700U with Radeon Graphics",
            cores: 8,
            threads: 16,
            gpuName: "AMD Radeon(TM) Graphics",
            ramGb: 15.2);

        Assert.IsTrue(profile.HasHighRam, "15.2 GB de RAM visível em máquina de 16 GB com VRAM compartilhada deve ser High RAM");
        Assert.IsTrue(profile.IsAmdRyzen);
        Assert.IsTrue(profile.IsAmdGpu);
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("sys_disable_paging_executive"));
    }

    [TestMethod]
    public void TestHardwareProfile_IntelArcGpu()
    {
        var profile = WindowsDebloatService.CalculateProfile(
            "Intel(R) Core(TM) i5-13400F",
            cores: 10,
            threads: 16,
            gpuName: "Intel(R) Arc(TM) A770 Graphics",
            ramGb: 32.0);

        Assert.IsTrue(profile.IsIntelHybrid);
        Assert.IsTrue(profile.IsIntelGpu, "Intel Arc A770 deve ser reconhecido como Intel GPU");
        Assert.IsTrue(profile.RecommendedTweakIds.Contains("gpu_hags"));
    }

    [TestMethod]
    public void TestTweaks_RevertAllPreservesNonDebloatBackups()
    {
        var settings = new AppSettings();
        // Simula chaves de backup criadas por outras abas (RESET OTIMIZA)
        settings.TweakBackup["fso"] = "1";
        settings.TweakBackup["power"] = "some-guid";
        settings.TweakBackup["custom_tweak"] = "test";

        // Adiciona chave de debloat
        settings.TweakBackup["cpu_system_responsiveness"] = "20";
        settings.TweakBackup["sys_telemetry"] = "1";

        var tweaks = WindowsDebloatService.GetAllTweaks();

        // Executa reversão de debloat síncrona
        WindowsDebloatService.RevertAllTweaks(settings, tweaks);

        // Chaves de debloat devem ser removidas
        Assert.IsFalse(settings.TweakBackup.ContainsKey("cpu_system_responsiveness"));
        Assert.IsFalse(settings.TweakBackup.ContainsKey("sys_telemetry"));

        // Chaves de outras telas DEVEM ser preservadas intactas
        Assert.IsTrue(settings.TweakBackup.ContainsKey("fso"), "Backup da tela RESET OTIMIZA (fso) deve ser preservado");
        Assert.IsTrue(settings.TweakBackup.ContainsKey("power"), "Backup de energia pré-existente deve ser preservado");
        Assert.IsTrue(settings.TweakBackup.ContainsKey("custom_tweak"), "Backups personalizados não devem ser apagados");
    }

    [TestMethod]
    public void TestTweaks_ClassicMenu_RestoreWhenOriginallyEnabled()
    {
        var settings = new AppSettings();
        // Usuário já tinha o menu clássico ("1") antes
        settings.TweakBackup["sys_classic_menu"] = "1";

        var tweaks = WindowsDebloatService.GetAllTweaks();
        var classicTweak = tweaks.FirstOrDefault(t => t.Id == "sys_classic_menu");
        Assert.IsNotNull(classicTweak);

        // Deve executar sem lançar exceção
        classicTweak.Restore(settings);
    }
}
