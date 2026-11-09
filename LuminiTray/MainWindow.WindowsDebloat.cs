using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

public partial class MainWindow
{
    private List<WindowsTweakItem>? _debloatTweaks;
    private List<DebloatAppItem> _scannedBloatware = new();
    private HardwareProfileRecommendation? _hardwareProfile;
    private CancellationTokenSource? _debloatCts;
    private bool _isScanningBloatware;
    private bool _isUninstallingBloatware;

    public void InitWindowsDebloatPage()
    {
        // 1. Inicializa os Tweaks de CPU, GPU, Input e Sistema
        if (_debloatTweaks == null)
        {
            _debloatTweaks = WindowsDebloatService.GetAllTweaks();

            // Divide por categorias e vincula às listas da interface
            if (ItemsTweaksCpu != null)
                ItemsTweaksCpu.ItemsSource = _debloatTweaks.Where(t => t.Category.Contains("CPU")).ToList();

            if (ItemsTweaksGpu != null)
                ItemsTweaksGpu.ItemsSource = _debloatTweaks.Where(t => t.Category.Contains("GPU")).ToList();

            if (ItemsTweaksInput != null)
                ItemsTweaksInput.ItemsSource = _debloatTweaks.Where(t => t.Category.Contains("Latência")).ToList();

            if (ItemsTweaksSystem != null)
                ItemsTweaksSystem.ItemsSource = _debloatTweaks.Where(t => t.Category.Contains("Telemetria")).ToList();
        }

        // Atualiza o estado real dos interruptores no registro
        foreach (var t in _debloatTweaks)
        {
            t.Refresh();
        }

        // 2. Detecta o perfil de hardware de forma assíncrona (não congela a interface)
        _ = RefreshHardwareProfileUiAsync();

        // 3. Atualiza estado do botão de reversão
        UpdateRevertButtonState();

        // 4. Varre bloatware automaticamente na primeira abertura se a lista estiver vazia
        if (_scannedBloatware.Count == 0 && !_isScanningBloatware)
        {
            _ = ScanBloatwareAsync();
        }
    }

    private async Task RefreshHardwareProfileUiAsync()
    {
        if (_hardwareProfile != null)
        {
            UpdateHardwareUi(_hardwareProfile);
            return;
        }

        try
        {
            var profile = await Task.Run(() => WindowsDebloatService.DetectHardwareProfile());
            _hardwareProfile = profile;
            UpdateHardwareUi(profile);
        }
        catch
        {
            if (TxtHardwareSummary != null)
                TxtHardwareSummary.Text = "Hardware detectado · Clique em Otimizar para aplicar o perfil ideal";
        }
    }

    private void UpdateHardwareUi(HardwareProfileRecommendation profile)
    {
        if (TxtHardwareSummary != null)
            TxtHardwareSummary.Text = profile.HardwareSummary;

        if (TxtProfileBadge != null)
            TxtProfileBadge.Text = profile.ProfileTitle;

        if (TxtProfileDescription != null)
            TxtProfileDescription.Text = profile.ProfileDescription;

        if (LstProfileHighlights != null)
            LstProfileHighlights.ItemsSource = profile.OptimizationHighlights;
    }

    private void UpdateRevertButtonState()
    {
        if (BtnRevertDebloatTweaks == null) return;

        bool hasBackups = _settings.TweakBackup.Keys.Any(k =>
            k.StartsWith("cpu_") || k.StartsWith("gpu_") || k.StartsWith("input_") ||
            k.StartsWith("net_") || k.StartsWith("sys_") || k.StartsWith("taskbar_") ||
            k.StartsWith("explorer_") || k.StartsWith("search_") || k.StartsWith("privacy_") || k.StartsWith("perf_"));

        BtnRevertDebloatTweaks.IsEnabled = hasBackups;
        BtnRevertDebloatTweaks.Opacity = hasBackups ? 1.0 : 0.6;
        BtnRevertDebloatTweaks.ToolTip = hasBackups
            ? "Restaura todos os valores originais do registro guardados antes das modificações."
            : "Nenhuma modificação foi registrada no backup ainda. O sistema já está nos padrões.";
    }

    // ------------------------------------------------------------------ Navegação de Sub-Abas

    private void BtnSubTabTweaks_Click(object sender, RoutedEventArgs e)
    {
        if (ViewWindowsTweaks == null || ViewWindowsDebloat == null) return;

        ViewWindowsTweaks.Visibility = Visibility.Visible;
        ViewWindowsDebloat.Visibility = Visibility.Collapsed;

        BtnSubTabTweaks.Style = (Style)FindResource("PrimaryButton");
        BtnSubTabDebloat.Style = (Style)FindResource("OutlineButton");
    }

    private void BtnSubTabDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (ViewWindowsTweaks == null || ViewWindowsDebloat == null) return;

        ViewWindowsTweaks.Visibility = Visibility.Collapsed;
        ViewWindowsDebloat.Visibility = Visibility.Visible;

        BtnSubTabTweaks.Style = (Style)FindResource("OutlineButton");
        BtnSubTabDebloat.Style = (Style)FindResource("PrimaryButton");

        if (_scannedBloatware.Count == 0 && !_isScanningBloatware)
        {
            _ = ScanBloatwareAsync();
        }
    }

    // ------------------------------------------------------------------ Perfil Adaptativo & Reversão

    private async void BtnApplyAdaptiveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_hardwareProfile == null || _debloatTweaks == null) return;

        BtnApplyAdaptiveProfile.IsEnabled = false;
        BtnApplyAdaptiveProfile.Content = "Aplicando Perfil Adaptativo...";

        try
        {
            await WindowsDebloatService.ApplyProfileAsync(_hardwareProfile, _settings, _debloatTweaks);
            UpdateRevertButtonState();

            Toast("Windows & Debloat", $"Perfil Adaptativo aplicado com sucesso!\n{_hardwareProfile.RecommendedTweakIds.Count} ajustes foram calibrados para seu hardware.", "\uE770");
        }
        catch (Exception ex)
        {
            Toast("Windows & Debloat", $"Erro ao aplicar perfil: {ex.Message}", "\uE783");
        }
        finally
        {
            BtnApplyAdaptiveProfile.IsEnabled = true;
            BtnApplyAdaptiveProfile.Content = "⚡ Otimizar para meu Hardware (Perfil Adaptativo)";
        }
    }

    private async void BtnRevertDebloatTweaks_Click(object sender, RoutedEventArgs e)
    {
        if (_debloatTweaks == null) return;

        bool hasBackups = _settings.TweakBackup.Keys.Any(k =>
            k.StartsWith("cpu_") || k.StartsWith("gpu_") || k.StartsWith("input_") ||
            k.StartsWith("net_") || k.StartsWith("sys_") || k.StartsWith("taskbar_") ||
            k.StartsWith("explorer_") || k.StartsWith("search_") || k.StartsWith("privacy_") || k.StartsWith("perf_"));

        if (!hasBackups)
        {
            MessageBox.Show("Nenhum ajuste foi alterado pelo aplicativo ainda. O sistema já está nos padrões originais.", "Restaurar Padrões", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            "Deseja desfazer todas as modificações de CPU, GPU e Sistema e restaurar os valores originais de fábrica do Windows?",
            "Restaurar Padrões do Windows",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        BtnRevertDebloatTweaks.IsEnabled = false;
        BtnRevertDebloatTweaks.Content = "Restaurando Padrões...";

        try
        {
            await WindowsDebloatService.RevertAllTweaksAsync(_settings, _debloatTweaks);
            UpdateRevertButtonState();

            Toast("Windows & Debloat", "Todas as modificações foram desfeitas com sucesso!\nValores de fábrica do Windows restaurados.", "\uE770");
        }
        catch (Exception ex)
        {
            Toast("Windows & Debloat", $"Erro ao restaurar padrões: {ex.Message}", "\uE783");
        }
        finally
        {
            BtnRevertDebloatTweaks.IsEnabled = true;
            BtnRevertDebloatTweaks.Content = "↩ Desfazer Todas as Modificações";
        }
    }

    private void TweakDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not WindowsTweakItem tweak) return;

        bool newState = cb.IsChecked == true;
        try
        {
            tweak.Write(newState, _settings);
            _settings.Save();
            tweak.Refresh();
            UpdateRevertButtonState();

            if (tweak.NeedsRestart)
            {
                Toast("Windows & Debloat", $"{tweak.Title}: {(newState ? "Ativado" : "Desativado")}.\nReinicie o computador para efetivar a alteração no driver/kernel.", "\uE770");
            }
        }
        catch (Exception ex)
        {
            Toast("Windows & Debloat", $"Erro ao alterar {tweak.Title}: {ex.Message}", "\uE783");
            tweak.Refresh();
        }
    }

    // ------------------------------------------------------------------ Debloat de Aplicativos

    private async void BtnScanBloatware_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstallingBloatware || _isScanningBloatware) return;
        await ScanBloatwareAsync();
    }

    private async Task ScanBloatwareAsync()
    {
        if (_isScanningBloatware || _isUninstallingBloatware) return;
        _isScanningBloatware = true;

        if (PbDebloatScan != null) PbDebloatScan.Visibility = Visibility.Visible;
        if (BtnScanBloatware != null) BtnScanBloatware.IsEnabled = false;
        if (TxtBloatwareCount != null) TxtBloatwareCount.Text = "Varrendo aplicativos...";

        var progress = new Progress<string>(msg =>
        {
            if (TxtBloatwareCount != null) TxtBloatwareCount.Text = msg;
        });

        try
        {
            var apps = await WindowsDebloatService.ScanInstalledBloatwareAsync(progress);
            _scannedBloatware = apps;

            ApplyBloatwareFilter();

            if (TxtBloatwareCount != null)
                TxtBloatwareCount.Text = $"{apps.Count} bloatwares detectados no seu Windows";
        }
        catch (Exception ex)
        {
            if (TxtBloatwareCount != null)
                TxtBloatwareCount.Text = $"Erro na varredura: {ex.Message}";
        }
        finally
        {
            _isScanningBloatware = false;
            if (PbDebloatScan != null) PbDebloatScan.Visibility = Visibility.Collapsed;
            if (BtnScanBloatware != null) BtnScanBloatware.IsEnabled = !_isUninstallingBloatware;
        }
    }

    private void TxtDebloatSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyBloatwareFilter();
    }

    private void ApplyBloatwareFilter()
    {
        if (LstDebloatApps == null) return;

        string query = TxtDebloatSearch?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(query))
        {
            LstDebloatApps.ItemsSource = _scannedBloatware.ToList();
        }
        else
        {
            LstDebloatApps.ItemsSource = _scannedBloatware.Where(a =>
                a.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.AppId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Vendor.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        UpdateUninstallButtonCount();
    }

    private void BtnSelectSafeBloatware_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstallingBloatware) return;
        foreach (var app in _scannedBloatware)
        {
            app.IsSelected = app.Recommendation.Equals("safe", StringComparison.OrdinalIgnoreCase);
        }
        UpdateUninstallButtonCount();
    }

    private void BtnSelectOemBloatware_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstallingBloatware) return;
        foreach (var app in _scannedBloatware)
        {
            app.IsSelected = app.IsOem;
        }
        UpdateUninstallButtonCount();
    }

    private void BtnDeselectAllBloatware_Click(object sender, RoutedEventArgs e)
    {
        if (_isUninstallingBloatware) return;
        foreach (var app in _scannedBloatware)
        {
            app.IsSelected = false;
        }
        UpdateUninstallButtonCount();
    }

    private void DebloatItemCheckbox_Click(object sender, RoutedEventArgs e)
    {
        UpdateUninstallButtonCount();
    }

    private void UpdateUninstallButtonCount()
    {
        if (BtnUninstallSelectedBloatware == null) return;

        int count = _scannedBloatware.Count(a => a.IsSelected);
        BtnUninstallSelectedBloatware.Content = count > 0
            ? $"Desinstalar Selecionados ({count})"
            : "Desinstalar Selecionados";
        BtnUninstallSelectedBloatware.IsEnabled = count > 0 && !_isUninstallingBloatware;
    }

    private async void BtnUninstallSelectedBloatware_Click(object sender, RoutedEventArgs e)
    {
        var selected = _scannedBloatware.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0 || _isUninstallingBloatware) return;

        var confirm = MessageBox.Show(
            $"Deseja desinstalar os {selected.Count} aplicativo(s) selecionados do seu Windows?",
            "Confirmar Debloat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _isUninstallingBloatware = true;
        _debloatCts = new CancellationTokenSource();

        if (BtnScanBloatware != null) BtnScanBloatware.IsEnabled = false;

        if (PbDebloatRemoval != null)
        {
            PbDebloatRemoval.Visibility = Visibility.Visible;
            PbDebloatRemoval.Value = 0;
            PbDebloatRemoval.Maximum = selected.Count;
        }

        if (TxtDebloatRemovalStatus != null)
            TxtDebloatRemovalStatus.Visibility = Visibility.Visible;

        BtnUninstallSelectedBloatware.IsEnabled = false;

        var progress = new Progress<DebloatProgressReport>(report =>
        {
            if (PbDebloatRemoval != null) PbDebloatRemoval.Value = report.CurrentIndex;
            if (TxtDebloatRemovalStatus != null) TxtDebloatRemovalStatus.Text = report.StatusMessage;
        });

        try
        {
            var result = await WindowsDebloatService.UninstallAppsAsync(selected, progress, _debloatCts.Token);

            Toast("Windows & Debloat", $"Debloat Concluído!\n{result.SuccessCount} aplicativo(s) removidos com sucesso." +
                      (result.ErrorCount > 0 ? $"\n{result.ErrorCount} aplicativo(s) apresentaram falha." : ""), "\uE770");

            // Remove os itens desinstalados com sucesso da lista local
            _scannedBloatware.RemoveAll(a => !a.IsInstalled);
            ApplyBloatwareFilter();

            if (TxtBloatwareCount != null)
                TxtBloatwareCount.Text = $"{_scannedBloatware.Count} bloatwares detectados no seu Windows";
        }
        catch (Exception ex)
        {
            Toast("Windows & Debloat", $"Erro durante o debloat: {ex.Message}", "\uE783");
        }
        finally
        {
            _isUninstallingBloatware = false;
            if (BtnScanBloatware != null) BtnScanBloatware.IsEnabled = true;
            if (PbDebloatRemoval != null) PbDebloatRemoval.Visibility = Visibility.Collapsed;
            if (TxtDebloatRemovalStatus != null) TxtDebloatRemovalStatus.Visibility = Visibility.Collapsed;
            UpdateUninstallButtonCount();
        }
    }

    private async void BtnReinstallAllWindowsApps_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Deseja reinstalar e re-registrar todos os aplicativos e componentes nativos padrão do Windows?\n\n" +
            "Este processo restaura pacotes AppX originais (como Microsoft Store, Calculadora, Fotos, etc.) caso tenham sido removidos.\n" +
            "O procedimento pode levar alguns minutos.",
            "Reinstalar Aplicativos do Windows",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        if (PbDebloatScan != null) PbDebloatScan.Visibility = Visibility.Visible;
        if (TxtDebloatRemovalStatus != null)
        {
            TxtDebloatRemovalStatus.Visibility = Visibility.Visible;
            TxtDebloatRemovalStatus.Text = "Reinstalando e restaurando pacotes AppX nativos do Windows...";
        }
        if (BtnReinstallAllWindowsApps != null) BtnReinstallAllWindowsApps.IsEnabled = false;

        var progress = new Progress<string>(status =>
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtDebloatRemovalStatus != null) TxtDebloatRemovalStatus.Text = status;
            });
        });

        try
        {
            var (ok, msg, restoredCount) = await WindowsDebloatService.ReinstallAllDefaultWindowsAppsAsync(progress);
            MessageBox.Show(msg, "Reinstalação de Apps do Windows", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro durante a restauração: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (PbDebloatScan != null) PbDebloatScan.Visibility = Visibility.Collapsed;
            if (TxtDebloatRemovalStatus != null) TxtDebloatRemovalStatus.Visibility = Visibility.Collapsed;
            if (BtnReinstallAllWindowsApps != null) BtnReinstallAllWindowsApps.IsEnabled = true;
            BtnScanBloatware_Click(this, new RoutedEventArgs());
        }
    }
}
