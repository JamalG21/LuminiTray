using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Modelo com checkbox para a lista de softwares do Salvador de Programas (Winget).
/// </summary>
public sealed class SelectableSoftwareItem
{
    public InstalledSoftwareItem App { get; set; }
    public bool IsSelected { get; set; } = true;

    public string DisplayName => App.DisplayName;
    public string Publisher => string.IsNullOrWhiteSpace(App.Publisher) ? "Desconhecido" : App.Publisher;
    public string DisplayVersion => string.IsNullOrWhiteSpace(App.DisplayVersion) ? "—" : App.DisplayVersion;
    public string FormattedSize => App.FormattedSize;

    public SelectableSoftwareItem(InstalledSoftwareItem app)
    {
        App = app;
    }
}

public partial class MainWindow
{
    private readonly ObservableCollection<BackupFolderItem> _backupFolders = new();
    private readonly List<SelectableSoftwareItem> _allBackupApps = new();
    private readonly ObservableCollection<SelectableSoftwareItem> _filteredBackupApps = new();
    private CancellationTokenSource? _foldersBackupCts;
    private CancellationTokenSource? _driverBackupCts;
    private bool _backupPageInitialized;

    private async void InitBackupPage()
    {
        if (_backupPageInitialized) return;
        _backupPageInitialized = true;

        // 1. Inicializa lista de pastas pessoais
        _ = LoadWiFiProfilesAsync();
        _backupFolders.Clear();
        foreach (var f in BackupService.GetDefaultUserFolders())
        {
            _backupFolders.Add(f);
        }
        if (LstBackupFolders != null)
        {
            LstBackupFolders.ItemsSource = _backupFolders;
        }

        // Define pasta padrão de destino (se nenhuma estiver setada)
        if (TxtBackupDestFolder != null && string.IsNullOrWhiteSpace(TxtBackupDestFolder.Text))
        {
            // Tenta uma unidade secundária D:\ ou E:\, senão Área de Trabalho
            var secondaryDrive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed && !d.RootDirectory.FullName.StartsWith("C", StringComparison.OrdinalIgnoreCase));

            if (secondaryDrive != null)
            {
                TxtBackupDestFolder.Text = Path.Combine(secondaryDrive.RootDirectory.FullName, "LuminiTray-Backups");
            }
            else
            {
                TxtBackupDestFolder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LuminiTray-Backups");
            }
        }

        // 2. Calcula tamanhos das pastas em background
        _ = Task.Run(async () =>
        {
            try
            {
                await BackupService.CalculateFolderSizesAsync(_backupFolders, CancellationToken.None);
                await Dispatcher.InvokeAsync(UpdateTotalBackupFolderSize);
            }
            catch { }
        });

        // 3. Carrega lista de softwares instalados para o Salvador de Programas
        await LoadBackupAppsListAsync();
    }

    private async Task LoadBackupAppsListAsync()
    {
        try
        {
            if (TxtBackupAppsStatus != null)
                TxtBackupAppsStatus.Text = "Carregando softwares instalados no Windows...";

            var apps = await AppUninstaller.GetInstalledAppsAsync();
            _allBackupApps.Clear();

            // Filtra componentes de runtime/sistema puros (redistributables/updates) para mostrar programas reais
            foreach (var a in apps.OrderBy(x => x.DisplayName))
            {
                if (string.IsNullOrWhiteSpace(a.DisplayName)) continue;
                _allBackupApps.Add(new SelectableSoftwareItem(a));
            }

            FilterBackupApps();
        }
        catch (Exception ex)
        {
            if (TxtBackupAppsStatus != null)
                TxtBackupAppsStatus.Text = $"Erro ao listar programas: {ex.Message}";
        }
    }

    private void FilterBackupApps()
    {
        string filter = TxtSearchBackupApps?.Text?.Trim() ?? "";
        _filteredBackupApps.Clear();

        var query = _allBackupApps.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(a => a.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                     a.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            _filteredBackupApps.Add(item);
        }

        if (LstBackupApps != null)
        {
            LstBackupApps.ItemsSource = _filteredBackupApps;
        }

        if (TxtBackupAppsStatus != null)
        {
            int selected = _allBackupApps.Count(a => a.IsSelected);
            TxtBackupAppsStatus.Text = $"{selected} de {_allBackupApps.Count} programas selecionados";
        }
    }

    private void TxtSearchBackupApps_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterBackupApps();
    }

    private void BtnSelectAllBackupApps_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in _allBackupApps) a.IsSelected = true;
        FilterBackupApps();
    }

    private void BtnDeselectAllBackupApps_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in _allBackupApps) a.IsSelected = false;
        FilterBackupApps();
    }

    private void ChkBackupApp_Click(object sender, RoutedEventArgs e)
    {
        if (TxtBackupAppsStatus != null)
        {
            int selected = _allBackupApps.Count(a => a.IsSelected);
            TxtBackupAppsStatus.Text = $"{selected} de {_allBackupApps.Count} programas selecionados";
        }
    }

    private void ChkBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        UpdateTotalBackupFolderSize();
    }

    private void UpdateTotalBackupFolderSize()
    {
        if (LstBackupFolders != null)
        {
            LstBackupFolders.Items.Refresh();
        }

        if (TxtTotalFolderSize != null)
        {
            long total = _backupFolders.Where(f => f.IsSelected).Sum(f => f.TotalSizeBytes);
            int files = _backupFolders.Where(f => f.IsSelected).Sum(f => f.FileCount);
            TxtTotalFolderSize.Text = $"Total selecionado: {DiskSpaceAnalyzer.FormatSize(total)} ({files:N0} arquivos)";
        }
    }

    // =========================================================================
    // SALVADOR DE PROGRAMAS (WINGET)
    // =========================================================================

    private async void BtnGenerateAppInstaller_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allBackupApps.Where(a => a.IsSelected).Select(a => a.App).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Nenhum programa foi selecionado na lista para o backup.", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sfd = new SaveFileDialog
        {
            Title = "Onde você deseja salvar o Instalador de Programas?",
            FileName = "Instalar_Meus_Programas.bat",
            Filter = "Script Executável Batch (*.bat)|*.bat|Todos os Arquivos (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (sfd.ShowDialog() == true)
        {
            string path = sfd.FileName;
            var (success, msg) = await BackupService.GenerateWingetInstallerScriptAsync(selected, path);
            if (success)
            {
                Toast("Lumini Tray", "Instalador de programas gerado com sucesso!", "\uE718");
                if (MessageBox.Show($"{msg}\n\nDeseja abrir a pasta onde o arquivo foi salvo?", "Sucesso", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
            }
            else
            {
                MessageBox.Show(msg, "Erro ao Gerar Instalador", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // =========================================================================
    // SALVADOR DE DRIVERS (PNPUTIL)
    // =========================================================================

    private async void BtnGenerateDriverBackup_Click(object sender, RoutedEventArgs e)
    {
        // Pergunta onde salvar o pacote de restauração de drivers
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta onde deseja salvar o Salvador de Drivers",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        string targetDir = dialog.FolderName;
        _driverBackupCts = new CancellationTokenSource();

        if (BtnGenerateDriverBackup != null) BtnGenerateDriverBackup.IsEnabled = false;
        if (PbDriverBackup != null) { PbDriverBackup.Visibility = Visibility.Visible; PbDriverBackup.IsIndeterminate = true; }
        if (TxtDriverBackupStatus != null) TxtDriverBackupStatus.Text = "Exportando drivers do computador via pnputil...";

        try
        {
            var (success, msg, installerPath) = await BackupService.ExportDriversAndGenerateInstallerAsync(
                targetDir,
                text => Dispatcher.BeginInvoke(() => { if (TxtDriverBackupStatus != null) TxtDriverBackupStatus.Text = text; }),
                _driverBackupCts.Token);

            if (success)
            {
                Toast("Lumini Tray", "Drivers exportados e instalador gerado com sucesso!", "\uE718");
                if (MessageBox.Show($"{msg}\n\nInstalador gerado:\n{installerPath}\n\nDeseja abrir a pasta do backup?", "Sucesso", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{installerPath}\"") { UseShellExecute = true });
                }
            }
            else
            {
                MessageBox.Show(msg, "Falha na Exportação de Drivers", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            if (BtnGenerateDriverBackup != null) BtnGenerateDriverBackup.IsEnabled = true;
            if (PbDriverBackup != null) PbDriverBackup.Visibility = Visibility.Collapsed;
            if (TxtDriverBackupStatus != null) TxtDriverBackupStatus.Text = "Pronto para gerar o instalador de drivers.";
        }
    }

    // =========================================================================
    // BACKUP DE PASTAS PESSOAIS
    // =========================================================================

    private void BtnBrowseBackupDest_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta de destino do backup",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            if (TxtBackupDestFolder != null)
                TxtBackupDestFolder.Text = dialog.FolderName;
        }
    }

    private async void BtnStartFoldersBackup_Click(object sender, RoutedEventArgs e)
    {
        var selected = _backupFolders.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Selecione pelo menos uma pasta pessoal para o backup.", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string dest = TxtBackupDestFolder?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(dest))
        {
            MessageBox.Show("Escolha uma pasta de destino válida para salvar o backup.", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(dest))
        {
            try { Directory.CreateDirectory(dest); }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível criar a pasta de destino: {ex.Message}", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        bool compressZip = RbBackupZip?.IsChecked == true;
        _foldersBackupCts = new CancellationTokenSource();

        if (BtnStartFoldersBackup != null) BtnStartFoldersBackup.IsEnabled = false;
        if (BtnCancelFoldersBackup != null) BtnCancelFoldersBackup.IsEnabled = true;
        if (PbFoldersBackup != null) { PbFoldersBackup.Visibility = Visibility.Visible; PbFoldersBackup.Value = 0; PbFoldersBackup.IsIndeterminate = false; }

        var progress = new Progress<BackupProgress>(p =>
        {
            if (PbFoldersBackup != null) PbFoldersBackup.Value = p.Percentage;
            if (TxtFoldersBackupStatus != null)
                TxtFoldersBackupStatus.Text = $"Copiando: {p.CurrentFile} ({p.Percentage:0.0}%) - {DiskSpaceAnalyzer.FormatSize(p.CopiedBytes)} de {DiskSpaceAnalyzer.FormatSize(p.TotalBytes)}";
        });

        try
        {
            var (success, msg) = await BackupService.ExecuteFoldersBackupAsync(selected, dest, compressZip, progress, _foldersBackupCts.Token);
            if (success)
            {
                Toast("Lumini Tray", "Backup de arquivos concluído com sucesso!", "\uE718");
                if (MessageBox.Show($"{msg}\n\nDeseja abrir a pasta de destino?", "Sucesso", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", dest) { UseShellExecute = true });
                }
            }
            else
            {
                MessageBox.Show(msg, "Resultado do Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            if (BtnStartFoldersBackup != null) BtnStartFoldersBackup.IsEnabled = true;
            if (BtnCancelFoldersBackup != null) BtnCancelFoldersBackup.IsEnabled = false;
            if (PbFoldersBackup != null) PbFoldersBackup.Visibility = Visibility.Collapsed;
            if (TxtFoldersBackupStatus != null) TxtFoldersBackupStatus.Text = "Pronto para iniciar novo backup de arquivos.";
        }
    }

    private void BtnCancelFoldersBackup_Click(object sender, RoutedEventArgs e)
    {
        _foldersBackupCts?.Cancel();
        if (TxtFoldersBackupStatus != null)
            TxtFoldersBackupStatus.Text = "Cancelando backup de arquivos...";
    }

    // ---------------- Backup e Restauração de Redes Wi-Fi (HUB Pro) ----------------

    private List<WiFiNetworkProfile> _wifiProfiles = new();

    private async Task LoadWiFiProfilesAsync()
    {
        try
        {
            var list = await BackupService.ListSavedWiFiProfilesAsync();
            _wifiProfiles = list;
            Dispatcher.Invoke(() =>
            {
                if (LstWifiProfiles != null) LstWifiProfiles.ItemsSource = _wifiProfiles;
                if (TxtBackupWifiStatus != null)
                {
                    TxtBackupWifiStatus.Text = _wifiProfiles.Count == 0
                        ? "Nenhuma rede Wi-Fi salva encontrada neste computador."
                        : $"{_wifiProfiles.Count} rede(s) Wi-Fi salvas identificadas e prontas para exportar.";
                }
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtBackupWifiStatus != null)
                    TxtBackupWifiStatus.Text = $"Não foi possível listar redes Wi-Fi: {ex.Message}";
            });
        }
    }

    private async void BtnBackupWifi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Escolha onde salvar o backup das redes Wi-Fi (ex: seu Pendrive)"
        };

        if (dialog.ShowDialog() != true) return;
        string targetDir = dialog.FolderName;

        if (BtnBackupWifi != null) BtnBackupWifi.IsEnabled = false;
        if (TxtBackupWifiStatus != null) TxtBackupWifiStatus.Text = "Exportando redes Wi-Fi e gerando script de restauração...";

        try
        {
            var (ok, msg, exported) = await BackupService.ExportWiFiProfilesAsync(targetDir);
            Toast("Backup Wi-Fi", msg, ok ? "\uE701" : "\uE783");

            if (ok)
            {
                var r = MessageBox.Show(
                    $"{msg}\n\nDeseja abrir a pasta onde o backup foi salvo agora?",
                    "Backup Concluído",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (r == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", targetDir) { UseShellExecute = true });
                }
            }
            else
            {
                MessageBox.Show(msg, "Backup Wi-Fi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao exportar Wi-Fi: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (BtnBackupWifi != null) BtnBackupWifi.IsEnabled = true;
            _ = LoadWiFiProfilesAsync();
        }
    }

    private async void BtnRestoreWifi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Selecione a pasta onde estão os arquivos de backup do Wi-Fi"
        };

        if (dialog.ShowDialog() != true) return;
        string sourceDir = dialog.FolderName;

        if (BtnRestoreWifi != null) BtnRestoreWifi.IsEnabled = false;
        if (TxtBackupWifiStatus != null) TxtBackupWifiStatus.Text = "Restaurando perfis Wi-Fi no Windows...";

        try
        {
            var (ok, msg, restored) = await BackupService.RestoreWiFiProfilesAsync(sourceDir);
            Toast("Restauração Wi-Fi", msg, ok ? "\uE701" : "\uE783");
            MessageBox.Show(msg, "Restauração de Redes Wi-Fi", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao restaurar Wi-Fi: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (BtnRestoreWifi != null) BtnRestoreWifi.IsEnabled = true;
            _ = LoadWiFiProfilesAsync();
        }
    }
}
