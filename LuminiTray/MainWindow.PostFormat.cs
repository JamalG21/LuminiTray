using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LuminiTray;

public partial class MainWindow
{
    private PostFormatSessionState? _currentPostFormatSession;
    private CancellationTokenSource? _postFormatCts;
    private readonly ObservableCollection<PostFormatQueueItem> _postFormatQueueList = new();
    private DiscoveredBackups? _discoveredBackups;

    private async void InitPostFormatPage()
    {
        if (LstPostFormatQueue != null)
        {
            LstPostFormatQueue.ItemsSource = _postFormatQueueList;
        }

        // Tenta carregar sessão salva previamente
        var savedState = await PostFormatManager.LoadSessionAsync();
        if (savedState != null && savedState.Items.Count > 0)
        {
            _currentPostFormatSession = savedState;
            SyncPostFormatUiFromState(savedState);
        }
        else
        {
            await RunPostFormatScanAsync();
        }
    }

    private async Task RunPostFormatScanAsync(string? explicitFolder = null)
    {
        if (TxtPostFormatScanStatus != null)
            TxtPostFormatScanStatus.Text = "Procurando backups em pendrives e discos conectados...";

        _discoveredBackups = await PostFormatManager.ScanForBackupsAsync(explicitFolder);

        if (TxtPostFormatScanStatus != null)
        {
            if (_discoveredBackups.HasAny)
            {
                TxtPostFormatScanStatus.Text = $"Backups encontrados: {_discoveredBackups.DriverCount} drivers, {_discoveredBackups.WifiCount} redes Wi-Fi e {_discoveredBackups.WingetAppIds.Count} programas Winget.";
            }
            else
            {
                TxtPostFormatScanStatus.Text = "Nenhum backup encontrado nos discos e pendrives conectados. Conecte o pendrive com o backup e clique em 'Procurar de novo'.";
            }
        }

        if (TxtPostFormatDiscoveredDrivers != null)
        {
            TxtPostFormatDiscoveredDrivers.Text = !string.IsNullOrEmpty(_discoveredBackups.DriverFolder)
                ? $"Pasta: {_discoveredBackups.DriverFolder} ({_discoveredBackups.DriverCount} drivers)"
                : "Nenhum backup de drivers detectado.";
        }

        if (TxtPostFormatDiscoveredWifi != null)
        {
            TxtPostFormatDiscoveredWifi.Text = !string.IsNullOrEmpty(_discoveredBackups.WifiFolder)
                ? $"Pasta: {_discoveredBackups.WifiFolder} ({_discoveredBackups.WifiCount} rede(s) com senha)"
                : "Nenhum backup de redes Wi-Fi detectado.";
        }

        if (TxtPostFormatDiscoveredApps != null)
        {
            TxtPostFormatDiscoveredApps.Text = _discoveredBackups.WingetAppIds.Count > 0
                ? $"{_discoveredBackups.WingetAppIds.Count} programa(s) exportado(s) para instalação automática."
                : "Nenhuma lista de programas exportados encontrada.";
        }

        bool hasDrivers = !string.IsNullOrEmpty(_discoveredBackups.DriverFolder) && _discoveredBackups.DriverCount > 0;
        bool hasWifi = !string.IsNullOrEmpty(_discoveredBackups.WifiFolder) && _discoveredBackups.WifiCount > 0;
        bool hasApps = _discoveredBackups.WingetAppIds.Count > 0;

        if (ChkPostFormatCategoryDrivers != null)
        {
            ChkPostFormatCategoryDrivers.IsEnabled = hasDrivers;
            ChkPostFormatCategoryDrivers.IsChecked = hasDrivers;
        }
        if (BtnPostFormatRunDrivers != null) BtnPostFormatRunDrivers.IsEnabled = hasDrivers;

        if (ChkPostFormatCategoryWifi != null)
        {
            ChkPostFormatCategoryWifi.IsEnabled = hasWifi;
            ChkPostFormatCategoryWifi.IsChecked = hasWifi;
        }
        if (BtnPostFormatRunWifi != null) BtnPostFormatRunWifi.IsEnabled = hasWifi;

        if (ChkPostFormatCategoryApps != null)
        {
            ChkPostFormatCategoryApps.IsEnabled = hasApps;
            ChkPostFormatCategoryApps.IsChecked = hasApps;
        }
        if (BtnPostFormatRunApps != null) BtnPostFormatRunApps.IsEnabled = hasApps;

        // Se não houver sessão ativa, monta fila inicial baseada no que foi achado
        if (_currentPostFormatSession == null || _currentPostFormatSession.Items.Count == 0)
        {
            BuildDefaultPostFormatQueue();
        }
    }

    private void BuildDefaultPostFormatQueue()
    {
        _postFormatQueueList.Clear();
        var session = new PostFormatSessionState();

        // 1. Ponto de restauração de segurança
        var itemRp = new PostFormatQueueItem
        {
            Id = "restore_point",
            Title = "Proteção do Sistema (Ponto de Restauração)",
            Subtitle = "Cria um ponto de retorno antes de iniciar as instalações",
            Category = PostFormatCategory.RestorePoint,
            Status = QueueItemStatus.Pending
        };
        session.Items.Add(itemRp);
        _postFormatQueueList.Add(itemRp);

        // 2. Drivers (se achou)
        if (_discoveredBackups != null && !string.IsNullOrEmpty(_discoveredBackups.DriverFolder))
        {
            var itemDrv = new PostFormatQueueItem
            {
                Id = "drivers",
                Title = $"Restaurar Drivers ({_discoveredBackups.DriverCount} detectados)",
                Subtitle = _discoveredBackups.DriverFolder,
                Category = PostFormatCategory.Drivers,
                TargetPayload = _discoveredBackups.DriverFolder,
                Status = QueueItemStatus.Pending
            };
            session.Items.Add(itemDrv);
            _postFormatQueueList.Add(itemDrv);
        }

        // 3. Wi-Fi (se achou)
        if (_discoveredBackups != null && !string.IsNullOrEmpty(_discoveredBackups.WifiFolder))
        {
            var itemWifi = new PostFormatQueueItem
            {
                Id = "wifi",
                Title = $"Restaurar Redes Wi-Fi e Senhas ({_discoveredBackups.WifiCount} perfis)",
                Subtitle = _discoveredBackups.WifiFolder,
                Category = PostFormatCategory.WiFi,
                TargetPayload = _discoveredBackups.WifiFolder,
                Status = QueueItemStatus.Pending
            };
            session.Items.Add(itemWifi);
            _postFormatQueueList.Add(itemWifi);
        }

        // 4. Debloat do Windows
        var itemDebloat = new PostFormatQueueItem
        {
            Id = "debloat",
            Title = "Limpar Apps Inúteis do Windows (Debloat)",
            Subtitle = "Remove telemetria e jogos patrocinados que vêm instalados",
            Category = PostFormatCategory.Debloat,
            Status = QueueItemStatus.Pending
        };
        session.Items.Add(itemDebloat);
        _postFormatQueueList.Add(itemDebloat);

        // 5. Apps Winget encontrados
        if (_discoveredBackups != null && _discoveredBackups.WingetAppIds.Count > 0)
        {
            foreach (var appId in _discoveredBackups.WingetAppIds)
            {
                var itemApp = new PostFormatQueueItem
                {
                    Id = "app_" + appId,
                    Title = $"Instalar {appId}",
                    Subtitle = "Via Microsoft Winget (Instalação Silenciosa)",
                    Category = PostFormatCategory.WingetApp,
                    TargetPayload = appId,
                    Status = QueueItemStatus.Pending
                };
                session.Items.Add(itemApp);
                _postFormatQueueList.Add(itemApp);
            }
        }

        // 6. Tweaks de performance
        var itemTweaks = new PostFormatQueueItem
        {
            Id = "tweaks",
            Title = "Otimizações de Desempenho e Privacidade",
            Subtitle = "Desativa telemetria agressiva e GameBar",
            Category = PostFormatCategory.Tweaks,
            Status = QueueItemStatus.Pending
        };
        session.Items.Add(itemTweaks);
        _postFormatQueueList.Add(itemTweaks);

        _currentPostFormatSession = session;
        UpdatePostFormatSummary();
    }

    private void SyncPostFormatUiFromState(PostFormatSessionState state)
    {
        _postFormatQueueList.Clear();
        foreach (var it in state.Items)
        {
            _postFormatQueueList.Add(it);
        }
        UpdatePostFormatSummary();
    }

    private void UpdatePostFormatSummary()
    {
        if (_currentPostFormatSession == null) return;
        int total = _currentPostFormatSession.TotalItems;
        int selected = _currentPostFormatSession.SelectedItems;
        int completed = _currentPostFormatSession.CompletedItems;
        int pending = _currentPostFormatSession.PendingItems;

        if (TxtPostFormatQueueSummary != null)
        {
            TxtPostFormatQueueSummary.Text = $"{selected} de {total} itens selecionados · {completed} concluídos · {pending} pendentes";
        }

        bool isRunning = _currentPostFormatSession.Status == "Running";
        if (BtnPostFormatExecuteAll != null)
        {
            if (isRunning)
            {
                BtnPostFormatExecuteAll.Content = "EXECUTANDO...";
                BtnPostFormatExecuteAll.IsEnabled = false;
            }
            else
            {
                BtnPostFormatExecuteAll.Content = pending > 0
                    ? (selected < total ? $"📥 EXECUTAR SELECIONADOS ({pending})" : $"📥 EXECUTAR TUDO ({pending})")
                    : "📥 NENHUM ITEM PENDENTE";
                BtnPostFormatExecuteAll.IsEnabled = pending > 0;
            }
        }

        if (BtnPostFormatPause != null)
        {
            BtnPostFormatPause.IsEnabled = isRunning;
        }

        if (PgbPostFormatProgress != null)
        {
            PgbPostFormatProgress.Maximum = Math.Max(1, total);
            PgbPostFormatProgress.Value = completed;
        }

        SyncCategoryCheckboxes();
    }

    private async void BtnPostFormatScanAgain_Click(object sender, RoutedEventArgs e)
    {
        await RunPostFormatScanAsync();
    }

    private async void BtnPostFormatChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Selecione a pasta onde está salvo o backup de drivers ou Wi-Fi",
            Multiselect = false
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
        {
            await RunPostFormatScanAsync(dlg.FolderName);
        }
    }

    private async void BtnPostFormatExecuteAll_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPostFormatSession == null || _currentPostFormatSession.Items.Count == 0) return;

        _postFormatCts?.Cancel();
        _postFormatCts = new CancellationTokenSource();

        _currentPostFormatSession.Status = "Running";
        if (ChkPostFormatAutoResume != null)
            _currentPostFormatSession.AutoResumeOnStartup = ChkPostFormatAutoResume.IsChecked == true;

        UpdatePostFormatSummary();

        try
        {
            await PostFormatManager.ExecuteQueueAsync(_currentPostFormatSession, (st, log) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (TxtPostFormatCurrentAction != null)
                        TxtPostFormatCurrentAction.Text = log;
                    UpdatePostFormatSummary();
                    LstPostFormatQueue?.Items.Refresh();
                });
            }, _postFormatCts.Token);

            if (_currentPostFormatSession.Status == "Completed")
            {
                int total = _currentPostFormatSession.TotalItems;
                int selected = _currentPostFormatSession.SelectedItems;
                string msg = selected < total
                    ? "Os itens selecionados da pós-formatação foram concluídos com sucesso!"
                    : "Todas as etapas da pós-formatação foram concluídas com sucesso!";
                MessageBox.Show(msg, "Pós-formatação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            if (TxtPostFormatCurrentAction != null)
                TxtPostFormatCurrentAction.Text = "Instalações pausadas. Você pode continuar a qualquer momento.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erro durante a execução: " + ex.Message, "Pós-formatação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdatePostFormatSummary();
        }
    }

    private void BtnPostFormatPause_Click(object sender, RoutedEventArgs e)
    {
        if (_postFormatCts != null && !_postFormatCts.IsCancellationRequested)
        {
            _postFormatCts.Cancel();
            if (TxtPostFormatCurrentAction != null)
                TxtPostFormatCurrentAction.Text = "Pausando após o término do item atual...";
            BtnPostFormatPause.IsEnabled = false;
        }
    }

    private void BtnPostFormatReset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Deseja realmente resetar a fila pós-formatação atual e limpar o progresso salvo?",
            "Resetar Fila Pós-Formatação", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            PostFormatManager.ClearSession();
            _currentPostFormatSession = null;
            BuildDefaultPostFormatQueue();
        }
    }

    // Ação do banner de continuação rápida no topo da aplicação
    private void BtnPostFormatBannerResume_Click(object sender, RoutedEventArgs e)
    {
        BrdPostFormatResumeBanner.Visibility = Visibility.Collapsed;
        NavPostFormat.IsChecked = true;
        BtnPostFormatExecuteAll_Click(sender, e);
    }

    private void BtnPostFormatBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        BrdPostFormatResumeBanner.Visibility = Visibility.Collapsed;
        PostFormatManager.ClearSession();
    }

    private void PostFormatQueueItem_Click(object sender, RoutedEventArgs e)
    {
        UpdatePostFormatSummary();
    }

    private void ChkPostFormatCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPostFormatSession == null) return;
        if (sender is CheckBox chk && chk.Tag is string catTag)
        {
            bool isChecked = chk.IsChecked == true;
            PostFormatCategory? targetCat = catTag switch
            {
                "Drivers" => PostFormatCategory.Drivers,
                "WiFi" => PostFormatCategory.WiFi,
                "Apps" => PostFormatCategory.WingetApp,
                _ => null
            };

            if (targetCat.HasValue)
            {
                foreach (var item in _currentPostFormatSession.Items.Where(x => x.Category == targetCat.Value))
                {
                    item.IsSelected = isChecked;
                }
            }
            UpdatePostFormatSummary();
        }
    }

    private void BtnPostFormatRunDrivers_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPostFormatSession == null) return;
        foreach (var item in _currentPostFormatSession.Items)
        {
            item.IsSelected = (item.Category == PostFormatCategory.Drivers || item.Category == PostFormatCategory.RestorePoint);
        }
        UpdatePostFormatSummary();
        BtnPostFormatExecuteAll_Click(sender, e);
    }

    private void BtnPostFormatRunWifi_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPostFormatSession == null) return;
        foreach (var item in _currentPostFormatSession.Items)
        {
            item.IsSelected = (item.Category == PostFormatCategory.WiFi);
        }
        UpdatePostFormatSummary();
        BtnPostFormatExecuteAll_Click(sender, e);
    }

    private void BtnPostFormatRunApps_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPostFormatSession == null) return;
        foreach (var item in _currentPostFormatSession.Items)
        {
            item.IsSelected = (item.Category == PostFormatCategory.WingetApp || item.Category == PostFormatCategory.RestorePoint);
        }
        UpdatePostFormatSummary();
        BtnPostFormatExecuteAll_Click(sender, e);
    }

    private void SyncCategoryCheckboxes()
    {
        if (_currentPostFormatSession == null) return;

        if (ChkPostFormatCategoryDrivers != null)
        {
            var drvItems = _currentPostFormatSession.Items.Where(x => x.Category == PostFormatCategory.Drivers).ToList();
            if (drvItems.Count > 0)
                ChkPostFormatCategoryDrivers.IsChecked = drvItems.All(x => x.IsSelected);
        }
        if (ChkPostFormatCategoryWifi != null)
        {
            var wifiItems = _currentPostFormatSession.Items.Where(x => x.Category == PostFormatCategory.WiFi).ToList();
            if (wifiItems.Count > 0)
                ChkPostFormatCategoryWifi.IsChecked = wifiItems.All(x => x.IsSelected);
        }
        if (ChkPostFormatCategoryApps != null)
        {
            var appItems = _currentPostFormatSession.Items.Where(x => x.Category == PostFormatCategory.WingetApp).ToList();
            if (appItems.Count > 0)
                ChkPostFormatCategoryApps.IsChecked = appItems.All(x => x.IsSelected);
        }
    }
}
