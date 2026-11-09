using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class PostFormatAndHealthTests
{
    [TestMethod]
    public void BatteryReport_XmlParsing_ExtractsMetricsCorrectly()
    {
        string sampleXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <BatteryReport>
              <Batteries>
                <Battery>
                  <Id>12345</Id>
                  <Manufacturer>LGC-LGC</Manufacturer>
                  <DeviceName>DELL 451-BBUX</DeviceName>
                  <Chemistry>Li-I</Chemistry>
                  <DesignCapacity>56000</DesignCapacity>
                  <FullChargeCapacity>48500</FullChargeCapacity>
                  <CycleCount>142</CycleCount>
                </Battery>
              </Batteries>
              <RuntimeEstimates>
                <RuntimeEstimate>
                  <ActiveFullChargeDuration>PT4H15M</ActiveFullChargeDuration>
                  <ActiveDesignDuration>PT5H10M</ActiveDesignDuration>
                </RuntimeEstimate>
              </RuntimeEstimates>
              <HistoryEntries>
                <HistoryEntry>
                  <Timestamp>2026-11-01T12:00:00</Timestamp>
                  <FullChargeCapacity>48600</FullChargeCapacity>
                  <DesignCapacity>56000</DesignCapacity>
                </HistoryEntry>
              </HistoryEntries>
            </BatteryReport>
            """;

        var report = BatteryReportService.ParseXml(sampleXml);

        Assert.IsNotNull(report);
        Assert.IsTrue(report.HasBattery);
        Assert.AreEqual(1, report.Batteries.Count);

        var primary = report.PrimaryBattery;
        Assert.IsNotNull(primary);
        Assert.AreEqual(142L, primary.CycleCount);
        Assert.AreEqual(56000L, primary.DesignCapacityMwh);
        Assert.AreEqual(48500L, primary.FullChargeCapacityMwh);
        Assert.AreEqual(56.0, primary.DesignCapacityWh, 0.1);
        Assert.AreEqual(48.5, primary.FullChargeCapacityWh, 0.1);
        Assert.IsTrue(primary.HealthPercentage > 80 && primary.HealthPercentage < 90);
        Assert.IsTrue(primary.DegradationPercentage > 10 && primary.DegradationPercentage < 20);
        Assert.AreEqual("LGC-LGC", primary.Manufacturer);
        Assert.AreEqual("DELL 451-BBUX", primary.ModelName);
    }

    [TestMethod]
    public void UsbDriveTarget_PropertiesAndSuspicion_CalculatedProperly()
    {
        var target = new UsbDriveTarget
        {
            DriveLetter = "E:\\",
            VolumeLabel = "PENDRIVE",
            Vid = "048D",
            Pid = "1234",
            VendorName = "Chipsbank",
            IsSuspiciousController = true,
            SuspiciousReason = "Controlador suspeito frequentemente utilizado em réplicas",
            TotalBytes = 1024L * 1024 * 1024 * 64 // 64 GB
        };

        Assert.IsTrue(target.IsSuspiciousController);
        Assert.AreEqual("64,00 GB", target.TotalFormatted);
        StringAssert.Contains(target.SuspiciousReason, "Controlador");
    }

    [TestMethod]
    public async Task PostFormatManager_SessionStateSerialization_Works()
    {
        var session = new PostFormatSessionState
        {
            Status = "Idle",
            LastUpdated = DateTime.Now,
            Items = new List<PostFormatQueueItem>
            {
                new PostFormatQueueItem
                {
                    Title = "Drivers de Chipset",
                    Category = PostFormatCategory.Drivers,
                    Status = QueueItemStatus.Pending,
                    TargetPayload = "C:\\Backups\\Drivers"
                },
                new PostFormatQueueItem
                {
                    Title = "Redes Wi-Fi",
                    Category = PostFormatCategory.WiFi,
                    Status = QueueItemStatus.Completed,
                    TargetPayload = "C:\\Backups\\Wifi"
                }
            }
        };

        await PostFormatManager.SaveSessionAsync(session);

        var loaded = await PostFormatManager.LoadSessionAsync();
        Assert.IsNotNull(loaded);
        Assert.AreEqual(2, loaded.Items.Count);
        Assert.AreEqual("Drivers de Chipset", loaded.Items[0].Title);
        Assert.AreEqual(QueueItemStatus.Pending, loaded.Items[0].Status);
        Assert.AreEqual(QueueItemStatus.Completed, loaded.Items[1].Status);

        PostFormatManager.ClearSession();
        var cleared = await PostFormatManager.LoadSessionAsync();
        Assert.IsNull(cleared);
    }

    [TestMethod]
    public void WindowsRepairService_InitializesAllOfficialTools()
    {
        var tasks = WindowsRepairService.GetDefaultRepairTasks();

        Assert.IsNotNull(tasks);
        Assert.IsTrue(tasks.Count >= 7);

        // Verifica ferramentas essenciais
        Assert.IsTrue(tasks.Any(t => t.Id == "restore_point"));
        Assert.IsTrue(tasks.Any(t => t.Id == "chkdsk_scan"));
        Assert.IsTrue(tasks.Any(t => t.Id == "dism"));
        Assert.IsTrue(tasks.Any(t => t.Id == "sfc"));
        Assert.IsTrue(tasks.Any(t => t.Id == "wmi"));
        Assert.IsTrue(tasks.Any(t => t.Id == "icon_cache"));
        Assert.IsTrue(tasks.Any(t => t.Id == "network"));
    }

    [TestMethod]
    public async Task WindowsRepairService_Cancellation_AbortsGracefully()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancela imediatamente

        var tasks = new List<RepairTaskItem>
        {
            new RepairTaskItem { Id = "test", Name = "Tarefa de Teste" }
        };

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        {
            await WindowsRepairService.ExecuteRepairsAsync(tasks, _ => { }, cts.Token);
        });

        Assert.AreEqual(RepairStepStatus.Idle, tasks[0].Status);
    }

    private static string GetProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MainWindow.xaml")))
                return dir.FullName;
            string subDir = Path.Combine(dir.FullName, "LuminiTray");
            if (Directory.Exists(subDir) && File.Exists(Path.Combine(subDir, "MainWindow.xaml")))
                return subDir;
            dir = dir.Parent;
        }

        string fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (File.Exists(Path.Combine(fallback, "MainWindow.xaml")))
            return fallback;
        string fallbackSub = Path.Combine(fallback, "LuminiTray");
        if (Directory.Exists(fallbackSub) && File.Exists(Path.Combine(fallbackSub, "MainWindow.xaml")))
            return fallbackSub;

        return fallback;
    }

    [TestMethod]
    public void XamlResources_AllStaticAndDynamicResources_AreDefined()
    {
        string projectDir = GetProjectDirectory();
        string themePath = Path.Combine(projectDir, "Theme.xaml");
        string mainXamlPath = Path.Combine(projectDir, "MainWindow.xaml");

        Assert.IsTrue(File.Exists(themePath), $"Theme.xaml não encontrado em {themePath}");
        Assert.IsTrue(File.Exists(mainXamlPath), $"MainWindow.xaml não encontrado em {mainXamlPath}");

        string themeContent = File.ReadAllText(themePath);
        string mainContent = File.ReadAllText(mainXamlPath);

        var definedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(themeContent, @"x:Key=""([^""]+)"""))
            definedKeys.Add(m.Groups[1].Value.Trim());
        foreach (Match m in Regex.Matches(mainContent, @"x:Key=""([^""]+)"""))
            definedKeys.Add(m.Groups[1].Value.Trim());

        Assert.IsTrue(definedKeys.Contains("Display"), "Recurso 'Display' deve existir em Theme.xaml.");
        Assert.IsTrue(definedKeys.Contains("BoolVis"), "Recurso 'BoolVis' deve existir em Theme.xaml.");
        Assert.IsTrue(definedKeys.Contains("BoolToVis"), "Recurso 'BoolToVis' deve existir em Theme.xaml.");

        foreach (Match m in Regex.Matches(mainContent, @"\{StaticResource\s+([^}]+)\}"))
        {
            string key = m.Groups[1].Value.Trim();
            Assert.IsTrue(definedKeys.Contains(key), $"StaticResource '{key}' referenciado em MainWindow.xaml não está definido!");
        }

        foreach (Match m in Regex.Matches(mainContent, @"\{DynamicResource\s+([^}]+)\}"))
        {
            string key = m.Groups[1].Value.Trim();
            Assert.IsTrue(definedKeys.Contains(key), $"DynamicResource '{key}' referenciado em MainWindow.xaml não está definido!");
        }
    }

    [TestMethod]
    public void XamlNavigation_AllNavItemsAndGroups_HaveValidCheckedAndTags()
    {
        string projectDir = GetProjectDirectory();
        string mainXamlPath = Path.Combine(projectDir, "MainWindow.xaml");
        Assert.IsTrue(File.Exists(mainXamlPath), $"MainWindow.xaml não encontrado em {mainXamlPath}");

        string xaml = File.ReadAllText(mainXamlPath);

        // 1. Coleta todas as páginas declaradas na árvore visual (Page*)
        var declaredPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(xaml, @"(?:x:)?Name\s*=\s*""(Page[A-Za-z0-9_]+)"""))
        {
            declaredPages.Add(m.Groups[1].Value);
        }

        Assert.IsTrue(declaredPages.Count > 0, "Pelo menos uma página 'Page*' deve estar declarada no XAML.");

        // 2. Garante que nenhum ToggleButton com Style 'NavGroup' possua Checked="Nav_Checked"
        // (Prevenção de regressão do crash ao clicar no cabeçalho do grupo)
        var toggleButtons = Regex.Matches(xaml, @"<ToggleButton\b[^>]*>");
        int navGroupCount = 0;
        foreach (Match m in toggleButtons)
        {
            string tag = m.Value;
            if (tag.Contains("NavGroup"))
            {
                navGroupCount++;
                Assert.IsFalse(Regex.IsMatch(tag, @"\bChecked\s*=\s*""Nav_Checked"""),
                    $"ToggleButton de grupo não deve ter Checked='Nav_Checked': {tag}");
            }
        }
        Assert.IsTrue(navGroupCount >= 6, $"Esperado ao menos 6 grupos de navegação, encontrados: {navGroupCount}");

        // 3. Garante que todos os RadioButtons com Style 'NavItem' possuam Checked="Nav_Checked" e Tag válida apontando para uma página existente
        var radioButtons = Regex.Matches(xaml, @"<RadioButton\b[^>]*>");
        int navItemCount = 0;
        foreach (Match m in radioButtons)
        {
            string tag = m.Value;
            if (tag.Contains("NavItem"))
            {
                navItemCount++;
                var checkedMatch = Regex.Match(tag, @"\bChecked\s*=\s*""([^""]+)""");
                Assert.IsTrue(checkedMatch.Success, $"NavItem deve definir o evento Checked: {tag}");
                Assert.AreEqual("Nav_Checked", checkedMatch.Groups[1].Value.Trim(), $"NavItem deve ter Checked='Nav_Checked': {tag}");

                var tagMatch = Regex.Match(tag, @"\bTag\s*=\s*""([^""]+)""");
                Assert.IsTrue(tagMatch.Success, $"NavItem deve possuir um atributo Tag para roteamento: {tag}");
                string pageTarget = tagMatch.Groups[1].Value.Trim();

                // A tag deve iniciar com Page*
                Assert.IsTrue(pageTarget.StartsWith("Page", StringComparison.OrdinalIgnoreCase),
                    $"Tag do NavItem deve apontar para uma página 'Page*': Tag='{pageTarget}'");

                // A página deve ser uma página declarada diretamente no XAML, ou uma subpágina periférica roteada para PagePerifericos
                bool isValidPage = declaredPages.Contains(pageTarget)
                    || (pageTarget.StartsWith("PagePerif", StringComparison.OrdinalIgnoreCase) && declaredPages.Contains("PagePerifericos"));

                Assert.IsTrue(isValidPage, $"NavItem referencia página inexistente no XAML: Tag='{pageTarget}'");
            }
        }
        Assert.IsTrue(navItemCount >= 36, $"Esperado ao menos 36 itens de navegação, encontrados: {navItemCount}");
    }

    [TestMethod]
    public void XamlBindings_ProgressBarsAndReadOnlyProperties_UseOneWayMode()
    {
        string projectDir = GetProjectDirectory();
        string mainXamlPath = Path.Combine(projectDir, "MainWindow.xaml");
        Assert.IsTrue(File.Exists(mainXamlPath), $"MainWindow.xaml não encontrado em {mainXamlPath}");

        string xaml = File.ReadAllText(mainXamlPath);

        // 1. Verifica todas as ProgressBars no XAML cujo atributo Value seja um {Binding ...}
        // No WPF, ProgressBar.Value é TwoWay por padrão; se apontar para propriedade somente leitura sem Mode=OneWay,
        // causa XamlParseException fatal ao instanciar a janela.
        var pbMatches = Regex.Matches(xaml, @"<ProgressBar\b[^>]*>");
        int boundPbCount = 0;
        foreach (Match m in pbMatches)
        {
            string pbTag = m.Value;
            var valMatch = Regex.Match(pbTag, @"\bValue\s*=\s*""([^""]*\{Binding[^""]*\}[^""]*)""");
            if (valMatch.Success)
            {
                boundPbCount++;
                string bindingExpr = valMatch.Groups[1].Value;
                Assert.IsTrue(Regex.IsMatch(bindingExpr, @"\bMode\s*=\s*OneWay\b"),
                    $"ProgressBar.Value com associação dinâmica DEVE explicitar Mode=OneWay para prevenir falha em propriedades somente leitura: {bindingExpr}");
            }
        }
        Assert.IsTrue(boundPbCount >= 2, $"Esperado ao menos 2 ProgressBars com Binding Value auditadas, encontradas: {boundPbCount}");

        // 2. Verifica propriedades somente leitura conhecidas que requerem OneWay (ProgressPercentage, SpeedBarRatio, DimensionsText, FileSizeText)
        string[] readOnlyProps = { "ProgressPercentage", "SpeedBarRatio", "DimensionsText", "FileSizeText" };
        foreach (string prop in readOnlyProps)
        {
            var matches = Regex.Matches(xaml, @"\{Binding\s+[^}]*\b" + prop + @"\b[^}]*\}");
            Assert.IsTrue(matches.Count > 0, $"Propriedade somente leitura '{prop}' deve ser referenciada em ao menos um binding no XAML.");
            foreach (Match bm in matches)
            {
                string bExpr = bm.Value;
                Assert.IsTrue(Regex.IsMatch(bExpr, @"\bMode\s*=\s*OneWay\b"),
                    $"Binding para propriedade somente leitura '{prop}' DEVE explicitar Mode=OneWay: {bExpr}");
            }
        }
    }

    [TestMethod]
    public void XamlEventHandlers_AllCodeBehindHandlers_AreCorrectlyWiredInXaml()
    {
        string projectDir = GetProjectDirectory();
        string mainXamlPath = Path.Combine(projectDir, "MainWindow.xaml");
        Assert.IsTrue(File.Exists(mainXamlPath), $"MainWindow.xaml não encontrado em {mainXamlPath}");

        string xaml = File.ReadAllText(mainXamlPath);

        // 1. Extrai todos os métodos manipuladores de eventos declarados nos arquivos MainWindow*.cs
        var csFiles = Directory.GetFiles(projectDir, "MainWindow*.cs");
        Assert.IsTrue(csFiles.Length >= 30, $"Esperado ao menos 30 arquivos MainWindow*.cs, encontrados: {csFiles.Length}");

        var codeBehindHandlers = new HashSet<string>(StringComparer.Ordinal);
        var methodRegex = new Regex(
            @"(?:private|protected|public|internal)?\s*(?:async\s+)?void\s+([A-Za-z0-9_]+)\s*\(\s*object\??\s+[A-Za-z0-9_]+,\s*([A-Za-z0-9_<>.]+)\s+[A-Za-z0-9_]+\s*\)",
            RegexOptions.Compiled);

        foreach (string file in csFiles)
        {
            string content = File.ReadAllText(file);
            foreach (Match m in methodRegex.Matches(content))
            {
                string methodName = m.Groups[1].Value;
                string argType = m.Groups[2].Value;
                if (argType.Contains("Event") || argType == "EventArgs")
                {
                    codeBehindHandlers.Add(methodName);
                }
            }
        }
        Assert.IsTrue(codeBehindHandlers.Count >= 400, $"Esperado ao menos 400 manipuladores no code-behind, encontrados: {codeBehindHandlers.Count}");

        // 2. Extrai todos os manipuladores de eventos vinculados aos elementos interativos no MainWindow.xaml
        var eventAttrRegex = new Regex(
            @"\b(Click|Checked|Unchecked|SelectionChanged|TextChanged|ValueChanged|MouseDown|MouseUp|MouseMove|MouseEnter|MouseLeave|MouseWheel|MouseDoubleClick|PreviewKeyDown|PreviewKeyUp|KeyDown|KeyUp|SizeChanged|ScrollChanged|Loaded|Unloaded|Closing|StateChanged|PasswordChanged)\s*=\s*""([^""]+)""",
            RegexOptions.Compiled);

        var xamlWiredHandlers = new HashSet<string>(StringComparer.Ordinal);
        var missingInCodeBehind = new List<string>();

        foreach (Match m in eventAttrRegex.Matches(xaml))
        {
            string eventType = m.Groups[1].Value;
            string handlerName = m.Groups[2].Value.Trim();
            xamlWiredHandlers.Add(handlerName);

            // Garante que o manipulador chamado no XAML exista no code-behind
            if (!codeBehindHandlers.Contains(handlerName))
            {
                missingInCodeBehind.Add($"{handlerName} ({eventType})");
            }
        }

        Assert.AreEqual(0, missingInCodeBehind.Count,
            $"Manipuladores vinculados no MainWindow.xaml que não existem nos arquivos MainWindow*.cs: {string.Join(", ", missingInCodeBehind)}");

        // 3. Garante que os manipuladores de UI do code-behind (com convenções de nome de evento _Click, _Changed, _SelectionChanged, etc.)
        // estejam vinculados a controles do XAML (excluindo timers e wrappers conhecidos)
        var ignoredHandlers = new HashSet<string>(StringComparer.Ordinal)
        {
            "AudioTimer_Tick",
            "LightTick",
            "OnUfoRendering",
            "SuppressorMeterTimer_Tick",
            "WallpaperSlideshow_Tick",
            "BtnDiskTabBadBlocks_Click",
            "BtnDiskTabHealth_Click",
            "BtnDiskTabQuickClean_Click",
            "BtnDiskTabSpace_Click"
        };

        var unwiredUiHandlers = new List<string>();
        foreach (string handler in codeBehindHandlers)
        {
            if (ignoredHandlers.Contains(handler)) continue;

            if (handler.Contains('_'))
            {
                if (!xamlWiredHandlers.Contains(handler))
                {
                    unwiredUiHandlers.Add(handler);
                }
            }
        }

        Assert.AreEqual(0, unwiredUiHandlers.Count,
            $"Manipuladores de evento de UI no code-behind não vinculados no MainWindow.xaml: {string.Join(", ", unwiredUiHandlers)}");

        Assert.IsTrue(xamlWiredHandlers.Count >= 430,
            $"Total de manipuladores vinculados no XAML deve ser representativo de todas as 32 abas (> 430), encontrados: {xamlWiredHandlers.Count}");
    }
}
