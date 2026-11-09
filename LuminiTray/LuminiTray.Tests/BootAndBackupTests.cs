using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class BootAndBackupTests
{
    [TestMethod]
    public void BootRecord_Formatting_IsAccurate()
    {
        var record = new BootRecord
        {
            Timestamp = new DateTime(2026, 11, 4, 15, 30, 0),
            TotalSeconds = 42.0,
            MainPathSeconds = 18.2,
            PostBootSeconds = 23.8,
            AppsStartedCount = 8
        };

        Assert.AreEqual("04/11 15:30", record.FormattedDate);
        Assert.AreEqual("42 s", record.FormattedTotal);
        StringAssert.Contains(record.FormattedBreakdown, "18 s");
        StringAssert.Contains(record.FormattedBreakdown, "24 s");
    }

    [TestMethod]
    public void BootRecord_LongBoot_FormatsMinutesCorrectly()
    {
        var record = new BootRecord
        {
            Timestamp = DateTime.Now,
            TotalSeconds = 135.0,
            MainPathSeconds = 60.0,
            PostBootSeconds = 75.0
        };

        Assert.AreEqual("2 min 15 s", record.FormattedTotal);
    }

    [TestMethod]
    public void BootDegrader_IconsAndDelays_MatchTypes()
    {
        var appDegrader = new BootDegrader
        {
            Name = "Steam",
            Type = "Programa",
            DelaySeconds = 8.4,
            LastOccurrence = DateTime.Today
        };

        Assert.AreEqual("📦", appDegrader.Icon);
        StringAssert.Contains(appDegrader.FormattedDelay, "8");
        StringAssert.Contains(appDegrader.FormattedSubtitle, "Programa");

        var driverDegrader = new BootDegrader
        {
            Name = "nvlddmkm.sys",
            Type = "Driver",
            DelaySeconds = 4.2,
            LastOccurrence = DateTime.Today
        };

        Assert.AreEqual("⚙️", driverDegrader.Icon);
        StringAssert.Contains(driverDegrader.FormattedDelay, "4");
        StringAssert.Contains(driverDegrader.FormattedSubtitle, "Driver");

        var serviceDegrader = new BootDegrader
        {
            Name = "Spooler",
            Type = "Serviço",
            DelaySeconds = 2.1,
            LastOccurrence = DateTime.Today
        };

        Assert.AreEqual("🔧", serviceDegrader.Icon);
        StringAssert.Contains(serviceDegrader.FormattedDelay, "2");
    }

    [TestMethod]
    public void BootDiagnosticsService_AnalyzeBootPerformance_ReturnsValidResult()
    {
        var result = BootDiagnosticsService.AnalyzeBootPerformance();
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Timeline);
        Assert.IsNotNull(result.Degraders);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.FastStartupNote));
    }

    [TestMethod]
    public void StartupEntry_RecommendationBadges_AreAssignedCorrectly()
    {
        var entryDiscord = new StartupEntry
        {
            Name = "Discord",
            Command = "C:\\Users\\User\\AppData\\Local\\Discord\\app.exe",
            Source = "Registry (HKCU)"
        };

        Assert.AreEqual("Seguro de desligar", entryDiscord.RecommendationBadge);
        Assert.AreEqual("#10B981", entryDiscord.RecommendationColor);

        var entryAudio = new StartupEntry
        {
            Name = "Realtek Audio Service",
            Command = "C:\\Program Files\\Realtek\\RtkAud.exe",
            Source = "Registry (HKLM)"
        };

        Assert.AreEqual("Melhor deixar ligado", entryAudio.RecommendationBadge);
        Assert.AreEqual("#3B82F6", entryAudio.RecommendationColor);

        var entryPeripheral = new StartupEntry
        {
            Name = "Logitech G HUB",
            Command = "C:\\Program Files\\LGHUB\\lghub.exe",
            Source = "Registry (HKLM)"
        };

        Assert.AreEqual("Confira antes", entryPeripheral.RecommendationBadge);
        Assert.AreEqual("#F59E0B", entryPeripheral.RecommendationColor);
    }

    [TestMethod]
    public void WiFiNetworkProfile_Model_PropertiesAndFormatting()
    {
        var profile = new WiFiNetworkProfile
        {
            Ssid = "MinhaRedeCasa_5G",
            Password = "SenhaSuperSecreta123",
            Authentication = "WPA2PSK"
        };

        Assert.AreEqual("MinhaRedeCasa_5G", profile.Ssid);
        Assert.AreEqual("SenhaSuperSecreta123", profile.Password);
        Assert.AreEqual("📶", profile.Icon);
        StringAssert.Contains(profile.Subtitle, "WPA2PSK");
        StringAssert.Contains(profile.Subtitle, "SenhaSuperSecreta123");
    }

    [TestMethod]
    public void UpgradableAppItem_PropertyChanged_NotifiesProperly()
    {
        var app = new UpgradableAppItem
        {
            Name = "OBS Studio",
            Id = "OBSProject.OBSStudio",
            InstalledVersion = "30.0.0",
            AvailableVersion = "30.2.0",
            Source = "winget"
        };

        bool propChanged = false;
        string changedProp = "";
        app.PropertyChanged += (s, e) =>
        {
            propChanged = true;
            changedProp = e.PropertyName ?? "";
        };

        app.Selected = false;
        Assert.IsTrue(propChanged);
        Assert.AreEqual("Selected", changedProp);

        propChanged = false;
        app.Status = "Atualizando...";
        Assert.IsTrue(propChanged);
        Assert.AreEqual("Status", changedProp);
    }
}
