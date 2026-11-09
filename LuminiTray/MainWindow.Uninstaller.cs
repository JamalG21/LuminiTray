using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

/// <summary>
/// Módulo de Desinstalação Avançada de Softwares com Multi-Source Factory, Desinstalação em Lote,
/// Opção de Abrir Local do Arquivo, Filtro de Componentes Ocultos e Varredura Profunda de Resíduos.
/// </summary>
public partial class MainWindow
{
    private List<InstalledSoftwareItem> _allInstalledApps = new();
    private List<InstalledSoftwareItem> _filteredInstalledApps = new();
    private LeftoverScanReport? _currentLeftoverReport;
    private string _activeCategoryTag = "All";
    private bool _uninstallerBusy;

    private async void InitUninstaller()
    {
        if (_uninstallerBusy || _allInstalledApps.Count > 0) return;
        await LoadInstalledAppsAsync();
    }

    private async Task LoadInstalledAppsAsync()
    {
        _uninstallerBusy = true;
        BtnUninstallRefresh.IsEnabled = false;
        TxtUninstallStatus.Text = "Varrendo sistema por programas Desktop (Win32), Microsoft Store, Steam, Portáteis e Componentes…";

        try
        {
            _allInstalledApps = await AppUninstaller.GetInstalledAppsAsync();
            FilterInstalledApps();
            TxtUninstallStatus.Text = $"{_allInstalledApps.Count} instalações detectadas no sistema.";
        }
        catch (Exception ex)
        {
            TxtUninstallStatus.Text = $"Erro ao listar aplicativos: {ex.Message}";
        }
        finally
        {
            _uninstallerBusy = false;
            BtnUninstallRefresh.IsEnabled = true;
        }
    }

    private void FilterInstalledApps()
    {
        string query = TxtUninstallSearch?.Text?.Trim() ?? "";
        bool showHidden = ChkShowHiddenApps?.IsChecked == true;

        IEnumerable<InstalledSoftwareItem> list = _allInstalledApps;

        // Filtro de Programas Ocultos / Componentes sem interface gráfica
        if (!showHidden)
        {
            list = list.Where(a => !a.IsHiddenOrComponent);
        }

        // Filtro por Categoria
        if (_activeCategoryTag != "All")
        {
            list = _activeCategoryTag switch
            {
                "DesktopWin32" => list.Where(a => a.Category == AppCategory.DesktopWin32),
                "MicrosoftStore" => list.Where(a => a.Category == AppCategory.MicrosoftStore),
                "SteamGame" => list.Where(a => a.Category == AppCategory.SteamGame),
                "PortableDirectory" => list.Where(a => a.Category == AppCategory.PortableDirectory),
                _ => list
            };
        }

        // Filtro por Texto de Busca
        if (!string.IsNullOrWhiteSpace(query))
        {
            list = list.Where(a => a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  a.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  (!string.IsNullOrEmpty(a.InstallLocation) && a.InstallLocation.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                                  a.DisplayVersion.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _filteredInstalledApps = list.ToList();

        if (LstInstalledApps != null)
        {
            LstInstalledApps.ItemsSource = null;
            LstInstalledApps.ItemsSource = _filteredInstalledApps;
        }

        int visibleTotal = _allInstalledApps.Count(a => showHidden || !a.IsHiddenOrComponent);
        if (TxtUninstallCount != null)
        {
            TxtUninstallCount.Text = $"{_filteredInstalledApps.Count} de {visibleTotal} programas exibidos";
        }

        // Atualiza contadores dos botões de filtro
        if (TxtFilterAllCount != null) TxtFilterAllCount.Text = $"{visibleTotal}";
        if (TxtFilterWin32Count != null) TxtFilterWin32Count.Text = $"{_allInstalledApps.Count(a => (showHidden || !a.IsHiddenOrComponent) && a.Category == AppCategory.DesktopWin32)}";
        if (TxtFilterStoreCount != null) TxtFilterStoreCount.Text = $"{_allInstalledApps.Count(a => (showHidden || !a.IsHiddenOrComponent) && a.Category == AppCategory.MicrosoftStore)}";
        if (TxtFilterSteamCount != null) TxtFilterSteamCount.Text = $"{_allInstalledApps.Count(a => (showHidden || !a.IsHiddenOrComponent) && a.Category == AppCategory.SteamGame)}";
        if (TxtFilterPortableCount != null) TxtFilterPortableCount.Text = $"{_allInstalledApps.Count(a => (showHidden || !a.IsHiddenOrComponent) && a.Category == AppCategory.PortableDirectory)}";

        UpdateBatchButtonState();
    }

    private void BtnFilterCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeCategoryTag = tag;

            // Atualiza visual dos botões
            var primaryStyle = TryFindResource("PrimaryButton") as Style;
            var outlineStyle = TryFindResource("OutlineButton") as Style;

            if (BtnFilterAll != null) BtnFilterAll.Style = tag == "All" ? primaryStyle : outlineStyle;
            if (BtnFilterWin32 != null) BtnFilterWin32.Style = tag == "DesktopWin32" ? primaryStyle : outlineStyle;
            if (BtnFilterStore != null) BtnFilterStore.Style = tag == "MicrosoftStore" ? primaryStyle : outlineStyle;
            if (BtnFilterSteam != null) BtnFilterSteam.Style = tag == "SteamGame" ? primaryStyle : outlineStyle;
            if (BtnFilterPortable != null) BtnFilterPortable.Style = tag == "PortableDirectory" ? primaryStyle : outlineStyle;

            FilterInstalledApps();
        }
    }

    private void TxtUninstallSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterInstalledApps();
    }

    private void ChkShowHiddenApps_Changed(object sender, RoutedEventArgs e)
    {
        FilterInstalledApps();
    }

    private async void BtnUninstallRefresh_Click(object sender, RoutedEventArgs e)
    {
        _allInstalledApps.Clear();
        await LoadInstalledAppsAsync();
    }

    #region Caixa de Seleção & Desinstalação em Lote

    private void ChkSelectAllApps_Checked(object sender, RoutedEventArgs e)
    {
        foreach (var app in _filteredInstalledApps)
        {
            app.IsSelected = true;
        }

        if (LstInstalledApps != null)
        {
            LstInstalledApps.ItemsSource = null;
            LstInstalledApps.ItemsSource = _filteredInstalledApps;
        }

        UpdateBatchButtonState();
    }

    private void ChkSelectAllApps_Unchecked(object sender, RoutedEventArgs e)
    {
        foreach (var app in _allInstalledApps)
        {
            app.IsSelected = false;
        }

        if (LstInstalledApps != null)
        {
            LstInstalledApps.ItemsSource = null;
            LstInstalledApps.ItemsSource = _filteredInstalledApps;
        }

        UpdateBatchButtonState();
    }

    private void BtnClearSelection_Click(object sender, RoutedEventArgs e)
    {
        if (ChkSelectAllApps != null) ChkSelectAllApps.IsChecked = false;

        foreach (var app in _allInstalledApps)
        {
            app.IsSelected = false;
        }

        if (LstInstalledApps != null)
        {
            LstInstalledApps.ItemsSource = null;
            LstInstalledApps.ItemsSource = _filteredInstalledApps;
        }

        UpdateBatchButtonState();
    }

    private void ChkAppItem_Changed(object sender, RoutedEventArgs e)
    {
        UpdateBatchButtonState();
    }

    private void UpdateBatchButtonState()
    {
        if (BtnUninstallBatch == null) return;

        int selectedCount = _allInstalledApps.Count(a => a.IsSelected);
        BtnUninstallBatch.Content = $"🗑️ Desinstalar Selecionados ({selectedCount})";
        BtnUninstallBatch.IsEnabled = selectedCount > 0;
    }

    private async void BtnUninstallBatch_Click(object sender, RoutedEventArgs e)
    {
        var selectedApps = _allInstalledApps.Where(a => a.IsSelected).ToList();
        if (selectedApps.Count == 0)
        {
            MessageBox.Show("Nenhum programa foi marcado nas caixas de seleção.", "Desinstalar em Lote", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string appList = string.Join("\n", selectedApps.Take(12).Select(a => $"• {a.DisplayName}"));
        if (selectedApps.Count > 12)
        {
            appList += $"\n... e mais {selectedApps.Count - 12} programa(s)";
        }

        var confirm = MessageBox.Show(
            $"Tem certeza absoluta que deseja desinstalar os {selectedApps.Count} programas selecionados ao mesmo tempo?\n\n{appList}\n\n" +
            "Os desinstaladores oficiais serão executados em sequência.",
            "Confirmar Desinstalação em Lote",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        TxtUninstallStatus.Text = $"Iniciando desinstalação em lote de {selectedApps.Count} programas…";
        int total = selectedApps.Count;
        int successCount = 0;

        for (int i = 0; i < total; i++)
        {
            var app = selectedApps[i];
            TxtUninstallStatus.Text = $"Desinstalando {i + 1} de {total}: {app.DisplayName}…";
            bool ok = await AppUninstaller.RunUninstallerAsync(app, silent: false);
            if (ok) successCount++;
        }

        TxtUninstallStatus.Text = $"Desinstalação em lote finalizada: {successCount} de {total} programas concluídos.";

        var cleanAsk = MessageBox.Show(
            $"A desinstalação dos programas foi finalizada ({successCount}/{total} executados).\n\n" +
            "Deseja iniciar a varredura profunda de resíduos no disco e no Registro para os programas desinstalados?",
            "Limpeza Profunda em Lote",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (cleanAsk == MessageBoxResult.Yes)
        {
            await StartBatchDeepScanAsync(selectedApps);
        }
        else
        {
            await LoadInstalledAppsAsync();
        }
    }

    private async Task StartBatchDeepScanAsync(List<InstalledSoftwareItem> apps)
    {
        TxtUninstallStatus.Text = $"Realizando varredura profunda de resíduos para {apps.Count} aplicativos desinstalados…";
        var consolidatedReport = new LeftoverScanReport
        {
            App = new InstalledSoftwareItem
            {
                DisplayName = $"{apps.Count} Programas Selecionados",
                Publisher = "Desinstalação em Lote"
            }
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        foreach (var app in apps)
        {
            try
            {
                var rep = await AppUninstaller.ScanLeftoversAsync(app, cts.Token);
                consolidatedReport.FilesAndFolders.AddRange(rep.FilesAndFolders);
                consolidatedReport.Shortcuts.AddRange(rep.Shortcuts);
                consolidatedReport.RegistryEntries.AddRange(rep.RegistryEntries);
                consolidatedReport.FirewallRules.AddRange(rep.FirewallRules);
                consolidatedReport.SystemTraces.AddRange(rep.SystemTraces);
            }
            catch { }
        }

        // Desduplica caminhos
        consolidatedReport.FilesAndFolders = consolidatedReport.FilesAndFolders
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        consolidatedReport.Shortcuts = consolidatedReport.Shortcuts
            .GroupBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        consolidatedReport.RegistryEntries = consolidatedReport.RegistryEntries
            .GroupBy(r => r.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        consolidatedReport.FirewallRules = consolidatedReport.FirewallRules
            .GroupBy(f => f.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        consolidatedReport.SystemTraces = consolidatedReport.SystemTraces
            .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        _currentLeftoverReport = consolidatedReport;
        ShowLeftoverReport(consolidatedReport);
        TxtUninstallStatus.Text = $"Varredura concluída: {consolidatedReport.TotalItemsCount} resíduos encontrados ({consolidatedReport.FormattedTotalSize}).";
    }

    #endregion

    #region Ações Individuais (Abrir Local, Desinstalar, Desinstalação Profunda)

    private void BtnOpenInstallLocation_Click(object sender, RoutedEventArgs e)
    {
        var app = (sender as FrameworkElement)?.Tag as InstalledSoftwareItem ?? LstInstalledApps.SelectedItem as InstalledSoftwareItem;
        if (app == null) return;
        OpenInstallLocation(app);
    }

    private void OpenInstallLocation(InstalledSoftwareItem app)
    {
        try
        {
            string? targetPath = null;
            bool isFile = false;

            // 1. Tenta o diretório de instalação registrado
            if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                targetPath = app.InstallLocation;
                isFile = false;
            }
            // 2. Tenta o caminho do ícone do programa
            else if (!string.IsNullOrWhiteSpace(app.DisplayIcon))
            {
                string iconPath = app.DisplayIcon.Trim('"', ' ');
                int commaIdx = iconPath.IndexOf(',');
                if (commaIdx > 0) iconPath = iconPath[..commaIdx].Trim();
                if (File.Exists(iconPath))
                {
                    targetPath = iconPath;
                    isFile = true;
                }
                else if (Directory.Exists(iconPath))
                {
                    targetPath = iconPath;
                    isFile = false;
                }
            }

            // 3. Tenta extrair o executável da string de desinstalação
            if (targetPath == null && !string.IsNullOrWhiteSpace(app.UninstallString))
            {
                string exePath = AppUninstaller.ExtractExecutablePath(app.UninstallString);
                if (File.Exists(exePath))
                {
                    targetPath = exePath;
                    isFile = true;
                }
                else
                {
                    string? dir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        targetPath = dir;
                        isFile = false;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                if (isFile)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{targetPath}\"") { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{targetPath}\"") { UseShellExecute = true });
                }
            }
            else
            {
                MessageBox.Show(
                    $"Não foi possível localizar uma pasta de arquivos físicos para \"{app.DisplayName}\".\n\n" +
                    "O software pode ser um aplicativo empacotado da Microsoft Store, um runtime ou um componente do Registro.",
                    "Abrir Local do Arquivo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao abrir local do arquivo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnUninstallNormal_Click(object sender, RoutedEventArgs e)
    {
        var app = (sender as FrameworkElement)?.Tag as InstalledSoftwareItem ?? LstInstalledApps.SelectedItem as InstalledSoftwareItem;
        if (app == null) return;

        var r = MessageBox.Show(
            $"Tem certeza que deseja desinstalar o programa:\n\n\"{app.DisplayName}\"?\n\n" +
            $"Fabricante: {app.Publisher}\n" +
            $"Versão: {app.DisplayVersion}\n\n" +
            "O desinstalador oficial do software será aberto.",
            "Confirmar Desinstalação",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (r != MessageBoxResult.Yes) return;

        TxtUninstallStatus.Text = $"Executando desinstalador de {app.DisplayName}…";
        bool ok = await AppUninstaller.RunUninstallerAsync(app, silent: false);

        if (ok)
        {
            var cleanAsk = MessageBox.Show(
                $"O desinstalador de \"{app.DisplayName}\" foi finalizado.\n\nDeseja realizar a varredura profunda em busca de pastas, atalhos e chaves no Registro deixados para trás?",
                "Limpeza Profunda de Resíduos",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (cleanAsk == MessageBoxResult.Yes)
            {
                await StartDeepScanAsync(app);
            }
            else
            {
                await LoadInstalledAppsAsync();
            }
        }
    }

    private async void BtnUninstallDeep_Click(object sender, RoutedEventArgs e)
    {
        var app = (sender as FrameworkElement)?.Tag as InstalledSoftwareItem ?? LstInstalledApps.SelectedItem as InstalledSoftwareItem;
        if (app == null) return;

        var r = MessageBox.Show(
            $"Tem certeza que deseja realizar a Desinstalação Profunda de:\n\n\"{app.DisplayName}\"?\n\n" +
            "1. O desinstalador oficial do programa será executado primeiro.\n" +
            "2. Em seguida, faremos uma varredura completa em busca de pastas, arquivos, atalhos, chaves no Registro e regras de firewall deixadas para trás.",
            "Confirmar Desinstalação Profunda",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (r != MessageBoxResult.Yes) return;

        TxtUninstallStatus.Text = $"Executando desinstalador de {app.DisplayName}…";
        await AppUninstaller.RunUninstallerAsync(app, silent: false);

        await StartDeepScanAsync(app);
    }

    private async Task StartDeepScanAsync(InstalledSoftwareItem app)
    {
        TxtUninstallStatus.Text = $"Varrendo disco, registro, atalhos e firewall por resíduos de {app.DisplayName}…";
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var report = await AppUninstaller.ScanLeftoversAsync(app, cts.Token);

        _currentLeftoverReport = report;
        ShowLeftoverReport(report);
        TxtUninstallStatus.Text = $"Varredura concluída: {report.TotalItemsCount} resíduos encontrados ({report.FormattedTotalSize}).";
    }

    #endregion

    #region Painel de Revisão de Resíduos Pós-Desinstalação

    private void ShowLeftoverReport(LeftoverScanReport report)
    {
        TxtLeftoverAppName.Text = report.App.DisplayName;
        TxtLeftoverSummary.Text = $"{report.FilesAndFolders.Count} pastas/arquivos ({report.FormattedTotalSize}), {report.Shortcuts.Count} atalhos, {report.RegistryEntries.Count} chaves no Registro, {report.FirewallRules.Count} regras do Firewall e {report.SystemTraces.Count} rastros do sistema.";

        if (LstLeftoverFiles != null)
        {
            LstLeftoverFiles.ItemsSource = null;
            LstLeftoverFiles.ItemsSource = report.FilesAndFolders;
        }

        if (LstLeftoverShortcuts != null)
        {
            LstLeftoverShortcuts.ItemsSource = null;
            LstLeftoverShortcuts.ItemsSource = report.Shortcuts;
        }

        if (LstLeftoverRegistry != null)
        {
            LstLeftoverRegistry.ItemsSource = null;
            LstLeftoverRegistry.ItemsSource = report.RegistryEntries;
        }

        if (LstLeftoverFirewall != null)
        {
            LstLeftoverFirewall.ItemsSource = null;
            LstLeftoverFirewall.ItemsSource = report.FirewallRules;
        }

        LeftoverReviewPanel.Visibility = Visibility.Visible;
    }

    private void BtnSelectAllLeftovers_Click(object sender, RoutedEventArgs e)
    {
        if (_currentLeftoverReport == null) return;
        foreach (var f in _currentLeftoverReport.FilesAndFolders) f.Selected = true;
        foreach (var s in _currentLeftoverReport.Shortcuts) s.Selected = true;
        foreach (var t in _currentLeftoverReport.SystemTraces) t.Selected = true;
        foreach (var r in _currentLeftoverReport.RegistryEntries) r.Selected = true;
        foreach (var fw in _currentLeftoverReport.FirewallRules) fw.Selected = true;

        RefreshLeftoverLists();
    }

    private void BtnDeselectAllLeftovers_Click(object sender, RoutedEventArgs e)
    {
        if (_currentLeftoverReport == null) return;
        foreach (var f in _currentLeftoverReport.FilesAndFolders) f.Selected = false;
        foreach (var s in _currentLeftoverReport.Shortcuts) s.Selected = false;
        foreach (var t in _currentLeftoverReport.SystemTraces) t.Selected = false;
        foreach (var r in _currentLeftoverReport.RegistryEntries) r.Selected = false;
        foreach (var fw in _currentLeftoverReport.FirewallRules) fw.Selected = false;

        RefreshLeftoverLists();
    }

    private void RefreshLeftoverLists()
    {
        if (_currentLeftoverReport == null) return;
        if (LstLeftoverFiles != null) { LstLeftoverFiles.ItemsSource = null; LstLeftoverFiles.ItemsSource = _currentLeftoverReport.FilesAndFolders; }
        if (LstLeftoverShortcuts != null) { LstLeftoverShortcuts.ItemsSource = null; LstLeftoverShortcuts.ItemsSource = _currentLeftoverReport.Shortcuts; }
        if (LstLeftoverRegistry != null) { LstLeftoverRegistry.ItemsSource = null; LstLeftoverRegistry.ItemsSource = _currentLeftoverReport.RegistryEntries; }
        if (LstLeftoverFirewall != null) { LstLeftoverFirewall.ItemsSource = null; LstLeftoverFirewall.ItemsSource = _currentLeftoverReport.FirewallRules; }
    }

    private async void BtnCleanLeftoversNow_Click(object sender, RoutedEventArgs e)
    {
        if (_currentLeftoverReport == null) return;

        int selectedFiles = _currentLeftoverReport.FilesAndFolders.Count(f => f.Selected) +
                            _currentLeftoverReport.Shortcuts.Count(s => s.Selected) +
                            _currentLeftoverReport.SystemTraces.Count(t => t.Selected);

        int selectedReg = _currentLeftoverReport.RegistryEntries.Count(r => r.Selected) +
                          _currentLeftoverReport.FirewallRules.Count(fw => fw.Selected);

        if (selectedFiles == 0 && selectedReg == 0)
        {
            MessageBox.Show("Nenhum resíduo selecionado para remoção.", "Resíduos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var r = MessageBox.Show(
            $"Confirmar exclusão permanente de:\n• {selectedFiles} pastas, arquivos e atalhos residuais\n• {selectedReg} chaves no Registro e regras de firewall?",
            "Excluir Resíduos",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (r != MessageBoxResult.Yes) return;

        BtnCleanLeftoversNow.IsEnabled = false;
        var (filesDel, regDel) = await AppUninstaller.CleanLeftoversAsync(_currentLeftoverReport);
        BtnCleanLeftoversNow.IsEnabled = true;

        LeftoverReviewPanel.Visibility = Visibility.Collapsed;
        MessageBox.Show($"Limpeza profunda concluída!\n{filesDel} pastas/arquivos/atalhos e {regDel} entradas de registro/firewall foram eliminados com sucesso.",
            "Resíduos Eliminados", MessageBoxButton.OK, MessageBoxImage.Information);

        // Atualiza a lista de aplicativos
        BtnUninstallRefresh_Click(this, new RoutedEventArgs());
    }

    private void BtnCloseLeftovers_Click(object sender, RoutedEventArgs e)
    {
        LeftoverReviewPanel.Visibility = Visibility.Collapsed;
    }

    #endregion
}
