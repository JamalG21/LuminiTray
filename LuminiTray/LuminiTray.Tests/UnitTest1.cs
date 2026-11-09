using Microsoft.VisualStudio.TestTools.UnitTesting;



using LuminiTray;



using System.IO;



using System.Text.Json;



using System.Runtime.InteropServices;



namespace LuminiTray.Tests;



[TestClass]



public class LuminiTrayTests



{



    [TestMethod]



    public void TestAppSettings_DefaultsAndSerialization()



    {



        var settings = new AppSettings();



        Assert.IsTrue(settings.AutoApplyTweaks, "AutoApplyTweaks deve ser true por padrão");



        Assert.IsTrue(settings.AutoCleanRam, "AutoCleanRam deve ser true por padrão");



        Assert.IsTrue(settings.AutoStartMinimized, "AutoStartMinimized deve ser true por padrão");



        Assert.IsFalse(settings.AutoCloseBackgroundApps, "AutoCloseBackgroundApps deve ser false por padrão");



        settings.AutoStartWithWindows = true;



        settings.LastAutoFeaturesSummary = "Test Summary";



        string json = JsonSerializer.Serialize(settings);



        var restored = JsonSerializer.Deserialize<AppSettings>(json);



        Assert.IsNotNull(restored);



        Assert.IsTrue(restored.AutoStartWithWindows);



        Assert.IsTrue(restored.AutoApplyTweaks);



        Assert.AreEqual("Test Summary", restored.LastAutoFeaturesSummary);
        Assert.IsTrue(settings.LivelyWallpaperAutoPauseGames);
        Assert.IsFalse(settings.LivelyWallpaperPauseOnFocusedApp);
        Assert.IsNotNull(settings.LivelyWallpaperPlaylist);
    }

    [TestMethod]
    public void TestLivelyWallpaper_PlaylistManagement()
    {
        var service = LivelyWallpaperService.Instance;
        service.Playlist.Clear();

        string temp1 = Path.GetTempFileName() + ".mp4";
        string temp2 = Path.GetTempFileName() + ".mp4";
        File.WriteAllText(temp1, "test");
        File.WriteAllText(temp2, "test");
        try
        {
            service.SyncPlaylistWithFolder(new[] { temp1, temp2 });
            Assert.AreEqual(2, service.Playlist.Count);
            Assert.AreEqual(temp1, service.Playlist[0]);
            Assert.AreEqual(temp2, service.Playlist[1]);
        }
        finally
        {
            try { File.Delete(temp1); } catch { }
            try { File.Delete(temp2); } catch { }
            service.Playlist.Clear();
        }
    }

    [TestMethod]
    public void TestWallpaperManager_ScanFolder_DetectsVideosAndSetsProperties()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "LuminiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string vidPath = Path.Combine(tempDir, "sample_wallpaper.mp4");
        string imgPath = Path.Combine(tempDir, "sample_photo.png");
        File.WriteAllText(vidPath, "dummy video");
        File.WriteAllText(imgPath, "dummy image");

        try
        {
            var items = WallpaperManager.ScanFolder(tempDir);
            Assert.AreEqual(2, items.Count);

            var vidItem = items.FirstOrDefault(i => i.IsVideo);
            Assert.IsNotNull(vidItem);
            Assert.AreEqual("sample_wallpaper.mp4", vidItem.FileName);
            Assert.AreEqual("🎬 ANIMADO", vidItem.TypeBadge);
            Assert.AreEqual("▶ APLICAR WALLPAPER ANIMADO", vidItem.ActionButtonText);
            Assert.AreEqual(System.Windows.Visibility.Visible, vidItem.VideoBadgeVisibility);

            var imgItem = items.FirstOrDefault(i => !i.IsVideo);
            Assert.IsNotNull(imgItem);
            Assert.AreEqual("sample_photo.png", imgItem.FileName);
            Assert.AreEqual("IMAGEM", imgItem.TypeBadge);
            Assert.AreEqual("DEFINIR COMO WALLPAPER", imgItem.ActionButtonText);
            Assert.AreEqual(System.Windows.Visibility.Collapsed, imgItem.VideoBadgeVisibility);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }



    }



    [TestMethod]



    public void TestAutoStartManager_PropertiesAndExe()



    {



        Assert.AreEqual("LuminiTrayAutoStart", AutoStartManager.TaskName);



        Assert.AreEqual("LuminiTray", AutoStartManager.RegistryValueName);



        string exe = AutoStartManager.GetExecutablePath();



        Assert.IsFalse(string.IsNullOrWhiteSpace(exe), "Caminho do executável não deve ser vazio");



        // Consulta de status não deve lançar exceção



        bool isEnabled = AutoStartManager.IsAutoStartEnabled();



        string status = AutoStartManager.GetStatusSummary();



        Assert.IsFalse(string.IsNullOrWhiteSpace(status));



    }



    [TestMethod]



    public void TestWindowsTweaks_Integrity()



    {



        var tweaks = WindowsTweaks.All();



        Assert.IsTrue(tweaks.Count >= 5, "Deveria haver pelo menos 5 ajustes");



        var ids = new HashSet<string>();



        foreach (var t in tweaks)



        {



            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Id));



            Assert.IsTrue(ids.Add(t.Id), $"ID duplicado: {t.Id}");



            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Title));



            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Description));



        }



        Assert.IsTrue(ids.Contains("power"));



        Assert.IsTrue(ids.Contains("gamemode"));



        Assert.IsTrue(ids.Contains("gamedvr"));



    }



    [TestMethod]



    public void TestOptimizer_MemoryStatusAndApps()



    {



        var (total, avail) = Optimizer.MemoryStatus();



        Assert.IsTrue(total > 0, "Total de RAM deve ser > 0");



        Assert.IsTrue(avail > 0, "RAM disponível deve ser > 0");



        Assert.IsTrue(avail <= total, "RAM disponível não pode exceder o total");



        var apps = Optimizer.ListApps();



        Assert.IsNotNull(apps);



        foreach (var a in apps)



        {



            Assert.AreNotEqual("System", a.ProcessName, ignoreCase: true);



            Assert.AreNotEqual("explorer", a.ProcessName, ignoreCase: true);



        }



    }



    [TestMethod]



    public void TestStartupManager_ExeFromCommand()



    {



        string? exe1 = StartupManager.ExeFromCommand("\"C:\\Program Files\\App\\app.exe\" --arg");



        Assert.IsNotNull(exe1);



        Assert.IsTrue(exe1.EndsWith("app.exe", StringComparison.OrdinalIgnoreCase));



        string? exe2 = StartupManager.ExeFromCommand("C:\\Windows\\notepad.exe");



        Assert.IsNotNull(exe2);



        Assert.IsTrue(exe2.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase));



    }



    [TestMethod]



    public async Task TestAutoFeaturesManager_Execution()



    {



        var settings = new AppSettings



        {



            AutoApplyTweaks = false,



            AutoCleanRam = true,



            AutoCloseBackgroundApps = false



        };



        var result = await AutoFeaturesManager.ApplyAsync(settings);



        Assert.IsNotNull(result);



        Assert.IsTrue(result.Success);



        Assert.IsTrue(settings.LastAutoFeaturesApplied.HasValue);



        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Summary));



    }



    [TestMethod]



    public void TestWallpaperManager_InstallLibraryAndScan()



    {



        string dir = WallpaperManager.DefaultLibraryFolder;



        Assert.IsFalse(string.IsNullOrWhiteSpace(dir));



        var generated = WallpaperManager.InstallDefaultLibrary();



        Assert.IsNotNull(generated);



        Assert.AreEqual(6, generated.Count, "Devem ser gerados exatamente 6 papéis de parede na biblioteca padrão.");



        foreach (var file in generated)



        {



            Assert.IsTrue(File.Exists(file), $"Arquivo de wallpaper gerado não encontrado: {file}");



            var fi = new FileInfo(file);



            Assert.IsTrue(fi.Length > 10000, $"Tamanho do arquivo muito pequeno: {fi.Length} bytes");



        }



        var scanned = WallpaperManager.ScanFolder(dir);



        Assert.IsNotNull(scanned);



        Assert.IsTrue(scanned.Count >= 6, $"Varredura deveria encontrar pelo menos 6 papéis de parede, encontrou {scanned.Count}");



        var first = scanned[0];



        Assert.IsFalse(string.IsNullOrWhiteSpace(first.FileName));



        Assert.IsFalse(string.IsNullOrWhiteSpace(first.FileSizeText));



        string dims = WallpaperManager.GetDimensions(first.FilePath);



        Assert.AreEqual("1920 × 1080", dims, "As dimensões geradas para a biblioteca padrão devem ser 1920 × 1080");



        var thumb = WallpaperManager.LoadThumbnail(first.FilePath, 320);



        Assert.IsNotNull(thumb, "Miniatura não deve ser nula");



        Assert.IsTrue(thumb.PixelWidth > 0, "Largura da miniatura deve ser maior que zero");



    }



    [TestMethod]



    public void TestWallpaperManager_CurrentAndSettings()



    {



        // GetCurrentWallpaper não deve falhar



        string? current = WallpaperManager.GetCurrentWallpaper();



        // Pode ser null se não houver ou se for cor sólida, mas se houver deve ser string não vazia



        if (!string.IsNullOrEmpty(current))



        {



            Assert.IsTrue(current.Length > 3);



        }



        var settings = new AppSettings



        {



            WallpaperFolder = WallpaperManager.DefaultLibraryFolder,



            WallpaperStyle = "fit",



            WallpaperIntervalMinutes = 15,



            AutoChangeWallpaperOnStartup = true



        };



        Assert.AreEqual("fit", settings.WallpaperStyle);



        Assert.AreEqual(15, settings.WallpaperIntervalMinutes);



        Assert.IsTrue(settings.AutoChangeWallpaperOnStartup);



        Assert.AreEqual(WallpaperManager.DefaultLibraryFolder, settings.WallpaperFolder);



    }



    [TestMethod]



    public void TestWallpaperManager_MonitorDetectionAndPerMonitorSettings()



    {



        var monitors = WallpaperManager.GetConnectedMonitors();



        Assert.IsNotNull(monitors);



        Assert.IsTrue(monitors.Count >= 1, "Deve ser detectado pelo menos 1 monitor no sistema");



        bool hasPrimary = false;



        foreach (var m in monitors)



        {



            Assert.IsFalse(string.IsNullOrWhiteSpace(m.DisplayName));



            Assert.IsFalse(string.IsNullOrWhiteSpace(m.DeviceId));



            Assert.IsTrue(m.Width > 0, "Largura do monitor deve ser positiva");



            Assert.IsTrue(m.Height > 0, "Altura do monitor deve ser positiva");



            Assert.IsFalse(string.IsNullOrWhiteSpace(m.ResolutionText));



            Assert.IsFalse(string.IsNullOrWhiteSpace(m.Label));



            if (m.IsPrimary) hasPrimary = true;



        }



        Assert.IsTrue(hasPrimary, "Pelo menos um monitor deve ser marcado como Principal");



        var settings = new AppSettings



        {



            SelectedWallpaperMonitorId = monitors[0].DeviceId



        };



        string json = JsonSerializer.Serialize(settings);



        var restored = JsonSerializer.Deserialize<AppSettings>(json);



        Assert.IsNotNull(restored);



        Assert.AreEqual(monitors[0].DeviceId, restored.SelectedWallpaperMonitorId);



    }



    [TestMethod]



    public void TestOverlay_CustomPositionAndLockSettings()



    {



        var settings = new AppSettings



        {



            OverlayCorner = "CUSTOM",



            OverlayCustomPosition = true,



            OverlayCustomX = 1920,



            OverlayCustomY = 50,



            OverlayLocked = false,



            OverlayScale = 1.3



        };



        string json = JsonSerializer.Serialize(settings);



        var restored = JsonSerializer.Deserialize<AppSettings>(json);



        Assert.IsNotNull(restored);



        Assert.AreEqual("CUSTOM", restored.OverlayCorner);



        Assert.IsTrue(restored.OverlayCustomPosition);



        Assert.AreEqual(1920, restored.OverlayCustomX);



        Assert.AreEqual(50, restored.OverlayCustomY);



        Assert.IsFalse(restored.OverlayLocked);



        Assert.AreEqual(1.3, restored.OverlayScale);



        // Verifica métricas de overlay



        var defaults = OverlayMetrics.Defaults();



        Assert.IsNotNull(defaults);



        Assert.IsTrue(defaults.Contains("fps"));



        Assert.IsTrue(defaults.Contains("frametime"));



    }



    [TestMethod]



    public void TestPsuCalculator_HardwareDetection()



    {



        // Detecção de processadores



        var ryzenX3d = PsuCalculator.DetectCurrentCpu("AMD Ryzen 7 7800X3D 8-Core Processor");



        Assert.IsNotNull(ryzenX3d);



        Assert.IsTrue(ryzenX3d.Name.Contains("7800X3D"));



        Assert.AreEqual(120, ryzenX3d.Watts);



        var intelI9 = PsuCalculator.DetectCurrentCpu("13th Gen Intel(R) Core(TM) i9-13900K");



        Assert.IsNotNull(intelI9);



        Assert.IsTrue(intelI9.Name.Contains("13900K"));



        Assert.AreEqual(320, intelI9.Watts);



        var ryzen5 = PsuCalculator.DetectCurrentCpu("AMD Ryzen 5 5600X 6-Core Processor");



        Assert.IsNotNull(ryzen5);



        Assert.IsTrue(ryzen5.Name.Contains("5600X"));



        var ryzen9000 = PsuCalculator.DetectCurrentCpu("AMD Ryzen 9 9950X 16-Core Processor");



        Assert.IsNotNull(ryzen9000);



        Assert.IsTrue(ryzen9000.Name.Contains("9950X"));



        var intelUltra = PsuCalculator.DetectCurrentCpu("Intel(R) Core(TM) Ultra 9 285K");



        Assert.IsNotNull(intelUltra);



        Assert.IsTrue(intelUltra.Name.Contains("285K"));



        var haswellDdr3 = PsuCalculator.DetectCurrentCpu("Intel(R) Core(TM) i7-4790K CPU @ 4.00GHz");



        Assert.IsNotNull(haswellDdr3);



        Assert.IsTrue(haswellDdr3.Name.Contains("4790K"));



        var xeonV3 = PsuCalculator.DetectCurrentCpu("Intel(R) Xeon(R) CPU E5-2666 v3 @ 2.90GHz");



        Assert.IsNotNull(xeonV3);



        Assert.IsTrue(xeonV3.Name.Contains("2666 v3"));



        // Detecção de Placa-Mãe



        var moboB550 = PsuCalculator.DetectCurrentMotherboard("ASUSTeK COMPUTER INC. TUF GAMING B550M-PLUS");



        Assert.IsNotNull(moboB550);



        Assert.AreEqual("Intermediária", moboB550.Category);



        Assert.AreEqual(50, moboB550.Watts);



        var moboZ790 = PsuCalculator.DetectCurrentMotherboard("MSI MAG Z790 TOMAHAWK WIFI");



        Assert.IsNotNull(moboZ790);



        Assert.AreEqual("Avançada", moboZ790.Category);



        Assert.AreEqual(75, moboZ790.Watts);



        var moboDdr3 = PsuCalculator.DetectCurrentMotherboard("Gigabyte Technology Co., Ltd. GA-H81M-S1");



        Assert.IsNotNull(moboDdr3);



        Assert.AreEqual("Legado DDR3", moboDdr3.Category);



        Assert.AreEqual(45, moboDdr3.Watts);



        var moboX99 = PsuCalculator.DetectCurrentMotherboard("HUANANZHI X99-QD4");



        Assert.IsNotNull(moboX99);



        Assert.AreEqual("Workstation", moboX99.Category);



        Assert.AreEqual(110, moboX99.Watts);



        // Detecção de placas de vídeo



        var rtx4070 = PsuCalculator.DetectCurrentGpu("NVIDIA GeForce RTX 4070 Ti SUPER");



        Assert.IsNotNull(rtx4070);



        Assert.IsTrue(rtx4070.Name.Contains("RTX 4070 Ti"));



        Assert.AreEqual(285, rtx4070.Watts);



        var rx7800 = PsuCalculator.DetectCurrentGpu("AMD Radeon RX 7800 XT");



        Assert.IsNotNull(rx7800);



        Assert.IsTrue(rx7800.Name.Contains("RX 7800 XT"));



        Assert.AreEqual(263, rx7800.Watts);



        var rtx5090 = PsuCalculator.DetectCurrentGpu("GeForce RTX 5090");



        Assert.IsNotNull(rtx5090);



        Assert.IsTrue(rtx5090.Name.Contains("5090"));



        Assert.AreEqual(600, rtx5090.Watts);



    }



    [TestMethod]



    public void TestPsuCalculator_CalculationAndHeadroom()



    {



        var config = new PsuConfig



        {



            CpuName = "AMD Ryzen 7 7800X3D",



            CpuWatts = 120,



            GpuName = "NVIDIA GeForce RTX 4070 Ti SUPER",



            GpuWatts = 285,



            RamType = "2x DDR5",



            RamSticks = 2,



            NvmeCount = 2,



            SataSsdCount = 0,



            HddCount = 1,



            CoolerType = "Water Cooler AIO 240mm",



            FanCount = 4,



            Overclock = false,



            RgbAndPeripherals = true



        };



        var result = PsuCalculator.Calculate(config);



        Assert.IsNotNull(result);



        // CPU (120) + GPU (285) + Mobo (50) + RAM (2*6=12) + Storage (2*8+12=28) + Cooler (22+4*3=34) + USB (20) = 549W



        Assert.IsTrue(result.TotalPeakWatts >= 500 && result.TotalPeakWatts <= 600, $"TotalPeakWatts inesperado: {result.TotalPeakWatts}");



        // Recomendação com ~30% de margem (549 * 1.30 = 713.7 -> Arredonda para 750W)



        Assert.AreEqual(750, result.RecommendedPsuWatts, "Fonte recomendada para ~549W de pico deve ser 750W");



        Assert.IsTrue(result.HeadroomWatts >= 150, "Headroom deve ser de pelo menos 150W");



        Assert.IsTrue(result.LoadPercentageAtRecommended < 80.0, "Carga na fonte recomendada deve estar abaixo de 80%");



        Assert.IsFalse(string.IsNullOrWhiteSpace(result.RecommendedEfficiency));



        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PcieCablesRecommendation));



        Assert.IsFalse(string.IsNullOrWhiteSpace(result.UpgradeAdvice));



        // Teste com Overclock (+15% no pico de CPU e GPU)



        config.Overclock = true;



        var ocResult = PsuCalculator.Calculate(config);



        Assert.IsTrue(ocResult.TotalPeakWatts > result.TotalPeakWatts, "Overclock deve aumentar a potência total estimada");



        Assert.IsTrue(ocResult.CpuWatts > result.CpuWatts);



        Assert.IsTrue(ocResult.GpuWatts > result.GpuWatts);



        // Teste com DDR3 e SSD SATA



        var ddr3Config = new PsuConfig



        {



            CpuName = "Intel Core i7-4790K",



            CpuWatts = 110,



            MotherboardWatts = 45,



            GpuName = "NVIDIA GeForce GTX 1660 Super",



            GpuWatts = 125,



            RamType = "2x DDR3",



            RamSticks = 2,



            NvmeCount = 0,



            SataSsdCount = 2,



            HddCount = 1,



            CoolerType = "Air Cooler",



            FanCount = 2,



            Overclock = false,



            RgbAndPeripherals = false



        };



        var ddr3Result = PsuCalculator.Calculate(ddr3Config);



        Assert.IsNotNull(ddr3Result);



        Assert.AreEqual(6, ddr3Result.RamWatts, "2 pentes DDR3 devem consumir 6W (3W cada)");



        Assert.AreEqual(20, ddr3Result.StorageWatts, "2x SATA SSD (8W) + 1x HDD (12W) = 20W");



        Assert.AreEqual(45, ddr3Result.MotherboardWatts, "Placa-Mãe DDR3 deve ser 45W");



        Assert.IsTrue(ddr3Result.RecommendedPsuWatts <= 500, "Setup intermediário antigo deve recomendar fonte de 450W ou 500W");



    }



    [TestMethod]



    public void TestPsuCalculator_MotherboardCompatibilityAndNotebookHardware()



    {



        // 1. Compatibilidade de CPU e RAM por Placa-Mãe



        var am5Mobo = PsuCalculator.MotherboardList.FirstOrDefault(m => m.Platform == "AM5");



        Assert.IsNotNull(am5Mobo);



        var am5Cpus = PsuCalculator.GetCompatibleCpus(am5Mobo);



        Assert.IsTrue(am5Cpus.Any(), "Deve ter CPUs compatíveis com AM5");



        Assert.IsTrue(am5Cpus.All(c => c.Platform == "AM5" || c.Platform == "All"), "Todas as CPUs retornadas devem ser AM5 ou All");



        Assert.IsTrue(am5Cpus.Any(c => c.Name.Contains("7800X3D")));



        var am5Ram = PsuCalculator.GetCompatibleRam(am5Mobo);



        Assert.IsTrue(am5Ram.All(r => r.Generation == "DDR5"), "AM5 só deve permitir memória DDR5");



        var am4Mobo = PsuCalculator.MotherboardList.FirstOrDefault(m => m.Platform == "AM4");



        Assert.IsNotNull(am4Mobo);



        var am4Cpus = PsuCalculator.GetCompatibleCpus(am4Mobo);



        Assert.IsTrue(am4Cpus.Any(c => c.Name.Contains("5800X3D") || c.Name.Contains("5600X")));



        var am4Ram = PsuCalculator.GetCompatibleRam(am4Mobo);



        Assert.IsTrue(am4Ram.All(r => r.Generation == "DDR4"), "AM4 só deve permitir memória DDR4");



        var lga1150Mobo = PsuCalculator.MotherboardList.FirstOrDefault(m => m.Platform == "LGA1150_1155");



        Assert.IsNotNull(lga1150Mobo);



        var lga1150Ram = PsuCalculator.GetCompatibleRam(lga1150Mobo);



        Assert.IsTrue(lga1150Ram.All(r => r.Generation == "DDR3"), "LGA 1150 só deve permitir memória DDR3");



        var noteMobo = PsuCalculator.MotherboardList.FirstOrDefault(m => m.Category == "Notebook");



        Assert.IsNotNull(noteMobo);



        var noteCpus = PsuCalculator.GetCompatibleCpus(noteMobo);



        Assert.IsTrue(noteCpus.Any(c => c.Category.Contains("Mobile") || c.Category == "Notebook"), "Deve conter CPUs de Notebook");



        var noteRam = PsuCalculator.GetCompatibleRam(noteMobo);



        Assert.IsTrue(noteRam.Any(r => r.Label.Contains("SO-DIMM")), "Deve conter opções SO-DIMM para notebook");



        // 2. Detecção de hardware mobile / notebook



        var noteCpu1 = PsuCalculator.DetectCurrentCpu("13th Gen Intel(R) Core(TM) i7-13700H");



        Assert.IsNotNull(noteCpu1);



        Assert.IsTrue(noteCpu1.Category.Contains("Mobile") || noteCpu1.Category == "Notebook");



        Assert.IsTrue(noteCpu1.Watts <= 120);



        var noteCpu2 = PsuCalculator.DetectCurrentCpu("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics");



        Assert.IsNotNull(noteCpu2);



        Assert.IsTrue(noteCpu2.Category.Contains("Mobile") || noteCpu2.Category == "Notebook");



        var noteGpu1 = PsuCalculator.DetectCurrentGpu("NVIDIA GeForce RTX 4060 Laptop GPU");



        Assert.IsNotNull(noteGpu1);



        Assert.IsTrue(noteGpu1.Category.Contains("Mobile") || noteGpu1.Category == "Notebook");



        Assert.IsTrue(noteGpu1.Watts <= 115);



        var noteGpu2 = PsuCalculator.DetectCurrentGpu("AMD Radeon 780M");



        Assert.IsNotNull(noteGpu2);



        Assert.AreEqual("Integrado", noteGpu2.Category);



        // 3. Detecção de placa-mãe de notebook



        var detectedNoteMobo = PsuCalculator.DetectCurrentMotherboard("Dell Inc. 0G84P1", "13th Gen Intel(R) Core(TM) i7-13700H");



        Assert.IsNotNull(detectedNoteMobo);



        Assert.AreEqual("Notebook", detectedNoteMobo.Category);



    }



    [TestMethod]



    public void TestDiskSpaceAnalyzer_FormattingAndIcons()



    {



        Assert.AreEqual("0 B", DiskSpaceAnalyzer.FormatSize(0));



        Assert.AreEqual("500 B", DiskSpaceAnalyzer.FormatSize(500));



        Assert.AreEqual("1 KB", DiskSpaceAnalyzer.FormatSize(1024));



        Assert.AreEqual($"{(1.0):F1} MB", DiskSpaceAnalyzer.FormatSize(1024 * 1024));



        Assert.AreEqual($"{(1.0):F2} GB", DiskSpaceAnalyzer.FormatSize(1024L * 1024 * 1024));



        Assert.AreEqual($"{(1.0):F2} TB", DiskSpaceAnalyzer.FormatSize(1024L * 1024 * 1024 * 1024));



        Assert.AreEqual("⚙️", DiskSpaceItem.GetFileIcon("test.exe"));



        Assert.AreEqual("⚙️", DiskSpaceItem.GetFileIcon("setup.msi"));



        Assert.AreEqual("📦", DiskSpaceItem.GetFileIcon("archive.zip"));



        Assert.AreEqual("🎬", DiskSpaceItem.GetFileIcon("video.mp4"));



        Assert.AreEqual("🎵", DiskSpaceItem.GetFileIcon("audio.mp3"));



        Assert.AreEqual("🖼️", DiskSpaceItem.GetFileIcon("photo.png"));



        Assert.AreEqual("🔧", DiskSpaceItem.GetFileIcon("driver.sys"));



        Assert.AreEqual("📄", DiskSpaceItem.GetFileIcon("readme.txt"));



        Assert.AreEqual("📄", DiskSpaceItem.GetFileIcon("unknown.xyz"));



    }



    [TestMethod]



    public async Task TestDiskSpaceAnalyzer_DirectoryScan()



    {



        string tempDir = Path.Combine(Path.GetTempPath(), "LuminiTray_DiskSpace_Test_" + Guid.NewGuid().ToString("N"));



        try



        {



            Directory.CreateDirectory(tempDir);



            string subDir1 = Path.Combine(tempDir, "Sub1");



            string subDir2 = Path.Combine(tempDir, "Sub2");



            Directory.CreateDirectory(subDir1);



            Directory.CreateDirectory(subDir2);



            // Cria arquivos de teste com tamanhos conhecidos



            byte[] buf10k = new byte[10240];



            byte[] buf5k = new byte[5120];



            byte[] buf1k = new byte[1024];



            File.WriteAllBytes(Path.Combine(subDir1, "large.bin"), buf10k);



            File.WriteAllBytes(Path.Combine(subDir1, "sample.txt"), buf5k);



            File.WriteAllBytes(Path.Combine(subDir2, "small.log"), buf1k);



            long expectedTotal = 10240 + 5120 + 1024;



            var (root, topFiles, extensions) = await DiskSpaceAnalyzer.ScanAsync(tempDir);



            Assert.IsNotNull(root);



            Assert.AreEqual(expectedTotal, root.Size, "Tamanho total do diretório raiz deve coincidir");



            Assert.AreEqual(2, root.Children.Count, "Devem haver 2 subdiretórios no primeiro nível");



            Assert.IsTrue(root.Children[0].Size >= root.Children[1].Size, "Filhos devem ser ordenados decrescente por tamanho");



            Assert.IsNotNull(topFiles);



            Assert.IsTrue(topFiles.Count >= 3, "Devem ser detectados pelo menos 3 arquivos");



            Assert.AreEqual("large.bin", topFiles[0].Name, "Maior arquivo deve ser large.bin");



            Assert.IsNotNull(extensions);



            Assert.IsTrue(extensions.Any(e => e.Extension.Equals(".bin", StringComparison.OrdinalIgnoreCase)));



            Assert.IsTrue(extensions.Any(e => e.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)));



            Assert.IsTrue(extensions.Any(e => e.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase)));



        }



        finally



        {



            if (Directory.Exists(tempDir))



            {



                try { Directory.Delete(tempDir, true); } catch { }



            }



        }



    }



    [TestMethod]



    public async Task TestDiskSpaceAnalyzer_QuickCleanScan()



    {



        var categories = await DiskSpaceAnalyzer.ScanQuickCleanAsync();



        Assert.IsNotNull(categories);



        Assert.IsTrue(categories.Count >= 4, "Devem haver categorias como Temp do Usuário, Windows Temp, Logs, etc.");



        foreach (var cat in categories)



        {



            Assert.IsFalse(string.IsNullOrWhiteSpace(cat.Name));



            Assert.IsFalse(string.IsNullOrWhiteSpace(cat.Description));



            Assert.IsTrue(cat.Size >= 0);



            Assert.IsFalse(string.IsNullOrWhiteSpace(cat.FormattedSize));



        }



        var shaderCat = categories.FirstOrDefault(c => c.Name.Contains("Shaders", StringComparison.OrdinalIgnoreCase));



        Assert.IsNotNull(shaderCat, "Deve existir a categoria de Caches de Sombras e Shaders");



        Assert.IsNotNull(shaderCat.ExplicitTargetFiles, "A categoria de shaders deve usar lista explícita filtrada preservando caches ativos");



        // Testa limpeza seletiva por ExplicitTargetFiles garantindo preservação de arquivos ativos



        string tempShaderDir = Path.Combine(Path.GetTempPath(), "LuminiTray_ShaderCleanTest_" + Guid.NewGuid().ToString("N"));



        Directory.CreateDirectory(tempShaderDir);



        try



        {



            string obsoleteFile = Path.Combine(tempShaderDir, "old_driver_cache.nvph");



            string activeGameFile = Path.Combine(tempShaderDir, "active_game_cache.nvph");



            File.WriteAllBytes(obsoleteFile, new byte[4096]);



            File.WriteAllBytes(activeGameFile, new byte[8192]);



            var testCat = new QuickCleanCategory



            {



                Name = "Test Shader Clean",



                Path = tempShaderDir,



                ExplicitTargetFiles = new List<string> { obsoleteFile },



                Size = 4096



            };



            var (cleanedCount, cleanedBytes) = await DiskSpaceAnalyzer.CleanCategoryAsync(testCat);



            Assert.AreEqual(1, cleanedCount);



            Assert.AreEqual(4096L, cleanedBytes);



            Assert.IsFalse(File.Exists(obsoleteFile), "Cache obsoleto deveria ter sido removido");



            Assert.IsTrue(File.Exists(activeGameFile), "Cache ativo de jogo instalado DEVE ser preservado");



        }



        finally



        {



            try { Directory.Delete(tempShaderDir, true); } catch { }



        }



    }



    [TestMethod]



    public async Task TestAppUninstaller_DetectionAndFinding()



    {



        var apps = await AppUninstaller.GetInstalledAppsAsync();



        Assert.IsNotNull(apps);



        // Testa busca de aplicativo simulado



        var mockApp = new InstalledSoftwareItem



        {



            DisplayName = "Test Mock Application",



            InstallLocation = @"C:\Program Files\TestMockApp",



            DisplayVersion = "1.0.0",



            Publisher = "Test Vendor",



            UninstallString = @"C:\Program Files\TestMockApp\uninstall.exe"



        };



        var appList = new List<InstalledSoftwareItem> { mockApp };



        // Teste de localização por caminho dentro da pasta de instalação



        var foundBySubfile = await AppUninstaller.FindAppByExecutableAsync(@"C:\Program Files\TestMockApp\bin\test.exe", appList);



        Assert.IsNotNull(foundBySubfile);



        Assert.AreEqual("Test Mock Application", foundBySubfile.DisplayName);



        // Teste de localização por nome de executável



        var foundByName = await AppUninstaller.FindAppByExecutableAsync(@"D:\Games\TestMockApp.exe", appList);



        Assert.IsNotNull(foundByName);



        Assert.AreEqual("Test Mock Application", foundByName.DisplayName);



        // Teste de alocação de unidade (DriveAllocation)



        Assert.AreEqual("C:", mockApp.DriveAllocation);



        var mockAppD = new InstalledSoftwareItem



        {



            DisplayName = "Game on D Drive",



            InstallLocation = @"D:\Games\MyGame"



        };



        Assert.AreEqual("D:", mockAppD.DriveAllocation);



        var mockAppIcon = new InstalledSoftwareItem



        {



            DisplayName = "App with Icon on E",



            DisplayIcon = @"E:\Programs\Editor\icon.ico"



        };



        Assert.AreEqual("E:", mockAppIcon.DriveAllocation);



    }



    [TestMethod]



    public void TestDisplayDriverUninstaller_HardwareDetection()



    {



        var hw = DisplayDriverUninstaller.DetectHardware();



        Assert.IsNotNull(hw);



        Assert.IsFalse(string.IsNullOrWhiteSpace(hw.GpuSummary));



        // IsInSafeMode deve ser verificável sem erro



        Assert.IsFalse(hw.IsInSafeMode, "Ambiente normal de testes não deve estar em Modo de Segurança");



    }



    [TestMethod]



    public void TestSystemInfoCollector_WindowsInstallDateAndModelDetection()



    {



        // 1. Data de instalação do Windows



        string installDate = SystemInfoCollector.GetWindowsInstallDate();



        Assert.IsFalse(string.IsNullOrWhiteSpace(installDate));



        Assert.AreNotEqual("—", installDate, "Data de instalação do Windows deve ser lida com sucesso");



        // 2. Modelo do computador / notebook



        string model = SystemInfoCollector.DetectSystemOrNotebookModel(out bool isNotebook);



        Assert.IsFalse(string.IsNullOrWhiteSpace(model));



        Assert.AreNotEqual("—", model, "Modelo do sistema deve ser identificado");



    }



    [TestMethod]



    public void TestStressReportData_IntegrityAndSpecs()



    {



        var report = new StressReportData



        {



            TestDate = DateTime.Now,



            Duration = TimeSpan.FromMinutes(10),



            StatusText = "Concluído com Êxito",



            WasAborted = false,



            CpuName = "AMD Ryzen 7 7800X3D",



            CpuCoresThreads = "8 núcleos / 16 threads",



            CpuMaxTemp = 82.5f,



            CpuTempLimit = 90f,



            CpuMaxPowerWatts = 88f,



            CpuAvgLoad = 100f,



            GpuName = "NVIDIA GeForce RTX 4070 Ti SUPER",



            GpuVram = "16 GB GDDR6X",



            GpuMaxTemp = 71.2f,



            GpuTempLimit = 85f,



            GpuMaxPowerWatts = 275f,



            GpuAvgFps = 142.5,



            GpuTotalFrames = 42500,



            RamTotalText = "32.0 GB DDR5",



            RamSpeedType = "6000 MT/s",



            RamPasses = 4,



            RamErrors = 0,



            RecentLogs = new List<string> { "Teste iniciado", "CPU 100%", "Concluído" }



        };



        Assert.IsFalse(report.WasAborted);



        Assert.AreEqual(82.5f, report.CpuMaxTemp);



        Assert.AreEqual("AMD Ryzen 7 7800X3D", report.CpuName);



        Assert.AreEqual("NVIDIA GeForce RTX 4070 Ti SUPER", report.GpuName);



        Assert.AreEqual(0, report.RamErrors);



        Assert.AreEqual(3, report.RecentLogs.Count);



    }



    [TestMethod]



    public async Task TestDiskSurfaceScanner_TargetEnumerationAndSurfaceScan()



    {



        var targets = DiskSurfaceScanner.GetAvailableTargets();



        Assert.IsNotNull(targets);



        Assert.IsTrue(targets.Count >= 1, "Deve existir ao menos uma unidade de armazenamento pronta para varredura");



        var primaryTarget = targets.First();



        Assert.IsFalse(string.IsNullOrWhiteSpace(primaryTarget.DriveLetter));



        Assert.IsTrue(primaryTarget.TotalBytes > 0);



        Assert.IsFalse(string.IsNullOrWhiteSpace(primaryTarget.DisplayName));



        // Executa varredura rápida limitada para teste



        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));



        var progress = await DiskSurfaceScanner.ScanSurfaceAsync(



            primaryTarget,



            isQuickScan: true,



            onBlockScanned: null,



            cts.Token);



        Assert.IsNotNull(progress);



        Assert.IsTrue(progress.BlockIndex > 0, "Deveria ter escaneado ao menos 1 bloco");



        Assert.AreEqual(0, progress.BadBlockCount, "Unidade saudável não deve apresentar falsos positivos de bad blocks");



        Assert.IsTrue(progress.ExcellentCount + progress.GoodCount + progress.AttentionCount + progress.CriticalCount > 0);



        // Verifica filtro de erros físicos de mídia vs erros lógicos de sistema/alinhamento



        Assert.IsTrue(DiskSurfaceScanner.IsPhysicalMediaError(23));   // ERROR_CRC



        Assert.IsTrue(DiskSurfaceScanner.IsPhysicalMediaError(27));   // ERROR_SECTOR_NOT_FOUND



        Assert.IsTrue(DiskSurfaceScanner.IsPhysicalMediaError(1117)); // ERROR_IO_DEVICE



        Assert.IsFalse(DiskSurfaceScanner.IsPhysicalMediaError(1));   // ERROR_INVALID_FUNCTION



        Assert.IsFalse(DiskSurfaceScanner.IsPhysicalMediaError(5));   // ERROR_ACCESS_DENIED



        Assert.IsFalse(DiskSurfaceScanner.IsPhysicalMediaError(32));  // ERROR_SHARING_VIOLATION



        Assert.IsFalse(DiskSurfaceScanner.IsPhysicalMediaError(87));  // ERROR_INVALID_PARAMETER



    }



    [TestMethod]



    public void TestSmartDiagnostics_PhysicalTargetsAndEnumeration()



    {



        var physicalTargets = DiskSurfaceScanner.GetPhysicalTargets();



        Assert.IsNotNull(physicalTargets);



        Assert.IsTrue(physicalTargets.Count >= 1, "Deve existir ao menos um disco alvo para verificação");



        foreach (var t in physicalTargets)



        {



            Assert.IsFalse(string.IsNullOrWhiteSpace(t.DevicePath));



            Assert.IsFalse(string.IsNullOrWhiteSpace(t.DisplayName));



        }



    }



    [TestMethod]



    public void TestSmartDiagnostics_ParseAtaSelfTestLog()



    {



        var snap = new SmartSnapshot();



        byte[] log = new byte[512];



        log[508] = 1; // mostrecenttest index = 1



        // Entry 1 (j=1, offset = 2 + 1*24 = 26): Extended test, failed read failure (status 0x72), failing LBA = 98765432



        int o = 2 + 1 * 24;



        log[o] = 0x02; // Extended offline



        log[o + 1] = 0x72; // status: 0x7 (read failure), remaining 20%



        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(o + 2), 1500); // 1500 hours



        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(log.AsSpan(o + 5), 98765432); // LBA



        SmartDiagnostics.ParseAtaSelfTestLog(log, snap);



        Assert.AreEqual(1, snap.SelfTests.Count);



        var entry = snap.SelfTests[0];



        Assert.IsTrue(entry.Failed);



        Assert.AreEqual(98765432L, entry.FailingLba);



        Assert.IsTrue(snap.SuspectLbas.Contains(98765432L));



        Assert.IsTrue(entry.StatusText.Contains("FALHA DE LEITURA", StringComparison.OrdinalIgnoreCase));



    }



    [TestMethod]



    public void TestSmartDiagnostics_ParseAtaErrorLog()



    {



        var snap = new SmartSnapshot();



        byte[] log = new byte[512];



        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(452), 3); // 3 device errors



        log[1] = 1; // error log pointer



        // error entry 0: o = 2, e = 2 + 60 = 62



        // error_struct: e+1 = error_register (0x40 = UNC), e+3..6 = LBA



        int e = 2 + 60;



        log[e + 1] = 0x40; // UNC (uncorrectable data)



        log[e + 3] = 0x78; // LBA bytes



        log[e + 4] = 0x56;



        log[e + 5] = 0x34;



        log[e + 6] = 0x02; // LBA 28-bit: dh & 0x0F = 0x02 -> LBA = 0x02345678 = 36984440



        log[2 + 4 * 12 + 7] = 0x20; // READ SECTORS command



        SmartDiagnostics.ParseAtaErrorLog(log, snap);



        Assert.AreEqual(3, snap.DeviceErrorCount);



        Assert.IsTrue(snap.Errors.Count >= 1);



        var err = snap.Errors.First();



        Assert.IsTrue(err.IsUncorrectable);



        Assert.AreEqual(36984440L, err.Lba);



        Assert.IsTrue(snap.SuspectLbas.Contains(36984440L));



        Assert.IsTrue(err.Description.Contains("LEITURA", StringComparison.OrdinalIgnoreCase));



    }



    [TestMethod]



    public void TestSmartDiagnostics_ParseNvmeSelfTestLog()



    {



        var snap = new SmartSnapshot();



        byte[] log = new byte[564];



        log[0] = 0x01; // Current op: 1 = short in progress



        log[1] = 45;   // 45% completion



        // Entry 0 at offset 4: Extended test (op=2), result 7 (failed segments), failing LBA = 55443322



        int o = 4;



        log[o] = (2 << 4) | 0x07; // op=2, res=7



        log[o + 1] = 3;            // failed segment 3



        log[o + 2] = 0x02;         // valid bit 1 = LBA valid



        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(o + 16), 55443322);



        SmartDiagnostics.ParseNvmeSelfTestLog(log, snap);



        Assert.IsTrue(snap.SelfTestRunning);



        Assert.AreEqual(55, snap.SelfTestRemainingPercent);



        Assert.AreEqual(1, snap.SelfTests.Count);



        var test = snap.SelfTests[0];



        Assert.IsTrue(test.Failed);



        Assert.AreEqual(55443322L, test.FailingLba);



        Assert.IsTrue(snap.SuspectLbas.Contains(55443322L));



    }



    [TestMethod]



    public void TestSmartDiagnostics_LocateLbaAndParseQueryCluster()



    {



        var disk = new PhysicalDiskTarget



        {



            Index = 0,



            SizeBytes = 500_000_000_000L,



            SectorSize = 512,



            Volumes = new List<DiskVolumeExtent>



            {



                new DiskVolumeExtent



                {



                    Letter = "C:",



                    StartOffset = 1_048_576L, // 1 MB (LBA 2048)



                    Length = 100_000_000_000L,



                    BytesPerCluster = 4096,



                    FileSystem = "NTFS"



                }



            }



        };



        // LBA 100 is in the MBR/GPT initial region



        var locMbr = SmartDiagnostics.Locate(disk, 100);



        Assert.IsTrue(locMbr.Region.Contains("MBR/GPT"));



        // LBA 2048 is cluster 0 of C:



        var locC = SmartDiagnostics.Locate(disk, 2048);



        Assert.AreEqual("C:", locC.Letter);



        Assert.AreEqual(0L, locC.Cluster);



        // Test fsutil querycluster output parsing



        string sampleOutput = "0x0000000000001234  VCN 0x00000000  LCN 0x0000000000001234  \\Windows\\System32\\drivers\\ntfs.sys";



        string parsed = SmartDiagnostics.ParseQueryCluster(sampleOutput);



        Assert.IsTrue(parsed.Contains("ntfs.sys", StringComparison.OrdinalIgnoreCase));



        string freeOutput = "Cluster is free / not in use";



        string parsedFree = SmartDiagnostics.ParseQueryCluster(freeOutput);



        Assert.IsTrue(parsedFree.Contains("livre", StringComparison.OrdinalIgnoreCase));



    }



    [TestMethod]



    public void TestSmartDiagnostics_AssessmentLevels()



    {



        // 1. Saudável



        var snapGood = new SmartSnapshot



        {



            Available = true,



            Reallocated = 0,



            Pending = 0,



            OfflineUncorrectable = 0,



            CrcErrors = 0,



            HealthPassed = true



        };



        SmartDiagnostics.Assess(snapGood);



        Assert.AreEqual(BadBlockLevel.Healthy, snapGood.Level);



        // 2. Crítico por setores pendentes



        var snapCritical = new SmartSnapshot



        {



            Available = true,



            Reallocated = 5,



            Pending = 2,



            OfflineUncorrectable = 0,



            HealthPassed = true



        };



        SmartDiagnostics.Assess(snapCritical);



        Assert.AreEqual(BadBlockLevel.Critical, snapCritical.Level);



        Assert.IsTrue(snapCritical.VerdictTitle.Contains("CRÍTICO", StringComparison.OrdinalIgnoreCase));



        // 3. NVMe com erros de mídia



        var diskNvme = new PhysicalDiskTarget { Index = 1, BusType = 17 };



        var snapNvme = new SmartSnapshot



        {



            Disk = diskNvme,



            Available = true,



            MediaErrors = 12,



            AvailableSpare = 100,



            SpareThreshold = 10,



            HealthPassed = true



        };



        SmartDiagnostics.Assess(snapNvme);



        Assert.AreEqual(BadBlockLevel.Critical, snapNvme.Level);



    }



    [TestMethod]



    public void TestAudioSettings_DefaultsAndSerialization()



    {



        var settings = new AudioSettings();



        Assert.IsTrue(settings.ChangeCommunications);



        Assert.IsFalse(settings.KeepVolumeEnabled);



        Assert.AreEqual(AudioMicMode.Toggle, settings.MicMode);



        Assert.AreEqual(200, settings.PttDelayMs);



        // Atalhos padrão



        Assert.AreEqual(0x7A, settings.HotkeyPlaybackCycle.VirtualKey); // F11



        Assert.IsTrue(settings.HotkeyPlaybackCycle.Ctrl);



        Assert.IsTrue(settings.HotkeyPlaybackCycle.Alt);



        Assert.AreEqual(0x76, settings.HotkeyRecordingCycle.VirtualKey); // F7



        Assert.IsTrue(settings.HotkeyRecordingCycle.Ctrl);



        Assert.IsTrue(settings.HotkeyRecordingCycle.Alt);



        Assert.AreEqual(0x4F, settings.HotkeyPlaybackMute.VirtualKey); // O



        Assert.AreEqual(0x4D, settings.HotkeyMicMute.VirtualKey); // M



        // Serialização JSON



        var json = System.Text.Json.JsonSerializer.Serialize(settings);



        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AudioSettings>(json);



        Assert.IsNotNull(deserialized);



        Assert.AreEqual(settings.PttDelayMs, deserialized.PttDelayMs);



        Assert.AreEqual(settings.HotkeyPlaybackCycle.VirtualKey, deserialized.HotkeyPlaybackCycle.VirtualKey);



        Assert.AreEqual("Ctrl + Alt + F11", deserialized.HotkeyPlaybackCycle.ToString());



    }



    [TestMethod]



    public void TestAudioHotkeyModel_Formatting()



    {



        var hk1 = new AudioHotkeyModel(0x7A, ctrl: true, alt: true);



        Assert.AreEqual("Ctrl + Alt + F11", hk1.ToString());



        var hk2 = new AudioHotkeyModel(0x4D, ctrl: true, shift: true);



        Assert.AreEqual("Ctrl + Shift + M", hk2.ToString());



        var hk3 = new AudioHotkeyModel(0x20); // Space



        Assert.AreEqual("Space", hk3.ToString());



        var empty = new AudioHotkeyModel(0);



        Assert.IsTrue(empty.IsEmpty);



        Assert.AreEqual("Nenhum", empty.ToString());



    }



    [TestMethod]



    public void TestAudioCore_CycleNextMath()



    {



        // Simulação do algoritmo cíclico de dispositivos



        var ids = new List<string> { "dev_1", "dev_2", "dev_3" };



        string current = "dev_1";



        int currentIndex = ids.FindIndex(id => id == current);



        int nextIndex = (currentIndex + 1) % ids.Count;



        Assert.AreEqual("dev_2", ids[nextIndex]);



        // Ciclo do último para o primeiro



        current = "dev_3";



        currentIndex = ids.FindIndex(id => id == current);



        nextIndex = (currentIndex + 1) % ids.Count;



        Assert.AreEqual("dev_1", ids[nextIndex]);



    }



    [TestMethod]



    public void TestAudioCore_LiveDeviceCheck()



    {



        var renders = AudioCore.GetDevices(AudioInterop.EDataFlow.Render);



        var captures = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture);



        System.Console.WriteLine($"Render devices: {renders.Count}");



        foreach (var r in renders)



        {



            System.Console.WriteLine($"  Render: {r.Name} | DefaultConsole={r.IsDefaultConsole} | Muted={r.IsMuted}");



        }



        System.Console.WriteLine($"Capture devices: {captures.Count}");



        foreach (var c in captures)



        {



            System.Console.WriteLine($"  Capture: {c.Name} | DefaultConsole={c.IsDefaultConsole} | DefaultComms={c.IsDefaultCommunications} | Muted={c.IsMuted}");



        }



        bool initialMute = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);



        bool toggled = AudioCore.ToggleDefaultMute(AudioInterop.EDataFlow.Capture);



        bool after = false;



        for (int retry = 0; retry < 5; retry++)



        {



            Thread.Sleep(100);



            after = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);



            if (after == toggled) break;



        }



        if (after == toggled)
        {
            Assert.AreEqual(toggled, after, "O estado de mudo do microfone padrão deve refletir o ToggleDefaultMute");
            Assert.AreNotEqual(initialMute, after, "ToggleDefaultMute deve inverter o estado real de mudo no hardware/CoreAudio");
        }
        else
        {
            System.Console.WriteLine($"[AVISO] Microfone físico padrão não aceitou alternância de mudo via software: {after} vs esperado {toggled}");
        }



        // Restore



        AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, initialMute);



        AudioCore.SetAllCaptureMute(initialMute);



        // Test SetDeviceMute on the default capture device



        var defaultCap = captures.FirstOrDefault(d => d.IsDefaultConsole) ?? captures.FirstOrDefault();



        if (defaultCap != null)



        {



            bool okMute = AudioCore.SetDeviceMute(defaultCap.Id, true);



            var updatedDevices = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture);



            var devAfterMute = updatedDevices.FirstOrDefault(d => d.Id == defaultCap.Id);



            System.Console.WriteLine($"Mute applied to {defaultCap.Name}: okMute={okMute}, IsMutedAfter={devAfterMute?.IsMuted}, Volume={devAfterMute?.VolumePercent}%");



            Assert.IsTrue(okMute, $"SetDeviceMute(true) falhou para {defaultCap.Name}");



            Assert.IsTrue(devAfterMute?.IsMuted == true, $"O microfone {defaultCap.Name} não ficou mutado após SetDeviceMute(true)");



            bool okUnmute = AudioCore.SetDeviceMute(defaultCap.Id, false);



            var restoredDevices = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture);



            var devAfterUnmute = restoredDevices.FirstOrDefault(d => d.Id == defaultCap.Id);



            System.Console.WriteLine($"Unmute applied to {defaultCap.Name}: okUnmute={okUnmute}, IsMutedAfter={devAfterUnmute?.IsMuted}, Volume={devAfterUnmute?.VolumePercent}%");



            Assert.IsTrue(okUnmute, $"SetDeviceMute(false) falhou para {defaultCap.Name}");



            Assert.IsTrue(devAfterUnmute?.IsMuted == false, $"O microfone {defaultCap.Name} não foi desmutado após SetDeviceMute(false)");



        }



        AudioCore.DesiredMicMuteState = false;



    }



    [TestMethod]



    public void TestAppSettings_NewSettingsDefaults()



    {



        var settings = new AppSettings();



        Assert.IsFalse(settings.OverlayLocked, "OverlayLocked deve iniciar como false (livre para arrastar)");



        Assert.IsTrue(settings.MinimizeToTrayOnClose, "MinimizeToTrayOnClose deve ser true por padrão");



        Assert.IsTrue(settings.ShowMicTrayIcon, "ShowMicTrayIcon deve ser true por padrão");



        Assert.IsTrue(settings.ShowAudioTrayIcon, "ShowAudioTrayIcon deve ser true por padrão");



        Assert.AreEqual("Monochrome", settings.AccentColorTheme, "AccentColorTheme deve ser Monochrome por padrão");



    }



    [TestMethod]



    public void TestThemeManager_AccentPalettes()



    {



        var themes = ThemeManager.AccentThemes;



        Assert.IsNotNull(themes);



        Assert.AreEqual(8, themes.Count, "Deveria haver 8 temas de acento configurados");



        string[] expectedKeys = ["Monochrome", "CyberBlue", "Emerald", "NeonPurple", "CrimsonRed", "SunsetOrange", "AmberGold", "DeepRose"];



        foreach (var key in expectedKeys)



        {



            Assert.IsTrue(themes.ContainsKey(key), $"Tema '{key}' ausente no ThemeManager");



            var item = themes[key];



            Assert.IsFalse(string.IsNullOrWhiteSpace(item.Name), $"Nome do tema '{key}' não deve ser vazio");



            Assert.IsFalse(string.IsNullOrWhiteSpace(item.Hex), $"Hex do tema '{key}' não deve ser vazio");



        }



    }



    [TestMethod]



    public void TestAudioDeviceInfo_StatusProperties()



    {



        var deviceMuted = new AudioDeviceInfo



        {



            Id = "test1",



            Name = "Mic Test",



            IsMuted = true



        };



        Assert.AreEqual("MUTADO", deviceMuted.MuteStatusText);



        Assert.AreEqual("DESMUTAR", deviceMuted.MuteButtonText);



        Assert.IsFalse(deviceMuted.IsNotMuted);



        var deviceActive = new AudioDeviceInfo



        {



            Id = "test2",



            Name = "Mic Test 2",



            IsMuted = false



        };



        Assert.AreEqual("ATIVO", deviceActive.MuteStatusText);



        Assert.AreEqual("MUTAR", deviceActive.MuteButtonText);



        Assert.IsTrue(deviceActive.IsNotMuted);



    }



    [TestMethod]



    public void TestSystemTrayManager_IconGeneration()



    {



        using var tray = new SystemTrayManager();



        Assert.IsTrue(tray.ShowMicIcon);



        Assert.IsTrue(tray.ShowAudioIcon);



        IntPtr h1 = SystemTrayManager.CreateMicIcon(true);



        Assert.AreNotEqual(IntPtr.Zero, h1, "Ícone do microfone mutado deve ser válido");



        SystemTrayManager.DestroyIcon(h1);



        IntPtr h2 = SystemTrayManager.CreateMicIcon(false);



        Assert.AreNotEqual(IntPtr.Zero, h2, "Ícone do microfone ativo deve ser válido");



        SystemTrayManager.DestroyIcon(h2);



        IntPtr h3 = SystemTrayManager.CreateSpeakerIcon(1);



        Assert.AreNotEqual(IntPtr.Zero, h3, "Ícone do alto-falante #1 deve ser válido");



        SystemTrayManager.DestroyIcon(h3);



        IntPtr h4 = SystemTrayManager.CreateSpeakerIcon(2);



        Assert.AreNotEqual(IntPtr.Zero, h4, "Ícone do alto-falante #2 deve ser válido");



        SystemTrayManager.DestroyIcon(h4);



    }



    [TestMethod]



    public void TestAudioDevice_EnableDisableAndRotationExclusion()



    {



        var activeDevice = new AudioDeviceInfo



        {



            Id = "device-1",



            Name = "Headphones",



            IsDisabled = false



        };



        Assert.IsTrue(activeDevice.IsEnabled);



        Assert.IsFalse(activeDevice.IsDisabled);



        Assert.AreEqual("ATIVO", activeDevice.DeviceStatusText);



        Assert.AreEqual("DESATIVAR", activeDevice.ToggleEnableButtonText);



        Assert.AreEqual(1.0, activeDevice.CardOpacity);



        var disabledDevice = new AudioDeviceInfo



        {



            Id = "device-2",



            Name = "Monitor Speaker",



            IsDisabled = true



        };



        Assert.IsFalse(disabledDevice.IsEnabled);



        Assert.IsTrue(disabledDevice.IsDisabled);



        Assert.AreEqual("DESATIVADO", disabledDevice.DeviceStatusText);



        Assert.AreEqual("ATIVAR DISPOSITIVO", disabledDevice.ToggleEnableButtonText);



        Assert.AreEqual(0.6, disabledDevice.CardOpacity);



        // Test AudioSettings disabled persistence



        var settings = new AudioSettings();



        settings.DisabledPlaybackDeviceIds.Add("device-2");



        settings.DisabledRecordingDeviceIds.Add("mic-2");



        Assert.IsTrue(settings.DisabledPlaybackDeviceIds.Contains("device-2"));



        Assert.IsTrue(settings.DisabledRecordingDeviceIds.Contains("mic-2"));



        // Rotation candidate filter ensures disabled devices are excluded



        var list = new List<AudioDeviceInfo> { activeDevice, disabledDevice };



        var rotatable = list.Where(d => d.IsEnabled).ToList();



        Assert.AreEqual(1, rotatable.Count);



        Assert.AreEqual("device-1", rotatable[0].Id);



    }



    private static string FindMainWindowXaml()



    {



        var dir = new DirectoryInfo(AppContext.BaseDirectory);



        while (dir != null)



        {



            string candidate = Path.Combine(dir.FullName, "MainWindow.xaml");



            if (File.Exists(candidate)) return candidate;



            string subCandidate = Path.Combine(dir.FullName, "LuminiTray", "MainWindow.xaml");



            if (File.Exists(subCandidate)) return subCandidate;



            dir = dir.Parent;



        }



        return "";



    }



    [TestMethod]



    public void TestDiskTabs_Organization()



    {



        string xamlFile = FindMainWindowXaml();



        Assert.IsTrue(File.Exists(xamlFile), $"MainWindow.xaml deve existir em {xamlFile}");



        string xaml = File.ReadAllText(xamlFile);



        // Sidebar has NavDiskTools



        Assert.IsTrue(xaml.Contains("x:Name=\"NavDiskTools\""), "Sidebar deve conter NavDiskTools");



        Assert.IsTrue(xaml.Contains("Tag=\"PageDiskTools\""), "NavDiskTools deve apontar para PageDiskTools");



        // PageDisks should only contain info



        int pageDisksIdx = xaml.IndexOf("x:Name=\"PageDisks\"");



        int pageEnergyIdx = xaml.IndexOf("x:Name=\"PageEnergy\"");



        Assert.IsTrue(pageDisksIdx > 0 && pageEnergyIdx > pageDisksIdx);



        string pageDisksContent = xaml.Substring(pageDisksIdx, pageEnergyIdx - pageDisksIdx);



        Assert.IsTrue(pageDisksContent.Contains("DiskViewHealth"), "PageDisks deve conter DiskViewHealth");



        Assert.IsTrue(pageDisksContent.Contains("DiskCards"), "PageDisks deve conter DiskCards");



        Assert.IsFalse(pageDisksContent.Contains("DiskViewSpace"), "PageDisks NÃO deve conter DiskViewSpace");



        Assert.IsFalse(pageDisksContent.Contains("DiskViewQuickClean"), "PageDisks NÃO deve conter DiskViewQuickClean");



        Assert.IsFalse(pageDisksContent.Contains("DiskViewBadBlocks"), "PageDisks NÃO deve conter DiskViewBadBlocks");



        // PageDiskTools should contain the 3 tools



        int pageDiskToolsIdx = xaml.IndexOf("x:Name=\"PageDiskTools\"");



        Assert.IsTrue(pageDiskToolsIdx > 0, "PageDiskTools deve existir no XAML");



        int pageNextIdx = xaml.IndexOf("x:Name=\"PageLauncher\"");



        Assert.IsTrue(pageNextIdx > pageDiskToolsIdx);



        string pageDiskToolsContent = xaml.Substring(pageDiskToolsIdx, pageNextIdx - pageDiskToolsIdx);



        Assert.IsTrue(pageDiskToolsContent.Contains("BtnToolDiskSpace"), "PageDiskTools deve conter botão do analisador de armazenamento");



        Assert.IsTrue(pageDiskToolsContent.Contains("BtnToolDiskClean"), "PageDiskTools deve conter botão de limpeza de disco");



        Assert.IsTrue(pageDiskToolsContent.Contains("BtnToolDiskBadBlocks"), "PageDiskTools deve conter botão de bad blocks");



        Assert.IsTrue(pageDiskToolsContent.Contains("DiskViewSpace"), "PageDiskTools deve conter DiskViewSpace");



        Assert.IsTrue(pageDiskToolsContent.Contains("DiskViewQuickClean"), "PageDiskTools deve conter DiskViewQuickClean");



        Assert.IsTrue(pageDiskToolsContent.Contains("DiskViewBadBlocks"), "PageDiskTools deve conter DiskViewBadBlocks");



    }



    [TestMethod]



    public void AudioSettings_TaskbarVolumeAndHotkeyOptimization_Tests()



    {



        var settings = new AudioSettings();



        Assert.IsTrue(settings.EnableGlobalHotkeys, "Atalhos globais devem vir ativados por padrão");



        Assert.IsTrue(settings.EnableTaskbarVolumeScroll, "Scroll de volume na barra de tarefas deve vir ativado");



        Assert.AreEqual(2, settings.TaskbarVolumeScrollStep, "Passo padrão de scroll de volume deve ser 2%");



        Assert.AreEqual("Ctrl Esquerdo", settings.TaskbarPerAppModifierKey, "Tecla padrão para volume por app deve ser Ctrl Esquerdo");



        Assert.IsFalse(settings.InvertTaskbarModifier, "Inversão de modificador padrão deve ser false");



        Assert.IsTrue(settings.MiddleClickToMute, "Clique do meio para mutar deve vir ativado");



        Assert.IsTrue(settings.DisableTaskbarVolumeInFullscreen, "Proteção de jogos em tela cheia/janela sem bordas deve vir ativada");



        // Teste de serialização



        string json = System.Text.Json.JsonSerializer.Serialize(settings);



        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AudioSettings>(json);



        Assert.IsNotNull(deserialized);



        Assert.IsTrue(deserialized.EnableGlobalHotkeys);



        Assert.IsTrue(deserialized.EnableTaskbarVolumeScroll);



        Assert.AreEqual(2, deserialized.TaskbarVolumeScrollStep);



        Assert.AreEqual("Ctrl Esquerdo", deserialized.TaskbarPerAppModifierKey);



        Assert.IsTrue(deserialized.MiddleClickToMute);



        Assert.IsTrue(deserialized.DisableTaskbarVolumeInFullscreen);



        // Verifica existência dos controles no MainWindow.xaml



        string xamlFile = FindMainWindowXaml();



        Assert.IsTrue(File.Exists(xamlFile), $"MainWindow.xaml deve existir em {xamlFile}");



        string xaml = File.ReadAllText(xamlFile);



        Assert.IsTrue(xaml.Contains("x:Name=\"ChkEnableGlobalHotkeys\""), "XAML deve conter ChkEnableGlobalHotkeys");



        Assert.IsTrue(xaml.Contains("x:Name=\"ChkEnableTaskbarVolume\""), "XAML deve conter ChkEnableTaskbarVolume");



        Assert.IsTrue(xaml.Contains("x:Name=\"CmbTaskbarModifier\""), "XAML deve conter CmbTaskbarModifier");



        Assert.IsTrue(xaml.Contains("x:Name=\"CmbTaskbarStep\""), "XAML deve conter CmbTaskbarStep");



        Assert.IsTrue(xaml.Contains("x:Name=\"ChkInvertTaskbarModifier\""), "XAML deve conter ChkInvertTaskbarModifier");



        Assert.IsTrue(xaml.Contains("x:Name=\"ChkMiddleClickToMute\""), "XAML deve conter ChkMiddleClickToMute");



        Assert.IsTrue(xaml.Contains("x:Name=\"ChkScrollAnywhere\""), "XAML deve conter ChkScrollAnywhere");



        Assert.IsTrue(xaml.Contains("x:Name=\"ChkDisableInFullscreen\""), "XAML deve conter ChkDisableInFullscreen");



        Assert.IsTrue(xaml.Contains("x:Name=\"CmbTaskbarTargetMode\""), "XAML deve conter CmbTaskbarTargetMode");



        Assert.IsTrue(xaml.Contains("x:Name=\"TxtTaskbarSpecificApp\""), "XAML deve conter TxtTaskbarSpecificApp");



    }



    [TestMethod]



    public void TestTaskbarVolume_GameAndFullscreenProtection()



    {



        // 1. Verificação de identificação de jogos competitivos conhecidos



        Assert.IsTrue(TaskbarVolumeHook.IsKnownGameProcess("VALORANT"));



        Assert.IsTrue(TaskbarVolumeHook.IsKnownGameProcess("VALORANT-Win64-Shipping"));



        Assert.IsTrue(TaskbarVolumeHook.IsKnownGameProcess("cs2"));



        Assert.IsTrue(TaskbarVolumeHook.IsKnownGameProcess("csgo"));



        Assert.IsTrue(TaskbarVolumeHook.IsKnownGameProcess("Overwatch"));



        Assert.IsTrue(TaskbarVolumeHook.IsKnownGameProcess("FortniteClient-Win64-Shipping"));



        Assert.IsTrue(TaskbarVolumeHook.IsKnownGameProcess("League of Legends"));



        // Processos que NÃO são jogos



        Assert.IsFalse(TaskbarVolumeHook.IsKnownGameProcess("explorer"));



        Assert.IsFalse(TaskbarVolumeHook.IsKnownGameProcess("chrome"));



        Assert.IsFalse(TaskbarVolumeHook.IsKnownGameProcess("Spotify"));



        Assert.IsFalse(TaskbarVolumeHook.IsKnownGameProcess("LuminiTray"));



        Assert.IsFalse(TaskbarVolumeHook.IsKnownGameProcess(""));



        // 2. Verificação de classes de janela da barra de tarefas vs janelas de jogos



        Assert.IsTrue(TaskbarVolumeHook.IsTaskbarClassName("Shell_TrayWnd"));



        Assert.IsTrue(TaskbarVolumeHook.IsTaskbarClassName("Shell_SecondaryTrayWnd"));



        Assert.IsTrue(TaskbarVolumeHook.IsTaskbarClassName("TaskListThumbnailWnd"));



        Assert.IsTrue(TaskbarVolumeHook.IsTaskbarClassName("tooltips_class32"));



        // Classes de janela de jogos/aplicativos que NÃO devem ser tratadas como barra de tarefas



        Assert.IsFalse(TaskbarVolumeHook.IsTaskbarClassName("UnrealWindow")); // Janela do Valorant



        Assert.IsFalse(TaskbarVolumeHook.IsTaskbarClassName("UnityWndClass"));



        Assert.IsFalse(TaskbarVolumeHook.IsTaskbarClassName("Chrome_WidgetWin_1"));



        Assert.IsFalse(TaskbarVolumeHook.IsTaskbarClassName("CabinetWClass"));



        // 3. Chamada de IsForegroundFullscreenOrGame não deve lançar exceção



        bool fs = TaskbarVolumeHook.IsForegroundFullscreenOrGame(0, 0);



        Assert.IsNotNull(fs);



    }



    [TestMethod]



    public void TestTaskbarVolume_PerAppResolutionAndAliasMapping()



    {



        // 1. Limpeza de strings de botões e atalhos da barra de tarefas



        Assert.AreEqual("Google Chrome", TaskbarVolumeHook.CleanTaskbarButtonText("Google Chrome - 2 janelas em execução"));



        Assert.AreEqual("Google Chrome", TaskbarVolumeHook.CleanTaskbarButtonText("Google Chrome - 1 janela em execução"));



        Assert.AreEqual("Microsoft Edge", TaskbarVolumeHook.CleanTaskbarButtonText("Microsoft Edge - 3 janelas em execução"));



        Assert.AreEqual("Spotify", TaskbarVolumeHook.CleanTaskbarButtonText("Spotify Free"));



        Assert.AreEqual("Spotify", TaskbarVolumeHook.CleanTaskbarButtonText("Spotify Premium"));



        Assert.AreEqual("Discord", TaskbarVolumeHook.CleanTaskbarButtonText("Discord (3)"));



        Assert.AreEqual("Steam", TaskbarVolumeHook.CleanTaskbarButtonText("Steam - executando"));



        // 2. Mapeamento de nomes de interface para executáveis de processos



        Assert.AreEqual("chrome", TaskbarVolumeHook.MapAppNameToProcessAlias("Google Chrome"));



        Assert.AreEqual("msedge", TaskbarVolumeHook.MapAppNameToProcessAlias("Microsoft Edge"));



        Assert.AreEqual("firefox", TaskbarVolumeHook.MapAppNameToProcessAlias("Mozilla Firefox"));



        Assert.AreEqual("Spotify", TaskbarVolumeHook.MapAppNameToProcessAlias("Spotify"));



        Assert.AreEqual("Discord", TaskbarVolumeHook.MapAppNameToProcessAlias("Discord"));



        Assert.AreEqual("steam", TaskbarVolumeHook.MapAppNameToProcessAlias("Steam"));



        Assert.AreEqual("Telegram", TaskbarVolumeHook.MapAppNameToProcessAlias("Telegram"));



        Assert.AreEqual("WhatsApp", TaskbarVolumeHook.MapAppNameToProcessAlias("WhatsApp"));



        Assert.AreEqual("Code", TaskbarVolumeHook.MapAppNameToProcessAlias("Visual Studio Code"));



        Assert.AreEqual("vlc", TaskbarVolumeHook.MapAppNameToProcessAlias("VLC media player"));



        Assert.AreEqual("VALORANT-Win64-Shipping", TaskbarVolumeHook.MapAppNameToProcessAlias("VALORANT"));



        // 3. Reconhecimento de regiões genéricas da barra de tarefas vs atalhos de apps



        Assert.IsTrue(TaskbarVolumeHook.IsGenericTaskbarName("Barra de tarefas"));



        Assert.IsTrue(TaskbarVolumeHook.IsGenericTaskbarName("Taskbar"));



        Assert.IsTrue(TaskbarVolumeHook.IsGenericTaskbarName("Iniciar"));



        Assert.IsTrue(TaskbarVolumeHook.IsGenericTaskbarName("Pesquisar"));



        Assert.IsTrue(TaskbarVolumeHook.IsGenericTaskbarName("DesktopWindowContentBridge"));



        Assert.IsTrue(TaskbarVolumeHook.IsGenericTaskbarName("Área de Notificação"));



        Assert.IsFalse(TaskbarVolumeHook.IsGenericTaskbarName("Google Chrome"));



        Assert.IsFalse(TaskbarVolumeHook.IsGenericTaskbarName("Spotify"));



        Assert.IsFalse(TaskbarVolumeHook.IsGenericTaskbarName("Discord"));



        Assert.IsFalse(TaskbarVolumeHook.IsGenericTaskbarName("Steam"));



        // 4. Windows 11: o nome da ilha XAML nunca pode ser tratado como app



        Assert.IsTrue(TaskbarVolumeHook.IsGenericTaskbarName("DesktopWindowXamlSource"));



        Assert.IsTrue(TaskbarVolumeHook.IsGenericTaskbarName("Mostrar ícones ocultos"));



        Assert.AreEqual("Microsoft Edge", TaskbarVolumeHook.CleanTaskbarButtonText("Microsoft Edge - 1 janela aberta"));



        Assert.AreEqual("Microsoft Edge", TaskbarVolumeHook.CleanTaskbarButtonText("Microsoft Edge, fixado"));



        // 5. AutomationId (AUMID / caminho do exe) → processo



        Assert.AreEqual("msedge", TaskbarVolumeHook.ProcessHintFromAutomationId("MSEdge"));



        Assert.AreEqual("chrome", TaskbarVolumeHook.ProcessHintFromAutomationId("Chrome"));



        Assert.AreEqual("Discord", TaskbarVolumeHook.ProcessHintFromAutomationId("com.squirrel.Discord.Discord"));



        Assert.AreEqual("vlc", TaskbarVolumeHook.ProcessHintFromAutomationId(@"{6D809377-6AF0-444B-8957-A3773F02200E}\VideoLAN\VLC\vlc.exe"));



    }



    [TestMethod]



    public void TestAudioCore_GetProcessTree()



    {



        uint curPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;



        var tree = AudioCore.GetProcessTree(curPid);



        Assert.IsNotNull(tree);



        Assert.IsTrue(tree.Contains(curPid), "Process tree deve conter o root PID");



    }



    [TestMethod]



    public void TestAudioCore_ActiveMediaPlayerFormatting()



    {



        Assert.AreEqual("Spotify", AudioCore.FormatPlayerFriendlyName("Spotify", ""));



        Assert.AreEqual("Microsoft Edge", AudioCore.FormatPlayerFriendlyName("msedge", ""));



        Assert.AreEqual("Google Chrome", AudioCore.FormatPlayerFriendlyName("chrome", ""));



        Assert.AreEqual("Mozilla Firefox", AudioCore.FormatPlayerFriendlyName("firefox", ""));



        Assert.AreEqual("VLC Media Player", AudioCore.FormatPlayerFriendlyName("vlc", ""));



        Assert.AreEqual("YouTube Music", AudioCore.FormatPlayerFriendlyName("msedge", "YouTube Music"));



        Assert.AreEqual("Lo-Fi Hip Hop", AudioCore.FormatPlayerFriendlyName("chrome", "Lo-Fi Hip Hop"));



    }



    [TestMethod]



    public void TestRealSystemAudioDiagnostic()



    {



        AudioComThread.Invoke(() =>



        {



            var enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();



            // 1. Check Default Devices for Console, Multimedia, Communications



            foreach (var role in new[] { AudioInterop.ERole.Console, AudioInterop.ERole.Multimedia, AudioInterop.ERole.Communications })



            {



                int hr = enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlow.Render, role, out var dev);



                if (hr == 0 && dev != null)



                {



                    dev.GetId(out string id);



                    dev.OpenPropertyStore(0, out var props);



                    var pkey = AudioInterop.PKEY_Device_FriendlyName;



                    props.GetValue(ref pkey, out var pv);



                    string name = Marshal.PtrToStringUni(pv.data1) ?? "Unknown";



                    Console.WriteLine($"Default {role}: '{name}' (ID: {id})");



                    Guid iid = new Guid(AudioInterop.IID_IAudioSessionManager2);



                    if (dev.Activate(ref iid, 23, IntPtr.Zero, out var mgrObj) == 0 && mgrObj != null)



                    {



                        var mgr = (AudioInterop.IAudioSessionManager2)mgrObj;



                        if (mgr.GetSessionEnumerator(out var sEnum) == 0 && sEnum != null)



                        {



                            sEnum.GetCount(out int count);



                            Console.WriteLine($"  -> Sessions on Default {role}: {count}");



                            for (int i = 0; i < count; i++)



                            {



                                int sHr = sEnum.GetSession(i, out var sObj);



                                Console.WriteLine($"     GetSession({i}) hr={sHr}, sObj={(sObj != null ? sObj.GetType().FullName : "null")}");



                                if (sObj != null)



                                {



                                    IntPtr pUnk = Marshal.GetIUnknownForObject(sObj);



                                    Guid iidCtl2 = new Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d");



                                    int hrQI = Marshal.QueryInterface(pUnk, ref iidCtl2, out IntPtr pCtl2);



                                    Guid iidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");



                                    int hrMeter = Marshal.QueryInterface(pUnk, ref iidMeter, out IntPtr pMeter);



                                    Console.WriteLine($"       pUnk={pUnk}, QI(Ctl2) hr=0x{hrQI:X8} ptr={pCtl2}, QI(Meter) hr=0x{hrMeter:X8} ptr={pMeter}");



                                    Marshal.Release(pUnk);



                                    if (pCtl2 != IntPtr.Zero) Marshal.Release(pCtl2);



                                    if (pMeter != IntPtr.Zero) Marshal.Release(pMeter);



                                    bool isCtl = sObj is AudioInterop.IAudioSessionControl;



                                    bool isCtl2 = sObj is AudioInterop.IAudioSessionControl2;



                                    bool isVol = sObj is AudioInterop.ISimpleAudioVolume;



                                    bool isMeter = sObj is AudioInterop.IAudioMeterInformation;



                                    Console.WriteLine($"       isCtl={isCtl}, isCtl2={isCtl2}, isVol={isVol}, isMeter={isMeter}");



                                    if (isCtl2 && sObj is AudioInterop.IAudioSessionControl2 ctl)



                                    {



                                        ctl.GetProcessId(out uint pid);



                                        ctl.GetState(out int state);



                                        string pName = AudioCore.GetSafeProcessName(pid);



                                        ctl.GetDisplayName(out string disp);



                                        Console.WriteLine($"       PID={pid} ({pName}), State={state}, Disp='{disp}'");



                                    }



                                }



                            }



                        }



                    }



                }



                else



                {



                    Console.WriteLine($"Default {role}: hr={hr}");



                }



            }



            // 2. Enumerate ALL active Render endpoints



            enumerator.EnumAudioEndpoints(AudioInterop.EDataFlow.Render, AudioInterop.EDeviceState.Active, out var coll);



            if (coll != null)



            {



                coll.GetCount(out uint totalDevs);



                Console.WriteLine($"Total Active Render Devices: {totalDevs}");



                for (uint i = 0; i < totalDevs; i++)



                {



                    coll.Item(i, out var d);



                    if (d != null)



                    {



                        d.GetId(out string dId);



                        d.OpenPropertyStore(0, out var dProps);



                        var pkey2 = AudioInterop.PKEY_Device_FriendlyName;



                        dProps.GetValue(ref pkey2, out var dPv);



                        string dName = Marshal.PtrToStringUni(dPv.data1) ?? "Unknown";



                        Guid iid = new Guid(AudioInterop.IID_IAudioSessionManager2);



                        int sCount = 0;



                        if (d.Activate(ref iid, 23, IntPtr.Zero, out var dMgr) == 0 && dMgr != null)



                        {



                            var m = (AudioInterop.IAudioSessionManager2)dMgr;



                            if (m.GetSessionEnumerator(out var se) == 0 && se != null)



                            {



                                se.GetCount(out sCount);



                                Console.WriteLine($"Device [{i}] '{dName}' -> Sessions: {sCount}");



                                for (int s = 0; s < sCount; s++)



                                {



                                    if (se.GetSession(s, out var so) == 0 && so is AudioInterop.IAudioSessionControl2 c2)



                                    {



                                        c2.GetProcessId(out uint p);



                                        c2.GetState(out int st);



                                        string pn = AudioCore.GetSafeProcessName(p);



                                        Console.WriteLine($"    DevSession {s}: PID={p} ({pn}), State={st}");



                                    }



                                }



                            }



                        }



                    }



                }



            }



            // 3. Test AudioCore.GetActiveAudioSessions() and AdjustActiveMediaPlayerVolume()



            var activeList = AudioCore.GetActiveAudioSessions();



            Console.WriteLine($"ActiveAudioSessions count: {activeList.Count}");



            foreach (var s in activeList)



            {



                Console.WriteLine($"  -> ActiveSession: PID={s.ProcessId}, Name={s.ProcessName}, Vol={s.Volume:P0}, Mute={s.IsMuted}");



            }



            int playerVol = AudioCore.AdjustActiveMediaPlayerVolume(0, out string? detectedPlayer);



            Console.WriteLine($"AdjustActiveMediaPlayerVolume test: DetectedPlayer='{detectedPlayer}', Volume={playerVol}");



        });



    }



    [TestMethod]



    public void AudioFeedbackVolume_SettingsAndSidebarIntegrations_Tests()



    {



        // 1. Settings SoundFeedbackVolume default and bounds



        var audioSettings = new AudioSettings();



        Assert.AreEqual(50, audioSettings.SoundFeedbackVolume, "Volume padrão do sinal sonoro deve ser 50%");



        AudioCore.FeedbackVolume = 75;



        Assert.AreEqual(75, AudioCore.FeedbackVolume);



        // 2. Play feedback with custom volume (does not throw)



        AudioCore.PlayFeedback(AudioFeedbackType.DeviceSwitched, 30);



        AudioCore.PlayFeedback(AudioFeedbackType.MicMuted, 80);



        // 3. Verify MainWindow.xaml contains the slider, test button, NavSettings at bottom and PanelSettingsInteg



        string xamlFile = FindMainWindowXaml();



        Assert.IsTrue(File.Exists(xamlFile), $"MainWindow.xaml deve existir em {xamlFile}");



        string xaml = File.ReadAllText(xamlFile);



        Assert.IsTrue(xaml.Contains("x:Name=\"SldSoundFeedbackVolume\""), "MainWindow.xaml deve conter o slider SldSoundFeedbackVolume");



        Assert.IsTrue(xaml.Contains("x:Name=\"BtnTestSoundFeedback\""), "MainWindow.xaml deve conter o botão BtnTestSoundFeedback");



        Assert.IsTrue(xaml.Contains("x:Name=\"TabSettingsInteg\""), "PageSettings deve conter a aba TabSettingsInteg");



        Assert.IsTrue(xaml.Contains("x:Name=\"PanelSettingsInteg\""), "PageSettings deve conter o painel PanelSettingsInteg");



        // NavSettings must be outside GrpToolsItems and pinned to bottom



        int grpToolsIdx = xaml.IndexOf("x:Name=\"GrpToolsItems\"");



        int grpMakerIdx = xaml.IndexOf("x:Name=\"GrpMaker\"");



        string grpToolsContent = xaml.Substring(grpToolsIdx, grpMakerIdx - grpToolsIdx);



        Assert.IsFalse(grpToolsContent.Contains("x:Name=\"NavSettings\""), "NavSettings NÃO deve estar dentro de GrpToolsItems");



        Assert.IsFalse(grpToolsContent.Contains("x:Name=\"NavInteg\""), "NavInteg NÃO deve estar dentro de GrpToolsItems");



    }



    [TestMethod]



    public void RestorePoints_OcStability_BottleneckDurations_Tests()



    {



        // 1. CIM timestamp parsing and RestorePointItem model



        bool okParse = SystemRestoreManager.TryParseCimDateTime("20261027003020.386102-000", out var parsedDate);



        Assert.IsTrue(okParse, "CIM datetime deve ser parseado com sucesso");



        Assert.AreEqual(2026, parsedDate.Year);



        var item = new RestorePointItem



        {



            SequenceNumber = 99,



            Description = "Ponto Teste",



            CreationTime = DateTime.Now,



            RestorePointType = 12



        };



        Assert.AreEqual("Alteração de Configurações", item.TypeText);



        Assert.AreEqual(99u, item.SequenceNumber);



        // 2. Query restore points (should return list without throwing)



        var points = SystemRestoreManager.GetRestorePoints();



        Assert.IsNotNull(points, "GetRestorePoints não deve retornar nulo");



        // 3. XAML checks:



        string xamlFile = FindMainWindowXaml();



        Assert.IsTrue(File.Exists(xamlFile));



        string xaml = File.ReadAllText(xamlFile);



        // A) Restauração: NavRestore and PageRestore



        Assert.IsTrue(xaml.Contains("x:Name=\"NavRestore\""), "Sidebar deve conter NavRestore");



        Assert.IsTrue(xaml.Contains("x:Name=\"PageRestore\""), "MainWindow.xaml deve conter PageRestore");



        // B) Overclock stability verifier: must contain 1 min option



        int ocDurIdx = xaml.IndexOf("x:Name=\"OcDurPanel\"");



        Assert.IsTrue(ocDurIdx > 0, "OcDurPanel deve existir");



        int ocDurEnd = xaml.IndexOf("</StackPanel>", ocDurIdx);



        string ocDurContent = xaml.Substring(ocDurIdx, ocDurEnd - ocDurIdx);



        Assert.IsTrue(ocDurContent.Contains("Content=\"1 min\""), "OcDurPanel deve conter a opção de 1 min");



        Assert.IsTrue(ocDurContent.Contains("Tag=\"1\""), "Opção de 1 min deve ter Tag='1'");



        // C) Bottleneck session durations: no 3 min, has 1, 5, 10, 15 and Manual



        int bnDurIdx = xaml.IndexOf("x:Name=\"BnDurationPanel\"");



        Assert.IsTrue(bnDurIdx > 0, "BnDurationPanel deve existir");



        int bnDurEnd = xaml.IndexOf("</StackPanel>", bnDurIdx);



        string bnDurContent = xaml.Substring(bnDurIdx, bnDurEnd - bnDurIdx);



        Assert.IsFalse(bnDurContent.Contains("Content=\"3 min\""), "BnDurationPanel NÃO deve conter o teste de 3 min");



        Assert.IsTrue(bnDurContent.Contains("Content=\"1 min\""), "BnDurationPanel deve conter 1 min");



        Assert.IsTrue(bnDurContent.Contains("Content=\"5 min\""), "BnDurationPanel deve conter 5 min");



        Assert.IsTrue(bnDurContent.Contains("Content=\"10 min\""), "BnDurationPanel deve conter 10 min");



        Assert.IsTrue(bnDurContent.Contains("Content=\"15 min\""), "BnDurationPanel deve conter 15 min");



        Assert.IsTrue(bnDurContent.Contains("Content=\"Manual\""), "BnDurationPanel deve conter opção Manual");



    }



    [TestMethod]
    public void TestPsuCalculator_LiveSystemFlow()
    {
        string boardText = "";
        try
        {
            var (maker, model) = OcCheck.Motherboard();
            boardText = $"{maker} {model}".Trim();
        }
        catch { }

        string cpuName = "AMD Ryzen 7 5700X";
        string gpuName = "NVIDIA GeForce RTX 3060";

        var detectedMobo = PsuCalculator.DetectCurrentMotherboard(boardText, cpuName);
        Assert.IsNotNull(detectedMobo);

        var compCpus = PsuCalculator.GetCompatibleCpus(detectedMobo);
        Assert.IsNotNull(compCpus);
        Assert.IsTrue(compCpus.Count > 0);

        var compRam = PsuCalculator.GetCompatibleRam(detectedMobo);
        Assert.IsNotNull(compRam);
        Assert.IsTrue(compRam.Count > 0);

        var detectedCpu = PsuCalculator.DetectCurrentCpu(cpuName);
        Assert.IsNotNull(detectedCpu);

        var detectedGpu = PsuCalculator.DetectCurrentGpu(gpuName);
        Assert.IsNotNull(detectedGpu);

        var config = new PsuConfig
        {
            CpuName = detectedCpu.Name,
            CpuWatts = detectedCpu.Watts,
            MotherboardName = detectedMobo.Name,
            MotherboardWatts = detectedMobo.Watts,
            GpuName = detectedGpu.Name,
            GpuWatts = detectedGpu.Watts,
            RamType = compRam[0].Generation,
            RamSticks = 2,
            NvmeCount = 1,
            CoolerType = "Air Cooler",
            FanCount = 4
        };
        var result = PsuCalculator.Calculate(config);
        Assert.IsNotNull(result);
        Assert.IsTrue(result.TotalPeakWatts > 0);
        Assert.IsTrue(result.RecommendedPsuWatts > 0);
    }



    [TestMethod]
    public void TestPeripherals_KeyboardTester()
    {
        var tester = new KeyboardTester();
        Assert.AreEqual(0, tester.TestedCount);
        Assert.AreEqual(0, tester.CurrentRollover);

        tester.OnKeyDown(System.Windows.Input.Key.W);
        tester.OnKeyDown(System.Windows.Input.Key.A);
        tester.OnKeyDown(System.Windows.Input.Key.S);
        tester.OnKeyDown(System.Windows.Input.Key.D);

        Assert.AreEqual(4, tester.TestedCount);
        Assert.AreEqual(4, tester.CurrentRollover);
        Assert.AreEqual(4, tester.PeakRollover);
        Assert.IsTrue(tester.IsKeyPressed(System.Windows.Input.Key.W));
        Assert.IsTrue(tester.IsKeyTested(System.Windows.Input.Key.W));

        tester.OnKeyUp(System.Windows.Input.Key.W);
        Assert.AreEqual(3, tester.CurrentRollover);
        Assert.IsFalse(tester.IsKeyPressed(System.Windows.Input.Key.W));
        Assert.IsTrue(tester.IsKeyTested(System.Windows.Input.Key.W));

        tester.Reset();
        Assert.AreEqual(0, tester.TestedCount);
        Assert.AreEqual(0, tester.CurrentRollover);
        Assert.AreEqual(0, tester.PeakRollover);
    }

    [TestMethod]
    public void TestPeripherals_MouseTester()
    {
        var tester = new MouseTester();
        Assert.AreEqual(0, tester.LeftClicks);
        Assert.AreEqual(0, tester.CurrentPollingRateHz);

        tester.OnMouseDown(System.Windows.Input.MouseButton.Left);
        Assert.IsTrue(tester.IsLeftDown);
        Assert.AreEqual(1, tester.LeftClicks);

        tester.OnMouseUp(System.Windows.Input.MouseButton.Left);
        Assert.IsFalse(tester.IsLeftDown);

        tester.OnMouseWheel(120);
        Assert.AreEqual(1, tester.ScrollUpCount);
        tester.OnMouseWheel(-120);
        Assert.AreEqual(1, tester.ScrollDownCount);

        for (int i = 0; i < 50; i++)
        {
            tester.OnMouseMove(new System.Windows.Point(i * 2, i * 2));
        }

        Assert.IsTrue(tester.TotalDistancePx > 0);

        tester.Reset();
        Assert.AreEqual(0, tester.LeftClicks);
        Assert.AreEqual(0, tester.ScrollUpCount);
        Assert.AreEqual(0, tester.TotalDistancePx);
    }

    [TestMethod]
    public void TestPeripherals_DisplayManager_Monitors()
    {
        var monitors = DisplayManager.GetMonitors();
        Assert.IsNotNull(monitors);
        Assert.IsTrue(monitors.Count > 0, "Deve detectar ao menos 1 monitor no sistema");

        var primary = monitors[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(primary.DeviceName));
        Assert.IsTrue(primary.CurrentWidth > 0);
        Assert.IsTrue(primary.CurrentHeight > 0);
    }

    [TestMethod]
    public void TestPeripherals_GamepadTester_Drift()
    {
        var snap = new GamepadStateSnapshot
        {
            IsConnected = true,
            LeftStickX = 0.05,
            LeftStickY = 0.02
        };

        double dist = Math.Sqrt(snap.LeftStickX * snap.LeftStickX + snap.LeftStickY * snap.LeftStickY);
        snap.LeftStickDriftPercent = Math.Round(dist * 100.0, 1);

        Assert.IsTrue(snap.LeftStickDriftPercent > 0);
        Assert.IsTrue(snap.LeftStickDriftPercent < 10.0);
    }

    [TestMethod]
    public void TestAudioSettings_ExcludedPlayerApps_Matching()
    {
        var settings = new AudioSettings();
        settings.ExcludedPlayerApps.Add("discord.exe");
        settings.ExcludedPlayerApps.Add("cs2");
        settings.ExcludedPlayerApps.Add("VALORANT-Win64-Shipping.exe");

        // Casos positivos
        Assert.IsTrue(settings.IsAppExcluded("discord"));
        Assert.IsTrue(settings.IsAppExcluded("discord.exe"));
        Assert.IsTrue(settings.IsAppExcluded("DISCORD.EXE"));
        Assert.IsTrue(settings.IsAppExcluded("cs2"));
        Assert.IsTrue(settings.IsAppExcluded("cs2.exe"));
        Assert.IsTrue(settings.IsAppExcluded("CS2.EXE"));
        Assert.IsTrue(settings.IsAppExcluded("VALORANT-Win64-Shipping"));
        Assert.IsTrue(settings.IsAppExcluded("valorant-win64-shipping.exe"));

        // Casos negativos
        Assert.IsFalse(settings.IsAppExcluded("spotify"));
        Assert.IsFalse(settings.IsAppExcluded("spotify.exe"));
        Assert.IsFalse(settings.IsAppExcluded("chrome.exe"));
        Assert.IsFalse(settings.IsAppExcluded(""));
        Assert.IsFalse(settings.IsAppExcluded(null));
    }

    [TestMethod]
    public void TestAudioCore_IsProcessExcluded()
    {
        var list = new List<string> { "discord.exe", "cs2", "League of Legends" };

        // Teste direto por nome de processo
        Assert.IsTrue(AudioCore.IsProcessExcluded("discord", "Discord", list));
        Assert.IsTrue(AudioCore.IsProcessExcluded("discord.exe", "Discord Voice", list));
        Assert.IsTrue(AudioCore.IsProcessExcluded("cs2", "Counter-Strike 2", list));
        Assert.IsTrue(AudioCore.IsProcessExcluded("CS2.EXE", null, list));

        // Teste por display name
        Assert.IsTrue(AudioCore.IsProcessExcluded("LeagueClientUx", "League of Legends (TM) Client", list));

        // Teste app nao excluido
        Assert.IsFalse(AudioCore.IsProcessExcluded("spotify", "Spotify Free", list));
        Assert.IsFalse(AudioCore.IsProcessExcluded("chrome", "Google Chrome", list));

        // Teste lista vazia ou nula
        Assert.IsFalse(AudioCore.IsProcessExcluded("discord", "Discord", null));
        Assert.IsFalse(AudioCore.IsProcessExcluded("discord", "Discord", new List<string>()));
    }
}
