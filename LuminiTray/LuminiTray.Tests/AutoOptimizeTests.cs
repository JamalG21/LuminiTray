using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class AutoOptimizeTests
{
    [DataTestMethod]
    [DataRow("discord", "discord.exe")]
    [DataRow("Discord.exe", "discord.exe")]
    [DataRow("  STEAM.EXE  ", "steam.exe")]
    [DataRow("spotify", "spotify.exe")]
    public void NormalizeProcessName_StandardizesCorrectly(string input, string expected)
    {
        string actual = AutoOptimizeService.NormalizeProcessName(input);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Exclusions_AddAndRemove_WorksCorrectly()
    {
        var settings = new AppSettings();
        settings.AutoOptimizeExclusions.Clear();

        var service = new AutoOptimizeService(settings);
        Assert.AreEqual(0, service.Exclusions.Count);

        bool added1 = service.AddExclusion("discord.exe");
        Assert.IsTrue(added1);
        Assert.AreEqual(1, service.Exclusions.Count);

        // Deduplicação insensível a maiúsculas/minúsculas
        bool addedDuplicate = service.AddExclusion("DISCORD");
        Assert.IsFalse(addedDuplicate);
        Assert.AreEqual(1, service.Exclusions.Count);

        bool added2 = service.AddExclusion("obs64.exe");
        Assert.IsTrue(added2);
        Assert.AreEqual(2, service.Exclusions.Count);

        // Remoção
        bool removed = service.RemoveExclusion("discord");
        Assert.IsTrue(removed);
        Assert.AreEqual(1, service.Exclusions.Count);
        Assert.AreEqual("obs64.exe", service.Exclusions[0]);
    }

    [TestMethod]
    public void Interval_ClampsValuesProperly()
    {
        var settings = new AppSettings();
        var service = new AutoOptimizeService(settings);

        service.SetInterval(0); // Menor que 1
        Assert.AreEqual(1, service.IntervalMinutes);

        service.SetInterval(30);
        Assert.AreEqual(30, service.IntervalMinutes);

        service.SetInterval(5000); // Maior que 1440
        Assert.AreEqual(1440, service.IntervalMinutes);
    }

    [TestMethod]
    public void Whitelist_PreventsProtectedAndExcludedProcessesFromBeingClosed()
    {
        // Garante que processos críticos e processos na whitelist nunca sejam considerados
        var exclusions = new List<string> { "chrome.exe", "discord.exe" };
        var result = Optimizer.CloseUselessProcesses(exclusions);

        // O resultado não deve conter nenhum dos processos protegidos ou excluídos
        foreach (var closed in result.Closed)
        {
            Assert.IsFalse(closed.Contains("chrome", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(closed.Contains("discord", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(closed.Contains("LuminiTray", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(closed.Contains("explorer", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void DebloatCatalog_LoadsFromEmbeddedResourcesSuccessfully()
    {
        // Valida que após a migração para Assets\Debloat os arquivos continuam disponíveis
        var catalog = WindowsDebloatService.LoadCatalog();
        Assert.IsNotNull(catalog, "O catálogo de bloatwares não deve ser nulo.");
        Assert.AreEqual(162, catalog.Count, "O catálogo deve conter exatamente os 162 identificadores de Win11Debloat.");

        // Verifica apps conhecidos
        Assert.IsTrue(catalog.Any(a => a.AppId.Contains("BingWeather", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(catalog.Any(a => a.AppId.Contains("MSTeams", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AppScannerService_NormalizeExe_StandardizesExtensions()
    {
        Assert.AreEqual("discord.exe", AppScannerService.NormalizeExe("discord"));
        Assert.AreEqual("discord.exe", AppScannerService.NormalizeExe("Discord.exe"));
        Assert.AreEqual("chrome.exe", AppScannerService.NormalizeExe("  CHROME.EXE  "));
        Assert.AreEqual("cs2.exe", AppScannerService.NormalizeExe("cs2"));
        Assert.AreEqual("", AppScannerService.NormalizeExe("   "));
    }

    [TestMethod]
    public void AppScannerService_IsProtectedSystemProcess_CorrectlyIdentifiesKernelAndSystem()
    {
        Assert.IsTrue(AppScannerService.IsProtectedSystemProcess("System"));
        Assert.IsTrue(AppScannerService.IsProtectedSystemProcess("dwm.exe"));
        Assert.IsTrue(AppScannerService.IsProtectedSystemProcess("explorer"));
        Assert.IsTrue(AppScannerService.IsProtectedSystemProcess("LuminiTray.exe"));
        Assert.IsTrue(AppScannerService.IsProtectedSystemProcess("svchost"));

        Assert.IsFalse(AppScannerService.IsProtectedSystemProcess("discord.exe"));
        Assert.IsFalse(AppScannerService.IsProtectedSystemProcess("spotify"));
        Assert.IsFalse(AppScannerService.IsProtectedSystemProcess("cs2.exe"));
        Assert.IsFalse(AppScannerService.IsProtectedSystemProcess("obs64.exe"));
    }

    [TestMethod]
    public async System.Threading.Tasks.Task AppScannerService_ScanAllAppsAsync_ExecutesOnDemandAndIdentifiesProcesses()
    {
        var exclusions = new List<string> { "discord.exe", "spotify.exe" };
        var apps = await AppScannerService.ScanAllAppsAsync(exclusions);

        Assert.IsNotNull(apps);
        // Em um sistema Windows em execução, deve haver pelo menos alguns processos ou programas detectados
        Assert.IsTrue(apps.Count >= 1, "A varredura sob demanda deve retornar pelo menos 1 processo/app.");

        // Verifica que todos os itens têm nome de executável válido
        foreach (var app in apps)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.ExeName));
            Assert.IsTrue(app.ExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.DisplayName));
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.CategoryBadge));
        }

        // Verifica marcação de itens já blindados
        var discordItem = apps.FirstOrDefault(x => x.ExeName == "discord.exe");
        if (discordItem != null)
        {
            Assert.IsTrue(discordItem.IsAlreadyExcluded);
            Assert.IsFalse(discordItem.CanSelect);
            Assert.AreEqual(System.Windows.Visibility.Visible, discordItem.AlreadyExcludedVisibility);
        }
    }

    [TestMethod]
    public void AutoOptimizeService_ThreadPoolTimer_StartsAndStopsCleanly()
    {
        var settings = new AppSettings
        {
            AutoOptimizeEnabled = false,
            AutoOptimizeIntervalMinutes = 15
        };

        using var service = new AutoOptimizeService(settings);
        Assert.IsFalse(service.IsEnabled);
        Assert.IsNull(service.NextRunTime);

        service.Start();
        Assert.IsNotNull(service.NextRunTime);
        Assert.IsTrue(service.NextRunTime.Value > DateTime.Now);

        service.Stop();
        Assert.IsNull(service.NextRunTime);
    }

    [TestMethod]
    public void LivelyWallpaperService_DefaultsToDisabled()
    {
        var settings = new AppSettings();
        Assert.IsFalse(settings.LivelyWallpaperEnabled, "Wallpapers animados devem vir desativados por padrão para consumo mínimo.");
        Assert.AreEqual(0, settings.LivelyWallpaperVolume, "Volume padrão deve ser 0% (mudo) para não incomodar o usuário.");

        var service = LivelyWallpaperService.Instance;
        Assert.IsNotNull(service);
        Assert.IsFalse(service.IsEnabled, "Serviço Lively deve iniciar desativado.");
        Assert.IsNotNull(LivelyDesktopCore.Instance, "LivelyDesktopCore deve estar instanciado.");
    }

    [TestMethod]
    public void LivelyWallpaperService_PlaylistAndInterval_WorkCorrectly()
    {
        var service = LivelyWallpaperService.Instance;
        service.SetInterval(20);
        Assert.AreEqual(20, service.IntervalMinutes);

        service.SetInterval(-10);
        Assert.AreEqual(0, service.IntervalMinutes);

        service.SetVolume(75);
        Assert.AreEqual(75, service.Volume);
    }

    [TestMethod]
    public void SystemTrayManager_CreateWallpaperIcon_GeneratesValidHandles()
    {
        IntPtr hIconPlaying = SystemTrayManager.CreateWallpaperIcon(isPaused: false);
        Assert.AreNotEqual(IntPtr.Zero, hIconPlaying);
        SystemTrayManager.DestroyIcon(hIconPlaying);

        IntPtr hIconPaused = SystemTrayManager.CreateWallpaperIcon(isPaused: true);
        Assert.AreNotEqual(IntPtr.Zero, hIconPaused);
        SystemTrayManager.DestroyIcon(hIconPaused);
    }
}