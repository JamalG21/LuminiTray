using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuminiTray.Tests;

[TestClass]
public class UninstallerRevampTests
{
    [TestMethod]
    public void TestDetectEngineAndQuietCommand_Msi()
    {
        var item = new InstalledSoftwareItem
        {
            DisplayName = "Test MSI App",
            UninstallString = "MsiExec.exe /I{12345678-ABCD-EF01-2345-6789ABCDEF01}",
            IsMsi = true
        };

        AppUninstaller.DetectEngineAndQuietCommand(item);

        Assert.AreEqual(UninstallerEngineType.Msi, item.EngineType);
        Assert.IsTrue(item.CanSilentUninstall);
        Assert.AreEqual("msiexec.exe /x {12345678-ABCD-EF01-2345-6789ABCDEF01} /qn /norestart", item.GeneratedQuietCommand);
    }

    [TestMethod]
    public void TestDetectEngineAndQuietCommand_Nsis()
    {
        var item = new InstalledSoftwareItem
        {
            DisplayName = "Test NSIS App",
            UninstallString = @"C:\Program Files\MyNsisApp\uninstall.exe"
        };

        AppUninstaller.DetectEngineAndQuietCommand(item);

        Assert.AreEqual(UninstallerEngineType.Nsis, item.EngineType);
        Assert.IsTrue(item.CanSilentUninstall);
        Assert.AreEqual("\"C:\\Program Files\\MyNsisApp\\uninstall.exe\" /S", item.GeneratedQuietCommand);
    }

    [TestMethod]
    public void TestDetectEngineAndQuietCommand_StoreApp()
    {
        var item = new InstalledSoftwareItem
        {
            DisplayName = "Spotify",
            PackageFullName = "SpotifyAB.SpotifyMusic_1.2.3.4_x64__zpdnekdrzrea0",
            Category = AppCategory.MicrosoftStore
        };

        AppUninstaller.DetectEngineAndQuietCommand(item);

        Assert.AreEqual(UninstallerEngineType.StoreApp, item.EngineType);
        Assert.IsTrue(item.CanSilentUninstall);
        Assert.IsTrue(item.GeneratedQuietCommand.Contains("Remove-AppxPackage"));
        Assert.IsTrue(item.GeneratedQuietCommand.Contains("SpotifyAB.SpotifyMusic"));
    }

    [TestMethod]
    public void TestDetectEngineAndQuietCommand_Steam()
    {
        var item = new InstalledSoftwareItem
        {
            DisplayName = "Counter-Strike 2",
            SteamAppId = "730",
            Category = AppCategory.SteamGame
        };

        AppUninstaller.DetectEngineAndQuietCommand(item);

        Assert.AreEqual(UninstallerEngineType.Steam, item.EngineType);
        Assert.IsFalse(item.CanSilentUninstall);
    }

    [TestMethod]
    public void TestSafetyGuard_IsProhibitedDirectory()
    {
        // Raízes e pastas de sistema protegidas NUNCA devem ser excluídas
        Assert.IsTrue(AppUninstaller.IsProhibitedDirectory(@"C:\"));
        Assert.IsTrue(AppUninstaller.IsProhibitedDirectory(@"C:\Windows"));
        Assert.IsTrue(AppUninstaller.IsProhibitedDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.IsTrue(AppUninstaller.IsProhibitedDirectory(Environment.GetFolderPath(Environment.SpecialFolder.System)));
        Assert.IsTrue(AppUninstaller.IsProhibitedDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.IsTrue(AppUninstaller.IsProhibitedDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.IsTrue(AppUninstaller.IsProhibitedDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)));

        // Pasta normal de aplicativo instalado (pelo menos 2 níveis de profundidade) deve ser permitida
        Assert.IsFalse(AppUninstaller.IsProhibitedDirectory(@"C:\Program Files\ResetTestVendor\TestAppDummy123"));
        Assert.IsFalse(AppUninstaller.IsProhibitedDirectory(@"D:\Games\MyCustomGame"));
    }

    [TestMethod]
    public void TestCategoryBadgesAndBrushes()
    {
        var win32 = new InstalledSoftwareItem { Category = AppCategory.DesktopWin32 };
        Assert.AreEqual("💻 Desktop", win32.CategoryBadge);
        Assert.AreEqual("#3B82F6", win32.CategoryBrush);

        var store = new InstalledSoftwareItem { Category = AppCategory.MicrosoftStore };
        Assert.AreEqual("🛍️ Store", store.CategoryBadge);
        Assert.AreEqual("#06B6D4", store.CategoryBrush);

        var steam = new InstalledSoftwareItem { Category = AppCategory.SteamGame };
        Assert.AreEqual("🎮 Steam", steam.CategoryBadge);
        Assert.AreEqual("#10B981", steam.CategoryBrush);

        var portable = new InstalledSoftwareItem { Category = AppCategory.PortableDirectory };
        Assert.AreEqual("📁 Portátil", portable.CategoryBadge);
        Assert.AreEqual("#F59E0B", portable.CategoryBrush);
    }

    [TestMethod]
    public void TestLeftoverScanReport_TotalsAndFormatting()
    {
        var report = new LeftoverScanReport
        {
            App = new InstalledSoftwareItem { DisplayName = "Dummy App" },
            FilesAndFolders = new List<LeftoverFileItem>
            {
                new LeftoverFileItem { Path = @"C:\Test\Dir", Size = 1048576, IsDirectory = true }
            },
            Shortcuts = new List<LeftoverFileItem>
            {
                new LeftoverFileItem { Path = @"C:\Desktop\App.lnk", Size = 1024, IsDirectory = false }
            },
            SystemTraces = new List<LeftoverFileItem>
            {
                new LeftoverFileItem { Path = @"C:\Windows\Prefetch\APP.EXE-123.pf", Size = 2048, IsDirectory = false }
            },
            RegistryEntries = new List<LeftoverRegistryItem>
            {
                new LeftoverRegistryItem { SubKeyPath = @"SOFTWARE\TestVendor\App" }
            },
            FirewallRules = new List<LeftoverRegistryItem>
            {
                new LeftoverRegistryItem { SubKeyPath = @"SYSTEM\Firewall\Rules", ValueName = "Rule1" }
            }
        };

        Assert.AreEqual(5, report.TotalItemsCount);
        Assert.AreEqual(1048576 + 1024 + 2048, report.TotalLeftoverBytes);
        Assert.IsTrue(report.FormattedTotalSize.Contains("MB") || report.FormattedTotalSize.Contains("KB"));
    }

    [TestMethod]
    public async Task TestGetInstalledAppsAsync_LiveScan()
    {
        var apps = await AppUninstaller.GetInstalledAppsAsync();
        Assert.IsNotNull(apps);
        Assert.IsTrue(apps.Count > 0, "Deveria detectar aplicativos instalados no sistema");

        // Verifica que o Steam ou Win32 ou Store foram detectados
        int win32Count = 0;
        int steamCount = 0;
        int storeCount = 0;
        int portableCount = 0;

        foreach (var app in apps)
        {
            switch (app.Category)
            {
                case AppCategory.DesktopWin32: win32Count++; break;
                case AppCategory.SteamGame: steamCount++; break;
                case AppCategory.MicrosoftStore: storeCount++; break;
                case AppCategory.PortableDirectory: portableCount++; break;
            }
        }

        Console.WriteLine($"Live Scan Results: Total={apps.Count}, Win32={win32Count}, Steam={steamCount}, Store={storeCount}, Portable={portableCount}");
        Assert.IsTrue(win32Count > 0, "Deveria detectar aplicativos Win32");
    }

    [TestMethod]
    public void TestInstalledSoftwareItem_SelectionAndHiddenComponent()
    {
        var item = new InstalledSoftwareItem
        {
            DisplayName = "Test Component",
            IsHiddenOrComponent = true,
            IsSelected = true
        };

        Assert.IsTrue(item.IsHiddenOrComponent);
        Assert.IsTrue(item.IsSelected);
        Assert.AreEqual("⚙️ Componente", item.HiddenBadge);
        Assert.AreEqual(System.Windows.Visibility.Visible, item.HiddenBadgeVisibility);

        var normalItem = new InstalledSoftwareItem
        {
            DisplayName = "Normal App",
            IsHiddenOrComponent = false,
            IsSelected = false
        };

        Assert.IsFalse(normalItem.IsHiddenOrComponent);
        Assert.IsFalse(normalItem.IsSelected);
        Assert.AreEqual("", normalItem.HiddenBadge);
        Assert.AreEqual(System.Windows.Visibility.Collapsed, normalItem.HiddenBadgeVisibility);
    }

    [TestMethod]
    public void TestExtractExecutablePath_QuotedAndUnquoted()
    {
        // Caso 1: Aspas duplas
        string cmd1 = "\"C:\\Program Files\\Vendor\\unins000.exe\" /silent";
        string exe1 = AppUninstaller.ExtractExecutablePath(cmd1);
        Assert.AreEqual(@"C:\Program Files\Vendor\unins000.exe", exe1);

        // Caso 2: Sem aspas, com espacos
        string cmd2 = @"C:\Program Files (x86)\My App\uninstall.exe -uninstall";
        string exe2 = AppUninstaller.ExtractExecutablePath(cmd2);
        Assert.AreEqual(@"C:\Program Files (x86)\My App\uninstall.exe", exe2);

        // Caso 3: MsiExec com /X
        string cmd3 = "MsiExec.exe /X{12345678-ABCD-1234-ABCD-123456789ABC}";
        string exe3 = AppUninstaller.ExtractExecutablePath(cmd3);
        Assert.IsTrue(exe3.EndsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TestBatchSelection_ListToggling()
    {
        var items = new List<InstalledSoftwareItem>
        {
            new InstalledSoftwareItem { DisplayName = "App 1", IsSelected = false },
            new InstalledSoftwareItem { DisplayName = "App 2", IsSelected = false },
            new InstalledSoftwareItem { DisplayName = "App 3", IsSelected = false }
        };

        // Selecionar todos
        foreach (var item in items) item.IsSelected = true;
        Assert.AreEqual(3, items.Count(i => i.IsSelected));

        // Desmarcar todos
        foreach (var item in items) item.IsSelected = false;
        Assert.AreEqual(0, items.Count(i => i.IsSelected));
    }
}
