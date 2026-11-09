using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class BackupAndPresetTests
{
    [TestMethod]
    public void TestFanPresets_SaveAndRestore()
    {
        var settings = new FanControlSaveSettings();
        var custom = new FanCurveSaveConfig
        {
            Name = "Silencioso Verão",
            TempSource = "Cpu",
            RampUpSeconds = 3.5,
            RampDownSeconds = 9.0,
            Points = new List<FanCurvePointSave>
            {
                new(25, 15),
                new(40, 25),
                new(55, 38),
                new(70, 55),
                new(80, 80),
                new(90, 100)
            }
        };

        settings.SavedCpuPresets.Add(custom);

        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<FanControlSaveSettings>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(1, restored.SavedCpuPresets.Count);
        Assert.AreEqual("Silencioso Verão", restored.SavedCpuPresets[0].Name);
        Assert.AreEqual(3.5, restored.SavedCpuPresets[0].RampUpSeconds);
        Assert.AreEqual(6, restored.SavedCpuPresets[0].Points.Count);
        Assert.AreEqual(15.0, restored.SavedCpuPresets[0].Points[0].Pwm);
    }

    [TestMethod]
    public void TestBackupService_DefaultUserFolders()
    {
        var folders = BackupService.GetDefaultUserFolders();
        Assert.IsTrue(folders.Count >= 3, "Deve listar ao menos Desktop, Downloads e Pictures");

        var desktop = folders.FirstOrDefault(f => f.Id == "Desktop");
        Assert.IsNotNull(desktop, "Desktop deve estar presente");
        Assert.IsTrue(desktop.IsSelected, "Desktop deve vir selecionado por padrão");

        var downloads = folders.FirstOrDefault(f => f.Id == "Downloads");
        Assert.IsNotNull(downloads, "Downloads deve estar presente");

        var pictures = folders.FirstOrDefault(f => f.Id == "Pictures");
        Assert.IsNotNull(pictures, "Pictures deve estar presente");
    }

    [TestMethod]
    public async Task TestBackupService_WingetScriptGeneration()
    {
        var apps = new List<InstalledSoftwareItem>
        {
            new() { DisplayName = "Google Chrome", Publisher = "Google LLC" },
            new() { DisplayName = "Discord", Publisher = "Discord Inc." },
            new() { DisplayName = "Visual Studio Code", Publisher = "Microsoft Corporation" }
        };

        string tempFile = Path.Combine(Path.GetTempPath(), $"test_winget_{Guid.NewGuid():N}.bat");
        try
        {
            var (success, msg) = await BackupService.GenerateWingetInstallerScriptAsync(apps, tempFile);
            Assert.IsTrue(success, msg);
            Assert.IsTrue(File.Exists(tempFile));

            string content = await File.ReadAllTextAsync(tempFile);
            Assert.IsTrue(content.Contains("winget install --name \"Google Chrome\""));
            Assert.IsTrue(content.Contains("winget install --name \"Discord\""));
            Assert.IsTrue(content.Contains("winget install --name \"Visual Studio Code\""));
            Assert.IsTrue(content.Contains("net session >nul 2>&1"), "Deve conter verificação de elevação");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
