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
}
