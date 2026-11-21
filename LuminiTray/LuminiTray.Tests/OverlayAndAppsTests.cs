using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuminiTray.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class OverlayAndAppsTests
{
    [TestMethod]
    public void TestAppInstaller_CatalogContainsExpectedCategories()
    {
        var catalog = AppInstaller.Catalog();
        Assert.IsTrue(catalog.Count >= 20, $"Catálogo deve conter pelo menos 20 apps, mas contém {catalog.Count}");

        var categories = catalog.Select(a => a.Category).Distinct().ToList();
        CollectionAssert.Contains(categories, "Jogos", "Catálogo deve conter a categoria Jogos");
        CollectionAssert.Contains(categories, "Comunicação", "Catálogo deve conter a categoria Comunicação");
        CollectionAssert.Contains(categories, "Navegação", "Catálogo deve conter a categoria Navegação");
        CollectionAssert.Contains(categories, "Utilitários", "Catálogo deve conter a categoria Utilitários");
    }

    [TestMethod]
    public void TestAppInstaller_AllAppsHaveValidOfficialProperties()
    {
        var catalog = AppInstaller.Catalog();
        var wingetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in catalog)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.Name), "App deve possuir Name");
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.Category), $"App {app.Name} deve possuir Category");
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.Publisher), $"App {app.Name} deve possuir Publisher");
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.WingetId), $"App {app.Name} deve possuir WingetId");
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.DisplayPrefix), $"App {app.Name} deve possuir DisplayPrefix");

            bool added = wingetIds.Add(app.WingetId);
            Assert.IsTrue(added, $"WingetId duplicado encontrado no catálogo: {app.WingetId}");
        }
    }

    [TestMethod]
    public void TestAppInstaller_CategoryDistribution()
    {
        var catalog = AppInstaller.Catalog();
        var jogos = catalog.Where(a => a.Category == "Jogos").ToList();
        var comunicacao = catalog.Where(a => a.Category == "Comunicação").ToList();
        var navegacao = catalog.Where(a => a.Category == "Navegação").ToList();
        var utilitarios = catalog.Where(a => a.Category == "Utilitários").ToList();

        Assert.IsTrue(jogos.Count >= 5, "Jogos deve ter pelo menos 5 apps");
        Assert.IsTrue(comunicacao.Count >= 5, "Comunicação deve ter pelo menos 5 apps");
        Assert.IsTrue(navegacao.Count >= 4, "Navegação deve ter pelo menos 4 apps");
        Assert.IsTrue(utilitarios.Count >= 6, "Utilitários deve ter pelo menos 6 apps");
    }

    [TestMethod]
    public void TestAppSettings_OverlayNewPropertiesSerialization()
    {
        var settings = new AppSettings
        {
            OverlayWidth = 320,
            OverlayHeight = 180,
            OverlayOpacity = 0.75,
            OverlayLocked = false
        };

        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(320, restored.OverlayWidth, 0.001);
        Assert.AreEqual(180, restored.OverlayHeight, 0.001);
        Assert.AreEqual(0.75, restored.OverlayOpacity, 0.001);
        Assert.IsFalse(restored.OverlayLocked);
        Assert.IsTrue(restored.OverlayTextOutlineEnabled);
        Assert.AreEqual(3.0, restored.OverlayTextOutlineThickness, 0.001);
        Assert.AreEqual(0.90, restored.OverlayTextOutlineOpacity, 0.001);
        Assert.IsTrue(restored.OverlayTextOutlineAutoOnly);
    }

    [TestMethod]
    public void TestOverlay_OpacityClamping()
    {
        double op1 = Math.Clamp(1.20, 0.20, 1.00);
        double op2 = Math.Clamp(0.05, 0.20, 1.00);
        double op3 = Math.Clamp(0.85, 0.20, 1.00);

        Assert.AreEqual(1.00, op1, 0.001);
        Assert.AreEqual(0.20, op2, 0.001);
        Assert.AreEqual(0.85, op3, 0.001);
    }

    private static readonly Lazy<System.Windows.Threading.Dispatcher> _staDispatcher = new(() =>
    {
        System.Windows.Threading.Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    });

    private static void RunOnSta(Action action)
    {
        Exception? captured = null;
        _staDispatcher.Value.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        if (captured != null)
            ExceptionDispatchInfo.Capture(captured).Throw();
    }

    [TestMethod]
    public void TestOutlinedTextBlock_MeasureAndInheritedProperties()
    {
        RunOnSta(() =>
        {
            var panel = new StackPanel();
            TextElement.SetFontFamily(panel, new FontFamily("Consolas"));
            TextElement.SetFontSize(panel, 18.0);
            TextElement.SetForeground(panel, Brushes.White);

            var tb = new OutlinedTextBlock
            {
                Text = "144 FPS",
                Stroke = Brushes.Black,
                StrokeThickness = 2.5
            };
            panel.Children.Add(tb);

            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.IsTrue(tb.DesiredSize.Width > 0, "Largura medida deve ser > 0 para texto não vazio");
            Assert.IsTrue(tb.DesiredSize.Height > 0, "Altura medida deve ser > 0");
            Assert.AreEqual(18.0, tb.FontSize, 0.001);
            Assert.AreEqual(Brushes.White, tb.Foreground);

            tb.Text = string.Empty;
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.AreEqual(0.0, tb.DesiredSize.Width, 0.001);
        });
    }

    [TestMethod]
    public void TestOverlayWindow_TextOutlineToggleAndAutoOnlyLogic()
    {
        RunOnSta(() =>
        {
            var win = new OverlayWindow();
            try
            {
                // Desabilitado: nunca deve aplicar contorno, mesmo com fundo transparente e autoOnly = false
                win.SetBackgroundOpacity(0.10);
                win.UpdateTextOutline(enabled: false, thickness: 4.0, opacity: 0.90, autoOnly: false);
                Assert.AreEqual(0.0, (double)win.Resources["OverlayStrokeThickness"], 0.001);
                Assert.AreEqual(Brushes.Transparent, win.Resources["OverlayStrokeBrush"]);

                // Habilitado + autoOnly = true + fundo opaco (>= 0.50): não deve aplicar
                win.SetBackgroundOpacity(0.50);
                win.UpdateTextOutline(enabled: true, thickness: 4.0, opacity: 0.90, autoOnly: true);
                Assert.AreEqual(0.0, (double)win.Resources["OverlayStrokeThickness"], 0.001);
                Assert.AreEqual(Brushes.Transparent, win.Resources["OverlayStrokeBrush"]);

                // Habilitado + autoOnly = true + fundo transparente (< 0.50): deve aplicar
                win.SetBackgroundOpacity(0.49);
                Assert.AreEqual(4.0, (double)win.Resources["OverlayStrokeThickness"], 0.001);
                var brush = win.Resources["OverlayStrokeBrush"] as SolidColorBrush;
                Assert.IsNotNull(brush);
                Assert.AreEqual(0, brush.Color.R);
                Assert.AreEqual(0, brush.Color.G);
                Assert.AreEqual(0, brush.Color.B);
                Assert.AreEqual((byte)Math.Round(0.90 * 255), brush.Color.A);

                // Habilitado + autoOnly = false + fundo opaco: deve aplicar
                win.SetBackgroundOpacity(0.85);
                win.UpdateTextOutline(enabled: true, thickness: 2.5, opacity: 0.50, autoOnly: false);
                Assert.AreEqual(2.5, (double)win.Resources["OverlayStrokeThickness"], 0.001);
                var brush2 = win.Resources["OverlayStrokeBrush"] as SolidColorBrush;
                Assert.IsNotNull(brush2);
                Assert.AreEqual((byte)Math.Round(0.50 * 255), brush2.Color.A);
            }
            finally
            {
                win.Close();
            }
        });
    }

    [TestMethod]
    public void TestMiniTaskManagerWindow_TextOutlineToggleAndAutoOnlyLogic()
    {
        RunOnSta(() =>
        {
            if (Application.Current == null)
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var ghostStyle = new Style(typeof(Button));
                ghostStyle.Seal();
                app.Resources["GhostButton"] = ghostStyle;
            }

            var win = new MiniTaskManagerWindow();
            try
            {
                // Desabilitado: nunca deve aplicar contorno, mesmo com fundo transparente
                win.SetBackgroundOpacity(0.0);
                win.UpdateTextOutline(enabled: false, thickness: 3.5, opacity: 0.85, autoOnly: true);
                Assert.AreEqual(0.0, (double)win.Resources["OverlayStrokeThickness"], 0.001);
                Assert.AreEqual(Brushes.Transparent, win.Resources["OverlayStrokeBrush"]);

                // Habilitado + autoOnly = true + fundo transparente: deve aplicar
                win.UpdateTextOutline(enabled: true, thickness: 3.5, opacity: 0.85, autoOnly: true);
                Assert.AreEqual(3.5, (double)win.Resources["OverlayStrokeThickness"], 0.001);
                var brush = win.Resources["OverlayStrokeBrush"] as SolidColorBrush;
                Assert.IsNotNull(brush);
                Assert.AreEqual((byte)Math.Round(0.85 * 255), brush.Color.A);

                // Habilitado + autoOnly = true + fundo opaco: deve remover
                win.SetBackgroundOpacity(0.90);
                Assert.AreEqual(0.0, (double)win.Resources["OverlayStrokeThickness"], 0.001);
                Assert.AreEqual(Brushes.Transparent, win.Resources["OverlayStrokeBrush"]);
            }
            finally
            {
                win.Close();
            }
        });
    }

    [TestMethod]
    public void TestOutlinedTextBlock_RenderProducesPixelsAndPreservesUnfrozenStrokeBrush()
    {
        RunOnSta(() =>
        {
            var unfrozenBrush = new SolidColorBrush(Colors.Black);
            var tb = new OutlinedTextBlock
            {
                Text = "144 FPS",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 24,
                Foreground = Brushes.White,
                Stroke = unfrozenBrush,
                StrokeThickness = 3.0
            };

            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tb.Arrange(new Rect(tb.DesiredSize));

            int w = Math.Max(1, (int)Math.Ceiling(tb.DesiredSize.Width));
            int h = Math.Max(1, (int)Math.Ceiling(tb.DesiredSize.Height));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(tb);

            var pixels = new byte[w * h * 4];
            rtb.CopyPixels(pixels, w * 4, 0);
            bool hasNonTransparentPixel = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 0)
                {
                    hasNonTransparentPixel = true;
                    break;
                }
            }
            Assert.IsTrue(hasNonTransparentPixel, "OnRender com contorno deve produzir pixels visíveis");
            Assert.IsFalse(unfrozenBrush.IsFrozen, "OnRender não deve congelar o Brush mutável do chamador");

            // Fast-path sem contorno (StrokeThickness = 0)
            tb.StrokeThickness = 0.0;
            tb.Arrange(new Rect(tb.DesiredSize));
            var rtbNoStroke = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtbNoStroke.Render(tb);
            var pixelsNoStroke = new byte[w * h * 4];
            rtbNoStroke.CopyPixels(pixelsNoStroke, w * 4, 0);
            Assert.IsTrue(pixelsNoStroke.Skip(3).Where((_, idx) => idx % 4 == 0).Any(a => a > 0),
                "OnRender sem contorno (fast-path DrawText) deve produzir pixels visíveis");
        });
    }

    [TestMethod]
    public void TestFanChannel_ReadOnlyIgnoresPwmAndRestore()
    {
        var ch = new FanChannel
        {
            Id = "RO_1",
            Name = "CPU Fan (Somente Leitura)",
            SourceType = FanSourceType.Motherboard,
            IsReadOnly = true,
            CurrentPwm = 42f,
            TargetPwm = 42f
        };

        ch.SetPwm(95f);
        Assert.AreEqual(42f, ch.CurrentPwm, 0.001f);
        Assert.AreEqual(42f, ch.TargetPwm, 0.001f);

        ch.IsManual = true;
        ch.IsLocked100 = true;
        ch.RestoreDefault();
        Assert.IsFalse(ch.IsManual);
        Assert.IsFalse(ch.IsLocked100);
    }

    [TestMethod]
    public void TestNotebookThermalBackend_ComputeThermalTarget()
    {
        var quiet = FanCurve.CreateQuiet();
        var (coolLow, maxLow) = NotebookThermalBackend.ComputeThermalTarget(40.0, quiet);
        Assert.AreEqual(0u, coolLow, "Temperatura baixa em curva silenciosa deve usar política Passiva (0)");
        Assert.IsTrue(maxLow <= 85u, "Curva silenciosa deve limitar teto de CPU");

        var perf = FanCurve.CreatePerformance();
        var (coolHigh, maxHigh) = NotebookThermalBackend.ComputeThermalTarget(85.0, perf);
        Assert.AreEqual(1u, coolHigh, "Temperatura alta em curva performance deve usar política Ativa (1)");
        Assert.AreEqual(100u, maxHigh, "Curva performance em alta temperatura deve liberar 100% da CPU");
    }

    [TestMethod]
    public void TestTweaksTab_NavigationTitleAndOfficeHotkeysTweakRegistered()
    {
        Assert.AreEqual("Tweaks Exclusivos", NavigationHistoryManager.GetFriendlyPageTitle("PageTweaks"));

        var tweaks = WindowsDebloatService.GetAllTweaks();
        var officeTweak = tweaks.FirstOrDefault(t => t.Id == "input_disable_office_hotkeys");
        Assert.IsNotNull(officeTweak, "Tweak input_disable_office_hotkeys deve estar registrada no WindowsDebloatService");
        Assert.AreEqual("🎯 Latência de Input & Periféricos", officeTweak.Category);
    }

    [TestMethod]
    public void TestPerifDevices_NavigationAndBluetoothAutoVsManualRules()
    {
        Assert.AreEqual("Periféricos · Dispositivos", NavigationHistoryManager.GetFriendlyPageTitle("PagePerifDevices"));

        var favDev = new BluetoothDeviceModel
        {
            Address = 0x112233445566UL,
            AddressText = "11:22:33:44:55:66",
            Name = "Fone Teste",
            IsFavorite = true,
            IsConnected = false
        };
        var emptyAttempts = new Dictionary<ulong, DateTime>();
        var now = DateTime.UtcNow;

        Assert.IsTrue(BluetoothManagerService.ShouldAutoConnect(favDev, radioActive: true, manualMode: false, now, emptyAttempts));
        Assert.IsFalse(BluetoothManagerService.ShouldAutoConnect(favDev, radioActive: true, manualMode: true, now, emptyAttempts));
        Assert.IsFalse(BluetoothManagerService.ShouldAutoConnect(favDev, radioActive: false, manualMode: false, now, emptyAttempts));

        var recentAttempts = new Dictionary<ulong, DateTime> { [favDev.Address] = now.AddSeconds(-5) };
        Assert.IsFalse(BluetoothManagerService.ShouldAutoConnect(favDev, radioActive: true, manualMode: false, now, recentAttempts));

        var settings = new AppSettings
        {
            BluetoothManualMode = true,
            BluetoothFavoriteAddresses = new List<string> { "11:22:33:44:55:66" }
        };
        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.IsTrue(restored.BluetoothManualMode);
        CollectionAssert.Contains(restored.BluetoothFavoriteAddresses, "11:22:33:44:55:66");

        string periphPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MainWindow.Peripherals.cs"));
        string periphCs = System.IO.File.ReadAllText(periphPath);
        Assert.IsTrue(periphCs.Contains("PanelPerifDevices?.Visibility != Visibility.Visible"),
            "InitPerifericosPage deve preservar PanelPerifDevices quando ativo");

        // Volumes internos SWD\WPDBUSENUM (HDs/SSDs) não devem ser classificados como celular MTP
        Assert.IsFalse(DeviceManagerService.IsRealPortableMtpDevice(
            @"SWD\WPDBUSENUM\{40A94CFC-D3F8-11F1-840E-806E6F6E6963}#0000000000100000", "WPD"));
        Assert.IsTrue(DeviceManagerService.IsRealPortableMtpDevice(
            @"USB\VID_04E8&PID_6860\R58M123456A", "WPD"));
    }

    [TestMethod]
    public void TestKeyboardRemapper_RulesSwapShortcutAppScopeAndSerialization()
    {
        // 1. Troca direta (A <-> B) via resolução simples sem loop infinito
        var keyRules = new List<KeyRemapRule>
        {
            new KeyRemapRule { FromVk = 0x41, ToVk = 0x42, IsEnabled = true }, // A -> B
            new KeyRemapRule { FromVk = 0x42, ToVk = 0x41, IsEnabled = true }, // B -> A
            new KeyRemapRule { FromVk = 0x14, IsDisabled = true, IsEnabled = true } // CapsLock -> Disabled
        };
        var shortcutRules = new List<ShortcutRemapRule>
        {
            new ShortcutRemapRule
            {
                FromModifiers = RemapModifiers.Ctrl,
                FromVk = 0x43, // Ctrl+C
                ToModifiers = RemapModifiers.Ctrl | RemapModifiers.Shift,
                ToVk = 0x56, // Ctrl+Shift+V
                TargetApp = "notepad.exe",
                IsEnabled = true
            }
        };

        var resA = KeyboardRemapperService.TryResolveRemap(0x41, RemapModifiers.None, "explorer.exe", keyRules, shortcutRules);
        Assert.IsTrue(resA.Matched);
        Assert.IsFalse(resA.SuppressOnly);
        Assert.AreEqual(0x42, resA.TargetVk);

        var resB = KeyboardRemapperService.TryResolveRemap(0x42, RemapModifiers.None, "explorer.exe", keyRules, shortcutRules);
        Assert.IsTrue(resB.Matched);
        Assert.AreEqual(0x41, resB.TargetVk);

        // 2. Tecla desabilitada
        var resCaps = KeyboardRemapperService.TryResolveRemap(0x14, RemapModifiers.None, "explorer.exe", keyRules, shortcutRules);
        Assert.IsTrue(resCaps.Matched);
        Assert.IsTrue(resCaps.SuppressOnly);

        // 3. Escopo por aplicativo (notepad.exe vs chrome.exe)
        var resNotepad = KeyboardRemapperService.TryResolveRemap(0x43, RemapModifiers.Ctrl, "notepad.exe", keyRules, shortcutRules);
        Assert.IsTrue(resNotepad.Matched);
        Assert.AreEqual(RemapModifiers.Ctrl | RemapModifiers.Shift, resNotepad.TargetModifiers);
        Assert.AreEqual(0x56, resNotepad.TargetVk);

        var resChrome = KeyboardRemapperService.TryResolveRemap(0x43, RemapModifiers.Ctrl, "chrome.exe", keyRules, shortcutRules);
        Assert.IsFalse(resChrome.Matched);

        // 3b. Escopo por aplicativo em tecla única
        var scopedKeyRules = new List<KeyRemapRule>
        {
            new KeyRemapRule { FromVk = 0x70, ToVk = 0x71, TargetApp = "code.exe", IsEnabled = true } // F1 -> F2 apenas no VS Code
        };
        var resF1Code = KeyboardRemapperService.TryResolveRemap(0x70, RemapModifiers.None, "code", scopedKeyRules, shortcutRules);
        Assert.IsTrue(resF1Code.Matched);
        Assert.AreEqual(0x71, resF1Code.TargetVk);
        var resF1Other = KeyboardRemapperService.TryResolveRemap(0x70, RemapModifiers.None, "notepad.exe", scopedKeyRules, shortcutRules);
        Assert.IsFalse(resF1Other.Matched);

        // 3c. Lista de exclusão bloqueia regras globais e específicas
        var excludedApps = new[] { "valorant.exe", "notepad" };
        var resExcludedKey = KeyboardRemapperService.TryResolveRemap(0x41, RemapModifiers.None, "valorant", keyRules, shortcutRules, excludedApps);
        Assert.IsFalse(resExcludedKey.Matched);
        var resExcludedShortcut = KeyboardRemapperService.TryResolveRemap(0x43, RemapModifiers.Ctrl, "notepad.exe", keyRules, shortcutRules, excludedApps);
        Assert.IsFalse(resExcludedShortcut.Matched);

        // 4. Serialização em AppSettings (padrão desativado e preservação de regras + exclusões)
        var settings = new AppSettings();
        Assert.IsFalse(settings.KeyboardRemapEnabled);
        settings.KeyboardRemapEnabled = true;
        settings.KeyRemaps = keyRules;
        settings.ShortcutRemaps = shortcutRules;
        settings.KeyboardRemapExcludedApps = new List<string> { "valorant.exe" };
        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.IsTrue(restored.KeyboardRemapEnabled);
        Assert.AreEqual(3, restored.KeyRemaps.Count);
        Assert.AreEqual(1, restored.ShortcutRemaps.Count);
        Assert.AreEqual("notepad.exe", restored.ShortcutRemaps[0].TargetApp);
        CollectionAssert.Contains(restored.KeyboardRemapExcludedApps, "valorant.exe");
    }

    [TestMethod]
    public void TestAppUpdater_VersionParsingAssetSelectionAndSettings()
    {
        Assert.IsTrue(AppUpdaterService.TryParseTag("v0.29.0", out var v1));
        Assert.AreEqual(new Version(0, 29, 0), v1);
        Assert.IsFalse(AppUpdaterService.TryParseTag("latest", out _));
        Assert.IsTrue(AppUpdaterService.IsNewer(new Version(0, 29, 0), new Version(0, 28, 1, 0)));
        Assert.IsFalse(AppUpdaterService.IsNewer(new Version(0, 28, 1), new Version(0, 28, 1, 0)));

        string json = @"[
          {""tag_name"":""latest"",""draft"":false,""prerelease"":false,""body"":""rolante"",""assets"":[]},
          {""tag_name"":""v0.28.1"",""body"":""antiga"",""assets"":[]},
          {""tag_name"":""v0.29.0"",""body"":""nova"",""html_url"":""https://x"",""assets"":[
            {""name"":""LuminiTray-Installer.exe"",""browser_download_url"":""https://h/i.exe""},
            {""name"":""LuminiTray-Portable.exe"",""browser_download_url"":""https://h/p.exe""},
            {""name"":""LuminiTray-Portable.zip"",""browser_download_url"":""https://h/p.zip""}]}]";
        var info = AppUpdaterService.ParseReleaseList(json);
        Assert.IsNotNull(info);
        Assert.AreEqual(new Version(0, 29, 0), info.Version);
        Assert.AreEqual("https://h/i.exe", info.InstallerUrl);
        Assert.AreEqual("https://h/p.zip", AppUpdaterService.SelectAssetUrl(info, hasAssetsFolder: true));
        Assert.AreEqual("https://h/p.exe", AppUpdaterService.SelectAssetUrl(info, hasAssetsFolder: false));

        var settings = new AppSettings();
        Assert.IsTrue(settings.AutoCheckUpdates);
        settings.AutoCheckUpdates = false;
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.IsFalse(restored.AutoCheckUpdates);
    }
    [TestMethod]
    public void TestOverlay_VramClockGroupAndTemperatureClockUnits()
    {
        var memClock = OverlayMetrics.All.First(m => m.Key == "gpu_memclock");
        Assert.AreEqual("VRAM", memClock.Group);

        Assert.AreEqual($"{65f:0.0} °C", OverlayMetrics.FormatTemperature(65f, "C"));
        Assert.AreEqual($"{149f:0.0} °F", OverlayMetrics.FormatTemperature(65f, "F"));
        Assert.AreEqual($"{338.15:0.0} K", OverlayMetrics.FormatTemperature(65f, "K"));
        Assert.AreEqual("— °F", OverlayMetrics.FormatTemperature(null, "F"));

        Assert.AreEqual("2790 MHz", OverlayMetrics.FormatClock(2790, "Auto", defaultGHz: false));
        Assert.AreEqual($"{4.5:0.00} GHz", OverlayMetrics.FormatClock(4500, "Auto", defaultGHz: true));
        Assert.AreEqual("4500 MHz", OverlayMetrics.FormatClock(4500, "MHz", defaultGHz: true));
        Assert.AreEqual($"{2.79:0.00} GHz", OverlayMetrics.FormatClock(2790, "GHz", defaultGHz: false));
        Assert.AreEqual("— GHz", OverlayMetrics.FormatClock(null, "GHz", defaultGHz: false));

        var settings = new AppSettings { OverlayTempUnit = "K", OverlayClockUnit = "GHz" };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.AreEqual("K", restored.OverlayTempUnit);
        Assert.AreEqual("GHz", restored.OverlayClockUnit);
    }
    [TestMethod]
    public void TestTaskbarTrayTweaks_BuildOperationsAndRestartDetection()
    {
        var baseState = new TaskbarTrayTweaksState();
        Assert.IsFalse(baseState.HasAnyActiveXamlTweak);

        var modified = baseState with
        {
            HideVolume = true,
            GrayscaleBattery = true,
            HideBellMode = TaskbarBellHideMode.WhenInactive,
            ShowDesktopWidth = 0
        };
        Assert.IsTrue(modified.HasAnyActiveXamlTweak);

        var ops = TaskbarTrayTweaksService.BuildOperations(baseState, modified);
        Assert.AreEqual(4, ops.Count);
        Assert.AreEqual(0, TaskbarTrayTweaksService.BuildOperations(baseState, baseState).Count);
        CollectionAssert.AreEqual(
            new[] { "VisualDiagConnection1", "VisualDiagConnection2" },
            TaskbarTrayTweaksService.DiagEndpointNames);
    }
    [TestMethod]
    public void TestPeripheralBattery_ParsersAndSettingsDefaults()
    {
        // Parsers de protocolos HaloBattery
        var (xLvl, _, xChg) = PeripheralBatteryService.ParseXboxXInputLevel(0x02, 2);
        Assert.AreEqual(55, xLvl);
        Assert.IsFalse(xChg);
        Assert.AreEqual((100, "Cabeado", true), PeripheralBatteryService.ParseXboxXInputLevel(0x01, 0));

        var rz = new byte[90]; rz[0] = 0x02; rz[9] = 204;
        Assert.AreEqual(80, PeripheralBatteryService.ParseRazerBatteryResponse(rz));

        var (logiLvl, logiChg) = PeripheralBatteryService.ParseLogitechHidPp2Unified(new byte[] { 0x11, 1, 5, 0x1A, 74, 0, 1 });
        Assert.AreEqual(74, logiLvl);
        Assert.IsTrue(logiChg);

        Assert.AreEqual((80, true), PeripheralBatteryService.ParsePlayStationDs4(0x18));
        Assert.AreEqual((65, true), PeripheralBatteryService.ParsePlayStationDualSense(0x16));
        Assert.AreEqual(81, PeripheralBatteryService.ParseCorsairBragi(new byte[] { 0x01, 0x02, 0x00, 0x2A, 0x03 }));
        Assert.AreEqual((92, true, true), PeripheralBatteryService.ParseSteelSeriesNova7(new byte[] { 0xB0, 0x03, 92, 0x01 }));
        var kc = new byte[24]; kc[0] = 0xB4; kc[1] = 0x06; kc[20] = 88;
        Assert.AreEqual(88, PeripheralBatteryService.ParseKeychronLevel(kc));

        Assert.IsTrue(PeripheralHidTransport.TryExtractVidPid(@"\\?\hid#vid_046d&pid_c548&mi_02#...", out ushort vid, out ushort pid));
        Assert.AreEqual((ushort)0x046D, vid);
        Assert.AreEqual((ushort)0xC548, pid);
        Assert.AreEqual("AABBCCDDEEFF", PeripheralHidTransport.ExtractMacFromInstanceId(@"BTHLE\DEV_AABBCCDDEEFF\8&1234"));

        // Padrão de fábrica e serialização
        var settings = new AppSettings();
        Assert.IsFalse(settings.ShowPeripheralBatteryTrayIcon);
        Assert.AreEqual(0, settings.PeripheralBatteryTrayKeys.Count);
        Assert.IsTrue(settings.PeripheralBatteryLowAlertEnabled);
        Assert.AreEqual(20, settings.PeripheralBatteryLowThreshold);
        settings.ShowPeripheralBatteryTrayIcon = true;
        settings.PeripheralBatteryTrayKeys = new List<string> { "logi:C548:1" };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.IsTrue(restored.ShowPeripheralBatteryTrayIcon);
        CollectionAssert.Contains(restored.PeripheralBatteryTrayKeys, "logi:C548:1");

        // Seleção de dispositivo único na bandeja e troca por duplo clique
        var list = new List<PeripheralBatteryInfo>
        {
            new() { Key = "mouse1", Name = "Mouse", Level = 70, IsOnline = true },
            new() { Key = "headset1", Name = "Headset", Level = 45, IsOnline = true }
        };
        Assert.AreEqual("headset1", SystemTrayManager.GetNextCycledDeviceKey(list, "mouse1"));
        Assert.AreEqual("mouse1", SystemTrayManager.GetNextCycledDeviceKey(list, "headset1"));
        Assert.AreEqual("headset1", SystemTrayManager.SelectSingleTrayDevice(list, "headset1", Array.Empty<string>())?.Key);
    }

    [TestMethod]
    public void TestPeripheralBattery_WdbmProtocolsAndDongleIdentification()
    {
        // Classificação de Dongle 2.4 GHz vs Bluetooth vs Cabo USB
        Assert.AreEqual(PeripheralConnectionType.Dongle24GHz,
            PeripheralBatteryService.ClassifyConnectionType(0x046D, 0xC548, "Logi Bolt Receiver", isBluetooth: false));
        Assert.AreEqual(PeripheralConnectionType.Dongle24GHz,
            PeripheralBatteryService.ClassifyConnectionType(0x1234, 0x5678, "2.4G Wireless Receiver", isBluetooth: false));
        Assert.AreEqual(PeripheralConnectionType.Bluetooth,
            PeripheralBatteryService.ClassifyConnectionType(0x046D, 0xB023, "MX Master 3S", isBluetooth: true));
        Assert.AreEqual(PeripheralConnectionType.UsbCable,
            PeripheralBatteryService.ClassifyConnectionType(0x1234, 0x5678, "Wired Gaming Mouse", isBluetooth: false));

        // Escala genérica HID (incluindo Apple 0x05AC quirk)
        Assert.AreEqual(75, PeripheralHidTransport.ScaleHidBatteryValue(75, 0, 100, 0x1234));
        Assert.AreEqual(50, PeripheralHidTransport.ScaleHidBatteryValue(128, 0, 255, 0x1234));
        Assert.AreEqual(64, PeripheralHidTransport.ScaleHidBatteryValue(64, 0, 255, 0x05AC));

        // ASUS ROG (0x0B05) normal e em espera (FF AA)
        var rogOk = PeripheralBatteryService.ParseAsusRogBatteryResponse(new byte[] { 0x00, 0x12, 0x07, 0x01, 82, 0x00 });
        Assert.IsNotNull(rogOk);
        Assert.AreEqual(82, rogOk.Value.Level);
        Assert.IsTrue(rogOk.Value.Charging);
        Assert.IsFalse(rogOk.Value.Asleep);

        var rogSleep = PeripheralBatteryService.ParseAsusRogBatteryResponse(new byte[] { 0x00, 0xFF, 0xAA, 0x00, 0x00, 0x00 });
        Assert.IsNotNull(rogSleep);
        Assert.IsTrue(rogSleep.Value.Asleep);

        // Kysona / ATK / VXE / Pulsar (17 bytes com checksum 0x55 - sum)
        var kys = new byte[17];
        kys[0] = 0x08; kys[1] = 0x04; kys[6] = 68; kys[7] = 1; kys[8] = 0x0F; kys[9] = 0x6E; // 3950 mV
        kys[16] = PeripheralBatteryService.ComputeKysonaChecksum(kys);
        var kysParsed = PeripheralBatteryService.ParseKysonaBatteryResponse(kys);
        Assert.IsNotNull(kysParsed);
        Assert.AreEqual(68, kysParsed.Value.Level);
        Assert.IsTrue(kysParsed.Value.Charging);
        Assert.AreEqual(3950, kysParsed.Value.Millivolts);

        // Rapoo Nordic 54L (0x07) & Legacy VT3 Pro (0xBB 0xB0)
        var rapoo = new byte[16];
        rapoo[0] = 0x07; rapoo[1] = 0x00; rapoo[7] = 1; rapoo[8] = 91; rapoo[13] = 0x15; rapoo[14] = 0x46;
        var rapooParsed = PeripheralBatteryService.ParseRapooBatteryResponse(rapoo, legacyVt3Pro: false);
        Assert.IsNotNull(rapooParsed);
        Assert.AreEqual(91, rapooParsed.Value.Level);
        Assert.AreEqual(PeripheralConnectionType.Dongle24GHz, rapooParsed.Value.Connection);
        Assert.AreEqual((ushort)0x4615, rapooParsed.Value.MousePid);

        var rapooLegacy = PeripheralBatteryService.ParseRapooBatteryResponse(new byte[] { 0xBB, 0xB0, 0x71, 0x84, 0x03, 0x02, 0x5F }, legacyVt3Pro: true);
        Assert.IsNotNull(rapooLegacy);
        Assert.AreEqual(95, rapooLegacy.Value.Level);
        Assert.IsTrue(rapooLegacy.Value.Charging);

        // Areson / Redragon (25A7:FA07)
        var areson = new byte[17];
        areson[0] = 0x09; areson[1] = 0x04; areson[2] = 0x00; areson[6] = 8; areson[7] = 0;
        areson[16] = PeripheralBatteryService.ComputeKysonaChecksum(areson);
        var aresonParsed = PeripheralBatteryService.ParseAresonBatteryResponse(areson);
        Assert.IsNotNull(aresonParsed);
        Assert.AreEqual(80, aresonParsed.Value.Level);
        Assert.IsFalse(aresonParsed.Value.Charging);

        // AULA / MCHOSE (0x13 0x4A)
        var aula = new byte[20];
        aula[0] = 0x13; aula[1] = 0xCA; aula[5] = 77; aula[6] = 0x10;
        int sum = 0; for (int i = 0; i < 19; i++) sum += aula[i];
        aula[19] = (byte)(sum & 0xFF);
        var aulaParsed = PeripheralBatteryService.ParseAulaBatteryResponse(aula);
        Assert.IsNotNull(aulaParsed);
        Assert.AreEqual(77, aulaParsed.Value.Level);
        Assert.IsTrue(aulaParsed.Value.Charging);

        // Nintendo Switch Pro (0x30)
        var swPro = new byte[16];
        swPro[0] = 0x30; swPro[2] = (byte)((3 << 5) | 0x10); // nível 3 (75%) + carregando
        var swParsed = PeripheralBatteryService.ParseSwitchProBatteryResponse(swPro);
        Assert.IsNotNull(swParsed);
        Assert.AreEqual(75, swParsed.Value.Level);
        Assert.IsTrue(swParsed.Value.Charging);

        // Badge de Dongle no modelo
        var dongleDev = new PeripheralBatteryInfo { Key = "dongle:1234:5678", Name = "Mouse 2.4G", ConnectionType = PeripheralConnectionType.Dongle24GHz };
        Assert.IsTrue(dongleDev.IsDongle);
        Assert.AreEqual("Dongle 2.4 GHz", dongleDev.ConnectionLabel);
        Assert.AreEqual("Conectado", dongleDev.DisplayLevel);
    }

    [TestMethod]
    public void TestPeripheralBattery_ConnectedOnly_GuidTrayAndMultiBrandXInput()
    {
        // 1. GUIDs determinísticos e distintos entre ícone principal e ícones individuais
        var gMain1 = GuidNotifyIcon.CreateDeterministicGuid("peripheral-battery-main");
        var gMain2 = GuidNotifyIcon.CreateDeterministicGuid("peripheral-battery-main");
        var gDev1 = GuidNotifyIcon.CreateDeterministicGuid("peripheral-battery-dev:logi:c548:1");
        var gDev2 = GuidNotifyIcon.CreateDeterministicGuid("peripheral-battery-dev:xinput:1a34:f517:0");
        Assert.AreEqual(gMain1, gMain2);
        Assert.AreNotEqual(gMain1, gDev1);
        Assert.AreNotEqual(gMain1, gDev2);
        Assert.AreNotEqual(gDev1, gDev2);

        // 2. Filtro estrito: apenas dispositivos atualmente conectados e acordados
        var mixed = new List<PeripheralBatteryInfo>
        {
            new() { Key = "a", Name = "Conectado", IsOnline = true, IsAsleep = false, Level = 80 },
            new() { Key = "b", Name = "BT Offline", IsOnline = false, IsAsleep = false, Level = 50 },
            new() { Key = "c", Name = "Mouse Dormindo", IsOnline = true, IsAsleep = true, Level = 60 }
        };
        var onlyConnected = PeripheralBatteryService.FilterConnectedOnly(mixed);
        Assert.AreEqual(1, onlyConnected.Count);
        Assert.AreEqual("a", onlyConnected[0].Key);

        // 3. Machenike G5 Pro V2 via dongle 2.4 GHz (XInput reporta WIRED=0x01) NÃO deve aparecer como Cabo USB nem 100%
        var gamepads = new List<UsbGamepadDescriptor>
        {
            new(0x1A34, 0xF517, "Dispositivo de Entrada USB", "", "", @"USB\VID_1A34&PID_F517\6&1", IsBluetooth: false)
        };
        var mach = PeripheralDeviceCatalog.ResolveXInputDevice(0, batteryType: 0x01, batteryLevel: 0, gamepads, matchedIndex: 0);
        Assert.AreEqual("Machenike G5 Pro V2", mach.DisplayName);
        Assert.AreEqual(PeripheralConnectionType.Dongle24GHz, mach.ConnectionType);
        Assert.IsNull(mach.Level);
        Assert.IsFalse(mach.IsCharging);
        Assert.AreEqual("Conectado", mach.ApproxText);

        // 4. Controle Xbox oficial cabeado (045E:0B12) continua sendo identificado como Cabo USB
        var xboxWiredList = new List<UsbGamepadDescriptor>
        {
            new(0x045E, 0x0B12, "Xbox Series X Controller", "Microsoft", "", @"USB\VID_045E&PID_0B12\1", IsBluetooth: false)
        };
        var xboxWired = PeripheralDeviceCatalog.ResolveXInputDevice(0, batteryType: 0x01, batteryLevel: 0, xboxWiredList, matchedIndex: 0);
        Assert.AreEqual(PeripheralConnectionType.UsbCable, xboxWired.ConnectionType);
        Assert.AreEqual(100, xboxWired.Level);
        Assert.IsTrue(xboxWired.IsCharging);

        // 5. Catálogo multi-marca & limpeza de nomes genéricos
        Assert.AreEqual("GameSir Cyclone 2", PeripheralDeviceCatalog.BuildDisplayName(0x3537, 0x1042, "USB Input Device"));
        Assert.AreEqual("8BitDo Ultimate 2.4G", PeripheralDeviceCatalog.BuildDisplayName(0x2DC8, 0x3106, ""));
        Assert.AreEqual("Flydigi Vader 4 Pro", PeripheralDeviceCatalog.BuildDisplayName(0x37D7, 0x2412, ""));
        Assert.AreEqual("Sharkoon 2.4G Wireless Mouse", PeripheralDeviceCatalog.BuildDisplayName(0x1EA7, 0x0067, "2.4G Wireless Receiver"));
        Assert.AreEqual("Logitech PRO X SUPERLIGHT", PeripheralDeviceCatalog.BuildDisplayName(0x046D, 0x9999, "PRO X SUPERLIGHT Receiver", "Logitech"));
    }

    [TestMethod]
    public void TestMonitoringLogService_And_PrivacyModeService_And_ReportBuilder()
    {
        // 1. SensorRow Min/Max horários + ResetMinMax
        var t1 = new DateTime(2026, 11, 18, 21, 0, 0);
        var t2 = new DateTime(2026, 11, 18, 21, 5, 12);
        var cpuRow = new SensorRow
        {
            Id = "/amdcpu/0/temperature/0",
            Hardware = "AMD Ryzen 7",
            Category = "Temperatura",
            Name = "Core (Tctl/Tdie)",
            Type = LibreHardwareMonitor.Hardware.SensorType.Temperature,
            HardwareType = LibreHardwareMonitor.Hardware.HardwareType.Cpu,
            IsSelectedForMonitoring = true
        };
        cpuRow.Update(55.0f, 52.0f, 58.0f, t1);
        Assert.AreEqual(t1, cpuRow.MinTime);
        Assert.AreEqual(t1, cpuRow.MaxTime);
        Assert.IsTrue(cpuRow.MinWithTime.Contains("52,0") || cpuRow.MinWithTime.Contains("52.0"));
        Assert.IsTrue(cpuRow.MinWithTime.Contains("21:00:00"));

        cpuRow.Update(71.5f, 52.0f, 71.5f, t2);
        Assert.AreEqual(t1, cpuRow.MinTime);
        Assert.AreEqual(t2, cpuRow.MaxTime);
        Assert.IsTrue(cpuRow.MaxWithTime.Contains("21:05:12"));

        // 2. MonitoringLogService: grava arquivo temp apenas durante sessão e apaga ao concluir
        string tempDir = Path.Combine(Path.GetTempPath(), "LuminiTrayMonTest_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var svc = new MonitoringLogService(tempDir);
            Assert.IsTrue(svc.Start(new[] { cpuRow }, intervalSeconds: 60, durationSeconds: 120, nowOverride: t1));
            Assert.IsTrue(svc.IsRunning);
            Assert.IsTrue(File.Exists(svc.ActiveTempFilePath));

            cpuRow.Update(65.0f, 52.0f, 71.5f, t1.AddSeconds(60));
            bool done1 = svc.Tick(new[] { cpuRow }, nowOverride: t1.AddSeconds(60));
            Assert.IsFalse(done1);
            Assert.IsTrue(File.Exists(svc.ActiveTempFilePath));

            cpuRow.Update(60.0f, 52.0f, 71.5f, t1.AddSeconds(120));
            bool done2 = svc.Tick(new[] { cpuRow }, nowOverride: t1.AddSeconds(120));
            Assert.IsTrue(done2);
            Assert.IsFalse(svc.IsRunning);
            // Arquivo bruto temporário DEVE ser apagado após concluir
            Assert.IsFalse(File.Exists(svc.ActiveTempFilePath));

            Assert.IsNotNull(svc.LastResult);
            Assert.IsTrue(svc.LastResult!.CompletedAutomatically);
            Assert.AreEqual(1, svc.LastResult.Sensors.Count);
            var sum = svc.LastResult.Sensors[0];
            Assert.AreEqual(3, sum.SampleCount);
            Assert.AreEqual(60.0f, sum.Min);
            Assert.AreEqual(t1.AddSeconds(120), sum.MinTime);
            Assert.AreEqual(71.5f, sum.Max);
            Assert.AreEqual(t1, sum.MaxTime);
            Assert.AreEqual(3, sum.Points.Count);

            var t3 = t2.AddMinutes(1);
            cpuRow.ResetMinMax(t3);
            Assert.AreEqual(60.0f, cpuRow.RawMin);
            Assert.AreEqual(60.0f, cpuRow.RawMax);
            Assert.AreEqual(t3, cpuRow.MinTime);
            Assert.AreEqual(t3, cpuRow.MaxTime);

            // 3. PrivacyModeService e ReportBuilder
            try
            {
                PrivacyModeService.IsEnabled = true;
                var info = new InfoNode { Header = "Endereço IPv4", Value = "192.168.1.45" };
                Assert.AreEqual(PrivacyModeService.MaskToken, info.MaskedValue);
                Assert.IsTrue(info.Display.Contains(PrivacyModeService.MaskToken));

                var normalInfo = new InfoNode { Header = "Adaptador", Value = "Ethernet 10.0.0.15 MAC AA:BB:CC:DD:EE:FF" };
                Assert.IsFalse(normalInfo.MaskedValue.Contains("10.0.0.15"));
                Assert.IsFalse(normalInfo.MaskedValue.Contains("AA:BB:CC:DD:EE:FF"));

                var reportData = new ReportData
                {
                    Machine = "PC-SECRETO-01",
                    PrivacyModeActive = true,
                    System = new List<ReportNode> { new("Rede", null, new List<ReportNode> { new("Gateway", "192.168.1.1", null) }) },
                    Monitoring = svc.LastResult
                };
                string json = ReportBuilder.Json(reportData);
                string html = ReportBuilder.Html(reportData);
                Assert.IsTrue(json.Contains("monitoramento_registrado"));
                Assert.IsFalse(json.Contains("192.168.1.1"));
                Assert.IsFalse(json.Contains("PC-SECRETO-01"));
                Assert.IsTrue(html.Contains("Monitoramento registrado"));
                Assert.IsTrue(html.Contains("<svg"));
                Assert.IsFalse(html.Contains("192.168.1.1"));
                Assert.IsFalse(html.Contains("PC-SECRETO-01"));
            }
            finally
            {
                PrivacyModeService.IsEnabled = false;
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void TestPhase1_CSharpOptimizations_EquivalenceAndCaching()
    {
        // 1. DiskSpaceItem.CategoryBrush retorna a mesma instância congelada por categoria (zero churn WPF)
        var d1 = new DiskSpaceItem { Name = "a.mp4", Category = "Vídeos", Size = 100 };
        var d2 = new DiskSpaceItem { Name = "b.mkv", Category = "Vídeos", Size = 200 };
        Assert.IsTrue(d1.CategoryBrush.IsFrozen);
        Assert.AreSame(d1.CategoryBrush, d2.CategoryBrush);

        // 2. FrameMonitor.ComputeFrameMetrics calcula FPS, frametime e 1% low sem alocação de LINQ
        // 61 timestamps espaçados de 10 ms (= 100 FPS exatos em 600 ms)
        var ts = new double[61];
        for (int i = 0; i < ts.Length; i++) ts[i] = i * 10.0;
        var (fps, ftMs, low1) = FrameMonitor.ComputeFrameMetrics(ts);
        Assert.AreEqual(100.0, fps, 0.01);
        Assert.AreEqual(10.0, ftMs, 0.01);
        Assert.AreEqual(100.0, low1, 0.01);

        // 3. SystemTrayManager & GuidNotifyIcon icon cache key
        Assert.AreEqual("85:0", SystemTrayManager.BuildBatteryIconCacheKey(85, false));
        Assert.AreEqual("85:1", SystemTrayManager.BuildBatteryIconCacheKey(85, true));
        Assert.AreEqual("-1:0", SystemTrayManager.BuildBatteryIconCacheKey(null, false));

        // 4. DiskTopologyService.RefId & Invalidate
        Assert.AreEqual(@"\\.\PHYSICALDRIVE0", DiskTopologyService.RefId("Win32_DiskDrive.DeviceID=\"\\\\\\\\.\\\\PHYSICALDRIVE0\""));
        DiskTopologyService.Invalidate();
    }

    [TestMethod]
    public void TestPhase2_RustNativeCoreBridge_AbiPingAllocAndFallback()
    {
        // 1. DLL nativa Rust carregada e ABI compatível
        Assert.IsTrue(NativeCoreBridge.IsAvailable, $"LuminiTray.NativeCore.dll indisponível: {NativeCoreBridge.FailureReason}");
        Assert.AreEqual(NativeCoreBridge.ExpectedAbiVersion, NativeCoreBridge.LoadedAbiVersion);
        Assert.IsTrue(NativeCoreBridge.IsEnabled());

        // 2. FFI ping + relato de erro em thread-local UTF-16
        Assert.AreEqual(37, NativeCoreBridge.TryPing(10));
        Assert.AreEqual(-1, NativeCoreBridge.TryPing(int.MinValue));
        Assert.IsTrue(NativeCoreBridge.GetLastError().Contains("invalid ping sentinel"));

        // 3. Alocação e liberação repetida na memória nativa Rust sem erro (500 ciclos)
        Assert.IsTrue(NativeCoreBridge.TryAllocAndFreeSmokeTest(4096, iterations: 500));

        // 4. Override / fallback para implementação gerenciada em C#
        try
        {
            NativeCoreBridge.ForceManagedOverride = true;
            Assert.IsFalse(NativeCoreBridge.IsEnabled());
        }
        finally
        {
            NativeCoreBridge.ForceManagedOverride = false;
        }
        Assert.IsTrue(NativeCoreBridge.IsEnabled());

        // 5. Harness de equivalência
        bool same = NativeCoreBridge.VerifyEquivalence(
            () => 10 * 3 + 7,
            () => NativeCoreBridge.TryPing(10) ?? 0,
            (a, b) => a == b,
            out int mVal, out int nVal);
        Assert.IsTrue(same);
        Assert.AreEqual(mVal, nVal);
    }

    [TestMethod]
    public async Task TestPhase3_RustModules_DiskFramesAndDspEquivalence()
    {
        Assert.IsTrue(NativeCoreBridge.IsAvailable, $"NativeCore indisponível: {NativeCoreBridge.FailureReason}");
        Assert.AreEqual(2u, NativeCoreBridge.LoadedAbiVersion);

        // 1. Disk Scanner equivalence (ScanManagedAsync vs ScanNativeAsync)
        string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lumini_phase3_disk_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempRoot);
        try
        {
            string subA = System.IO.Path.Combine(tempRoot, "SubA");
            string subEmpty = System.IO.Path.Combine(tempRoot, "EmptyDir");
            System.IO.Directory.CreateDirectory(subA);
            System.IO.Directory.CreateDirectory(subEmpty);

            System.IO.File.WriteAllBytes(System.IO.Path.Combine(tempRoot, "alpha.log"), new byte[1200]);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(tempRoot, "noext"), new byte[345]);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(subA, "video.mp4"), new byte[4096]);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(subA, "notes.txt"), new byte[512]);

            var managedDisk = await DiskSpaceAnalyzer.ScanManagedAsync(tempRoot);
            var nativeDisk = await DiskSpaceAnalyzer.ScanNativeAsync(tempRoot);

            Assert.AreEqual(managedDisk.Root.Size, nativeDisk.Root.Size);
            Assert.AreEqual(managedDisk.Root.FileCount, nativeDisk.Root.FileCount);
            Assert.AreEqual(managedDisk.Root.FolderCount, nativeDisk.Root.FolderCount);
            Assert.AreEqual(managedDisk.Root.Category, nativeDisk.Root.Category);
            Assert.AreEqual(managedDisk.AccessDeniedCount, nativeDisk.AccessDeniedCount);
            Assert.AreEqual(managedDisk.ReparseCount, nativeDisk.ReparseCount);
            Assert.AreEqual(managedDisk.TopFiles.Count, nativeDisk.TopFiles.Count);
            Assert.AreEqual(managedDisk.TopFiles[0].Name, nativeDisk.TopFiles[0].Name);
            Assert.AreEqual(managedDisk.TopFiles[0].Size, nativeDisk.TopFiles[0].Size);
            Assert.AreEqual(managedDisk.Extensions.Count, nativeDisk.Extensions.Count);
            Assert.AreEqual(managedDisk.Extensions[0].Extension, nativeDisk.Extensions[0].Extension);
            Assert.AreEqual(managedDisk.Extensions[0].TotalSize, nativeDisk.Extensions[0].TotalSize);
        }
        finally
        {
            try { System.IO.Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        // 2. Frame Monitor metrics & native store equivalence
        double[] timestamps = new double[150];
        double curTs = 0.0;
        for (int i = 0; i < timestamps.Length; i++)
        {
            timestamps[i] = curTs;
            curTs += (i % 15 == 0) ? 16.0 : 8.0;
        }
        var (mFps, mFt, mLow1) = FrameMonitor.ComputeFrameMetricsManaged(timestamps);
        Assert.IsTrue(NativeCoreBridge.TryComputeFrameMetrics(timestamps, out double nFps, out double nFt, out double nLow1));
        Assert.AreEqual(mFps, nFps, 1e-6);
        Assert.AreEqual(mFt, nFt, 1e-6);
        Assert.AreEqual(mLow1, nLow1, 1e-6);

        IntPtr store = NativeCoreBridge.TryCreateFrameStore();
        Assert.AreNotEqual(IntPtr.Zero, store);
        try
        {
            for (int i = 0; i < timestamps.Length; i++)
                Assert.IsTrue(NativeCoreBridge.TryRecordFrame(store, 4242, timestamps[i], 10_000.0));

            double[] recent = new double[120];
            Assert.IsTrue(NativeCoreBridge.TryQueryFrameStats(store, 4242, 10_500.0, out bool hasStats, out double sFps, out double sFt, out double sLow1, recent, out int rCount));
            Assert.IsTrue(hasStats);
            Assert.AreEqual(mFps, sFps, 1e-6);
            Assert.AreEqual(mFt, sFt, 1e-6);
            Assert.AreEqual(mLow1, sLow1, 1e-6);
            Assert.AreEqual(120, rCount);

            // Stale check (> 2000 ms elapsed since hostNowMs=10_000.0)
            Assert.IsTrue(NativeCoreBridge.TryQueryFrameStats(store, 4242, 13_000.0, out bool staleHasStats, out _, out _, out _, recent, out _));
            Assert.IsFalse(staleHasStats);
        }
        finally
        {
            NativeCoreBridge.DestroyFrameStore(store);
        }

        // 3. Voice DSP pipeline & SoftLimit equivalence (ProcessManaged vs Rust Process)
        float[] probeLimits = { -1.5f, -0.9f, -0.5f, 0f, 0.5f, 0.85f, 0.95f, 2.0f };
        foreach (float x in probeLimits)
        {
            Assert.IsTrue(NativeCoreBridge.TrySoftLimit(x, out float rustLim));
            try
            {
                NativeCoreBridge.ForceManagedOverride = true;
                float managedLim = NoiseSuppressionEngine.SoftLimit(x);
                Assert.AreEqual(managedLim, rustLim, 1e-6f);
            }
            finally
            {
                NativeCoreBridge.ForceManagedOverride = false;
            }
        }

        using var engineRust = new NoiseSuppressionEngine
        {
            GainDb = 3.0f,
            LowEqDb = 2.0f,
            MidEqDb = -1.5f,
            HighEqDb = 2.5f,
            GateThresholdDb = -50.0f,
            LimiterEnabled = true,
            DeesserEnabled = true
        };
        using var engineManaged = new NoiseSuppressionEngine
        {
            GainDb = 3.0f,
            LowEqDb = 2.0f,
            MidEqDb = -1.5f,
            HighEqDb = 2.5f,
            GateThresholdDb = -50.0f,
            LimiterEnabled = true,
            DeesserEnabled = true
        };

        for (int frame = 0; frame < 10; frame++)
        {
            float amp = frame < 2 ? 0.0005f : (frame < 7 ? 0.45f : 0.95f);
            float freq = frame >= 7 ? 7200.0f : 320.0f;
            float[] bufRust = new float[NoiseSuppressionEngine.FrameSamples];
            float[] bufManaged = new float[NoiseSuppressionEngine.FrameSamples];
            for (int i = 0; i < NoiseSuppressionEngine.FrameSamples; i++)
            {
                float s = amp * MathF.Sin(2f * MathF.PI * freq * (frame * 480 + i) / 48000f);
                bufRust[i] = s;
                bufManaged[i] = s;
            }

            engineRust.Process(bufRust);
            engineManaged.ProcessManaged(bufManaged);

            Assert.AreEqual(engineManaged.LastVoiceProbability, engineRust.LastVoiceProbability, 1e-4f, $"VAD divergente no frame {frame}");
            for (int i = 0; i < NoiseSuppressionEngine.FrameSamples; i++)
            {
                Assert.AreEqual(bufManaged[i], bufRust[i], 1e-4f, $"Amostra {i} do frame {frame} divergiu.");
            }
        }
    }
}
