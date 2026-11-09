using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

/// <summary>Aba RESET FERRAMENTAS · Instalar apps: essenciais pós-formatação organizados por categoria pelo winget.</summary>
public partial class MainWindow
{
    private List<InstallableApp>? _installApps;
    private string _selectedAppCategory = "TODOS";
    private bool _appsBusy, _appsStop;

    private void InitApps()
    {
        if (_appsBusy) return;
        if (_installApps == null)
        {
            _installApps = AppInstaller.Catalog();
            AppInstaller.RefreshInstalled(_installApps);
            foreach (var a in _installApps) a.Selected = a.PreSelect && !a.Installed;
        }
        else AppInstaller.RefreshInstalled(_installApps);

        ApplyAppsFilter();

        bool winget = AppInstaller.WingetAvailable;
        BtnAppsStore.Visibility = winget ? Visibility.Collapsed : Visibility.Visible;
        BtnAppsInstall.IsEnabled = winget;
        UpdateAppsStatus(winget);
    }

    private void ApplyAppsFilter()
    {
        if (_installApps == null) return;
        if (_selectedAppCategory == "TODOS")
        {
            AppsList.ItemsSource = _installApps;
        }
        else
        {
            AppsList.ItemsSource = _installApps
                .Where(a => a.Category.Equals(_selectedAppCategory, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void UpdateAppsStatus(bool winget)
    {
        if (_installApps == null) return;
        int installed = _installApps.Count(a => a.Installed);
        int total = _installApps.Count;
        AppsStatus.Text = winget
            ? $"{installed} de {total} apps já instalados no sistema. Marque os programas desejados nas categorias e clique em INSTALAR SELECIONADOS."
            : "O winget não foi encontrado. Em PC recém-formatado ele aparece depois que a Microsoft Store atualiza o " +
              "\"Instalador de Aplicativo\": use o botão abaixo, espere atualizar e abra esta página de novo.";
    }

    private void BtnAppsCat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string cat)
        {
            _selectedAppCategory = cat;
            ApplyAppsFilter();
        }
    }

    private void BtnAppsAll_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null || _appsBusy) return;
        var targetApps = _selectedAppCategory == "TODOS"
            ? (IEnumerable<InstallableApp>)_installApps
            : _installApps.Where(a => a.Category.Equals(_selectedAppCategory, StringComparison.OrdinalIgnoreCase));

        foreach (var a in targetApps) a.Selected = !a.Installed;
    }

    private void BtnAppsNone_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null || _appsBusy) return;
        var targetApps = _selectedAppCategory == "TODOS"
            ? (IEnumerable<InstallableApp>)_installApps
            : _installApps.Where(a => a.Category.Equals(_selectedAppCategory, StringComparison.OrdinalIgnoreCase));

        foreach (var a in targetApps) a.Selected = false;
    }

    private void BtnAppsStore_Click(object sender, RoutedEventArgs e)
    {
        // Página do "Instalador de Aplicativo" (App Installer), que traz o winget.
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGH4NNS1") { UseShellExecute = true }); }
        catch (Exception ex) { AppsStatus.Text = $"Não foi possível abrir a Microsoft Store: {ex.Message}"; }
    }

    private async void BtnAppsInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null) return;
        if (_appsBusy) { _appsStop = true; BtnAppsInstall.IsEnabled = false; AppsStatus.Text = "Parando depois do app atual…"; return; }

        var sel = _installApps.Where(a => a.Selected && !a.Installed).ToList();
        if (sel.Count == 0) { AppsStatus.Text = "Nenhum app selecionado."; return; }
        if (sel.Any(a => a.WingetId == "RARLab.WinRAR") && sel.Any(a => a.WingetId == "7zip.7zip"))
        {
            var r = MessageBox.Show("WinRAR e 7-Zip fazem a mesma coisa. Instalar os dois mesmo assim?", "Instalar apps",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        _appsBusy = true; _appsStop = false;
        BtnAppsInstall.Content = "PARAR";
        foreach (var a in sel) a.Status = "Na fila.";
        int ok = 0, done = 0;
        var failed = new List<string>();
        AppsProgress.ScaleX = 0;

        foreach (var app in sel)
        {
            if (_appsStop) { app.Status = "Não instalado (parado)."; continue; }
            AppsStatus.Text = $"Instalando {app.Name} ({done + 1} de {sel.Count})… baixando do site oficial ({app.Publisher}), pode levar alguns minutos.";
            app.Status = "Instalando…";
            try
            {
                var (success, msg) = await AppInstaller.InstallAsync(app, CancellationToken.None);
                app.Status = msg;
                if (success) { ok++; app.Selected = false; }
                else failed.Add(app.Name);
            }
            catch (Exception ex) { app.Status = $"Falhou: {ex.Message}"; failed.Add(app.Name); }
            done++;
            AppsProgress.ScaleX = (double)done / sel.Count;
        }

        AppInstaller.RefreshInstalled(_installApps);
        _appsBusy = false;
        BtnAppsInstall.Content = "INSTALAR SELECIONADOS";
        BtnAppsInstall.IsEnabled = true;
        AppsStatus.Text = $"{ok} de {sel.Count} instalados." +
                          (failed.Count > 0 ? $" Falharam: {string.Join(", ", failed)} (veja o motivo na lista)." : "") +
                          (_appsStop ? " Instalação interrompida." : "");
    }

    // ---------------- Sub-Abas e Atualizador de Programas (HUB Pro) ----------------

    private List<UpgradableAppItem> _upgradableApps = new();
    private bool _isScanningUpgrades;
    private bool _isUpgrading;

    private void BtnTabAppsInstall_Click(object sender, RoutedEventArgs e)
    {
        if (ViewAppsInstall != null) ViewAppsInstall.Visibility = Visibility.Visible;
        if (ViewAppsUpdate != null) ViewAppsUpdate.Visibility = Visibility.Collapsed;
        BtnTabAppsInstall.Style = (Style)FindResource("PrimaryButton");
        BtnTabAppsUpdate.Style = (Style)FindResource("OutlineButton");
    }

    private void BtnTabAppsUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewAppsInstall != null) ViewAppsInstall.Visibility = Visibility.Collapsed;
        if (ViewAppsUpdate != null) ViewAppsUpdate.Visibility = Visibility.Visible;
        BtnTabAppsInstall.Style = (Style)FindResource("OutlineButton");
        BtnTabAppsUpdate.Style = (Style)FindResource("PrimaryButton");

        if (_upgradableApps.Count == 0 && !_isScanningUpgrades)
        {
            BtnAppsUpdateCheck_Click(this, new RoutedEventArgs());
        }
    }

    private async void BtnAppsUpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanningUpgrades || _isUpgrading) return;
        _isScanningUpgrades = true;

        if (PbAppsUpdate != null) PbAppsUpdate.Visibility = Visibility.Visible;
        if (TxtAppsUpdateStatus != null) TxtAppsUpdateStatus.Text = "Consultando atualizações disponíveis via winget...";
        if (BtnAppsUpdateCheck != null) BtnAppsUpdateCheck.IsEnabled = false;

        try
        {
            var apps = await AppInstaller.CheckUpgradesAsync();
            _upgradableApps = apps;
            ApplyAppsUpdateFilter();

            if (TxtAppsUpdateStatus != null)
            {
                TxtAppsUpdateStatus.Text = _upgradableApps.Count == 0
                    ? "Todos os seus programas estão atualizados para a versão mais recente!"
                    : $"{_upgradableApps.Count} programa(s) com nova versão disponível para atualizar.";
            }
        }
        catch (Exception ex)
        {
            if (TxtAppsUpdateStatus != null)
                TxtAppsUpdateStatus.Text = $"Erro ao verificar atualizações: {ex.Message}";
        }
        finally
        {
            _isScanningUpgrades = false;
            if (PbAppsUpdate != null) PbAppsUpdate.Visibility = Visibility.Collapsed;
            if (BtnAppsUpdateCheck != null) BtnAppsUpdateCheck.IsEnabled = true;
        }
    }

    private void ApplyAppsUpdateFilter()
    {
        if (LstUpgradableApps == null) return;
        string query = TxtAppsUpdateSearch?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            LstUpgradableApps.ItemsSource = _upgradableApps;
        }
        else
        {
            LstUpgradableApps.ItemsSource = _upgradableApps.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private void TxtAppsUpdateSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyAppsUpdateFilter();
    }

    private async void BtnAppsUpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _upgradableApps.Where(a => a.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Nenhum programa selecionado para atualizar.", "Atualizar Programas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_isUpgrading) return;
        _isUpgrading = true;

        if (PbAppsUpdate != null) PbAppsUpdate.Visibility = Visibility.Visible;
        if (BtnAppsUpdateSelected != null) BtnAppsUpdateSelected.IsEnabled = false;
        if (BtnAppsUpdateAll != null) BtnAppsUpdateAll.IsEnabled = false;

        try
        {
            int ok = 0;
            foreach (var app in selected)
            {
                app.Status = "Atualizando...";
                if (TxtAppsUpdateStatus != null) TxtAppsUpdateStatus.Text = $"Atualizando {app.Name} para versão {app.AvailableVersion}...";

                var (success, msg) = await AppInstaller.UpgradeAppAsync(app, CancellationToken.None);
                app.Status = success ? "Atualizado ✔" : "Falhou";
                if (success)
                {
                    ok++;
                    app.Selected = false;
                }
            }

            Toast("Atualização de Programas", $"{ok} de {selected.Count} programa(s) atualizados com sucesso.", "\uE719");
        }
        finally
        {
            _isUpgrading = false;
            if (PbAppsUpdate != null) PbAppsUpdate.Visibility = Visibility.Collapsed;
            if (BtnAppsUpdateSelected != null) BtnAppsUpdateSelected.IsEnabled = true;
            if (BtnAppsUpdateAll != null) BtnAppsUpdateAll.IsEnabled = true;
            if (TxtAppsUpdateStatus != null) TxtAppsUpdateStatus.Text = "Atualizações concluídas.";
        }
    }

    private async void BtnAppsUpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpgrading) return;
        _isUpgrading = true;

        if (PbAppsUpdate != null) PbAppsUpdate.Visibility = Visibility.Visible;
        if (TxtAppsUpdateStatus != null) TxtAppsUpdateStatus.Text = "Atualizando todos os programas em lote silencioso pelo winget...";
        if (BtnAppsUpdateSelected != null) BtnAppsUpdateSelected.IsEnabled = false;
        if (BtnAppsUpdateAll != null) BtnAppsUpdateAll.IsEnabled = false;

        try
        {
            var (ok, msg) = await AppInstaller.UpgradeAllAsync(CancellationToken.None);
            Toast("Atualização Completa", msg, ok ? "\uE73E" : "\uE783");
            BtnAppsUpdateCheck_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro durante a atualização: {ex.Message}", "Atualizações", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isUpgrading = false;
            if (PbAppsUpdate != null) PbAppsUpdate.Visibility = Visibility.Collapsed;
            if (BtnAppsUpdateSelected != null) BtnAppsUpdateSelected.IsEnabled = true;
            if (BtnAppsUpdateAll != null) BtnAppsUpdateAll.IsEnabled = true;
        }
    }

    private async void BtnUpgradeSingleApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UpgradableAppItem app }) return;
        if (_isUpgrading) return;
        _isUpgrading = true;

        app.Status = "Atualizando...";
        if (TxtAppsUpdateStatus != null) TxtAppsUpdateStatus.Text = $"Atualizando {app.Name}...";

        try
        {
            var (success, msg) = await AppInstaller.UpgradeAppAsync(app, CancellationToken.None);
            app.Status = success ? "Atualizado ✔" : "Falhou";
            Toast("Atualização", $"{app.Name}: {msg}", success ? "\uE73E" : "\uE783");
        }
        catch (Exception ex)
        {
            app.Status = $"Falhou: {ex.Message}";
        }
        finally
        {
            _isUpgrading = false;
            if (TxtAppsUpdateStatus != null) TxtAppsUpdateStatus.Text = "Pronto.";
        }
    }
}
