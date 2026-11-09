using System.Diagnostics;

using System.IO;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Threading;

using Microsoft.Win32;



namespace LuminiTray;



/// <summary>

/// Aba RESET FERRAMENTAS · Wallpapers: Integração com o Windows para alteração de papéis de parede,

/// instalação da biblioteca oficial RESET ou seleção de pastas personalizadas com troca automática.

/// </summary>

public partial class MainWindow

{

    private List<WallpaperItem> _wallpapers = new();

    private List<WallpaperMonitorInfo> _monitors = new();

    private DispatcherTimer? _wallpaperSlideshowTimer;

    private FileSystemWatcher? _wallpaperFolderWatcher;

    private bool _isWallpaperInitialized;



    /// <summary>

    /// Inicializa a página de Wallpapers ao ser exibida.

    /// </summary>

    private void InitWallpapers()

    {

        if (!_isWallpaperInitialized)

        {

            _isWallpaperInitialized = true;



            LivelyWallpaperService.Instance.MediaErrorOccurred += OnLivelyWallpaperMediaError;

            LivelyWallpaperService.Instance.WallpaperChanged += OnLivelyWallpaperChanged;



            // Define pasta padrão se nenhuma tiver sido configurada

            if (string.IsNullOrWhiteSpace(_settings.WallpaperFolder))

            {

                if (Directory.Exists(WallpaperManager.DefaultLibraryFolder))

                    _settings.WallpaperFolder = WallpaperManager.DefaultLibraryFolder;

                else

                    _settings.WallpaperFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                _settings.Save();

            }



            TxtWallpaperFolder.Text = _settings.WallpaperFolder;

            ChkAutoChangeWallpaperOnStartup.IsChecked = _settings.AutoChangeWallpaperOnStartup;



            // Detecta monitores automaticamente

            DetectAndPopulateMonitors();



            // Seleciona o estilo configurado

            foreach (ComboBoxItem item in CmbWallpaperStyle.Items)

            {

                if (string.Equals(item.Tag?.ToString(), _settings.WallpaperStyle, StringComparison.OrdinalIgnoreCase))

                {

                    CmbWallpaperStyle.SelectedItem = item;

                    break;

                }

            }



            // Seleciona o intervalo configurado

            foreach (ComboBoxItem item in CmbWallpaperInterval.Items)

            {

                if (int.TryParse(item.Tag?.ToString(), out int mins) && mins == _settings.WallpaperIntervalMinutes)

                {

                    CmbWallpaperInterval.SelectedItem = item;

                    break;

                }

            }



            ConfigureWallpaperSlideshow(_settings.WallpaperIntervalMinutes);

            SetupWallpaperFolderWatcher(_settings.WallpaperFolder);

        }



        RefreshCurrentWallpaperPreview();

        RefreshWallpaperList();

        UpdateLivelyWallpaperUi();

    }



    /// <summary>

    /// Realiza a busca automática de monitores conectados e preenche a caixa de seleção.

    /// </summary>

    private void DetectAndPopulateMonitors()

    {

        _monitors = WallpaperManager.GetConnectedMonitors();



        TxtDetectedMonitorsCount.Text = _monitors.Count == 1

            ? "(1 monitor detectado)"

            : $"({_monitors.Count} monitores detectados)";



        CmbWallpaperMonitor.SelectionChanged -= CmbWallpaperMonitor_SelectionChanged;

        CmbWallpaperMonitor.Items.Clear();



        var allItem = new ComboBoxItem

        {

            Content = "Todos os Monitores (Padrão)",

            Tag = "ALL"

        };

        CmbWallpaperMonitor.Items.Add(allItem);



        ComboBoxItem? toSelect = null;

        foreach (var m in _monitors)

        {

            var item = new ComboBoxItem

            {

                Content = m.Label,

                Tag = m.DeviceId

            };

            CmbWallpaperMonitor.Items.Add(item);



            if (string.Equals(m.DeviceId, _settings.SelectedWallpaperMonitorId, StringComparison.OrdinalIgnoreCase))

            {

                toSelect = item;

            }

        }



        if (toSelect != null)

            CmbWallpaperMonitor.SelectedItem = toSelect;

        else

            CmbWallpaperMonitor.SelectedIndex = 0;



        CmbWallpaperMonitor.SelectionChanged += CmbWallpaperMonitor_SelectionChanged;

    }



    private void CmbWallpaperMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isWallpaperInitialized) return;

        string? targetDeviceId = (CmbWallpaperMonitor.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ALL";
        _settings.SelectedWallpaperMonitorId = targetDeviceId;
        _settings.Save();

        RefreshCurrentWallpaperPreview();
        UpdateLivelyWallpaperUi();
    }



    private void BtnRefreshMonitors_Click(object sender, RoutedEventArgs e)

    {

        DetectAndPopulateMonitors();

        RefreshCurrentWallpaperPreview();

        Toast("Monitores Atualizados", $"{_monitors.Count} tela{(_monitors.Count > 1 ? "s detectadas" : " detectada")} no sistema.", "\uEB9F");

    }



    /// <summary>

    /// Atualiza o cartão do wallpaper atualmente ativo na área de trabalho do Windows.

    /// </summary>

    private void RefreshCurrentWallpaperPreview()

    {

        try

        {

            string? targetDeviceId = (CmbWallpaperMonitor?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? _settings.SelectedWallpaperMonitorId;

            var targetMonitor = _monitors.FirstOrDefault(m => string.Equals(m.DeviceId, targetDeviceId, StringComparison.OrdinalIgnoreCase));



            if (targetMonitor != null)

            {

                TxtCurrentWallpaperMonitorLabel.Text = targetMonitor.DisplayName.ToUpperInvariant();

            }

            else

            {

                TxtCurrentWallpaperMonitorLabel.Text = "TODOS OS MONITORES";

            }



            string? activeVideo = LivelyWallpaperService.Instance.IsEnabled
                ? LivelyWallpaperService.Instance.GetVideoPathForMonitor(targetDeviceId)
                : null;

            if (!string.IsNullOrEmpty(activeVideo) && File.Exists(activeVideo))
            {
                ImgCurrentWallpaper.Source = WallpaperManager.LoadThumbnail(activeVideo, 480);
                TxtCurrentWallpaperName.Text = $"{Path.GetFileName(activeVideo)} (Animado)";
                TxtCurrentWallpaperDimensions.Text = WallpaperManager.GetDimensions(activeVideo);
                TxtCurrentWallpaperPath.Text = activeVideo;
                TxtCurrentWallpaperPath.ToolTip = activeVideo;
                BtnOpenCurrentInExplorer.IsEnabled = true;
            }
            else
            {
                string? current = WallpaperManager.GetWallpaperForMonitor(targetDeviceId);

                if (!string.IsNullOrEmpty(current) && File.Exists(current))
                {
                    ImgCurrentWallpaper.Source = WallpaperManager.LoadThumbnail(current, 480);
                    TxtCurrentWallpaperName.Text = Path.GetFileName(current);
                    TxtCurrentWallpaperDimensions.Text = WallpaperManager.GetDimensions(current);
                    TxtCurrentWallpaperPath.Text = current;
                    TxtCurrentWallpaperPath.ToolTip = current;
                    BtnOpenCurrentInExplorer.IsEnabled = true;
                }
                else
                {
                    ImgCurrentWallpaper.Source = null;
                    TxtCurrentWallpaperName.Text = "Wallpaper Padrão do Windows";
                    TxtCurrentWallpaperDimensions.Text = targetMonitor != null ? targetMonitor.ResolutionText : "Resolução do Sistema";
                    TxtCurrentWallpaperPath.Text = "Tema ou plano de fundo padrão do Windows";
                    TxtCurrentWallpaperPath.ToolTip = null;
                    BtnOpenCurrentInExplorer.IsEnabled = false;
                }
            }

        }

        catch (Exception ex)

        {

            TxtCurrentWallpaperName.Text = "Não identificado";

            TxtCurrentWallpaperDimensions.Text = "";

            TxtCurrentWallpaperPath.Text = ex.Message;

            BtnOpenCurrentInExplorer.IsEnabled = false;

        }

    }



    /// <summary>

    /// Varre a pasta selecionada e carrega os papéis de parede na galeria.

    /// </summary>

    private void SetupWallpaperFolderWatcher(string? folder)

    {

        try

        {

            _wallpaperFolderWatcher?.Dispose();

            _wallpaperFolderWatcher = null;



            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;



            _wallpaperFolderWatcher = new FileSystemWatcher(folder)

            {

                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,

                EnableRaisingEvents = true

            };



            var debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };

            debounceTimer.Tick += (_, _) =>

            {

                debounceTimer.Stop();

                RefreshWallpaperList();

                UpdateLivelyWallpaperUi();

            };



            void TriggerRefresh()
            {
                Dispatcher.BeginInvoke(() =>
                {
                    debounceTimer.Stop();
                    debounceTimer.Start();
                });
            }
            _wallpaperFolderWatcher.Created += (_, _) => TriggerRefresh();
            _wallpaperFolderWatcher.Deleted += (_, _) => TriggerRefresh();
            _wallpaperFolderWatcher.Renamed += (_, _) => TriggerRefresh();

        }

        catch { }

    }



    private async void RefreshWallpaperList()

    {

        string folder = _settings.WallpaperFolder ?? WallpaperManager.DefaultLibraryFolder;

        TxtWallpaperStatus.Text = "Carregando galeria...";



        string? current = WallpaperManager.GetCurrentWallpaper();



        // Detecta vídeos na pasta para a playlist de Wallpapers Animados

        if (Directory.Exists(folder))

        {

            try

            {

                var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mkv", ".avi", ".wmv" };

                var videoFiles = Directory.GetFiles(folder)

                    .Where(f => videoExtensions.Contains(Path.GetExtension(f)))

                    .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)

                    .ToList();



                if (videoFiles.Count > 0)

                {

                    LivelyWallpaperService.Instance.SyncPlaylistWithFolder(videoFiles);

                }

            }

            catch { }

        }



        List<WallpaperItem> items = await Task.Run(() => WallpaperManager.ScanFolder(folder, current));



        if (items.Count == 0)

        {

            _wallpapers.Clear();

            WallpaperGallery.ItemsSource = null;

            WallpaperGallery.Visibility = Visibility.Collapsed;

            WallpaperEmptyNotice.Visibility = Visibility.Visible;

            TxtWallpaperCount.Text = LivelyWallpaperService.Instance.Playlist.Count > 0

                ? $"{LivelyWallpaperService.Instance.Playlist.Count} VÍDEO(S) DE WALLPAPER PRONTO(S)"

                : "NENHUM PAPEL DE PAREDE ENCONTRADO";

            TxtWallpaperStatus.Text = LivelyWallpaperService.Instance.Playlist.Count > 0

                ? $"{LivelyWallpaperService.Instance.Playlist.Count} vídeo(s) de wallpaper animado (.mp4/.webm) detectados na pasta."

                : (Directory.Exists(folder) ? "Nenhuma imagem ou vídeo compatível encontrado na pasta." : "A pasta selecionada não existe.");

            return;

        }



        WallpaperEmptyNotice.Visibility = Visibility.Collapsed;

        WallpaperGallery.Visibility = Visibility.Visible;



        int videoCount = items.Count(i => i.IsVideo);

        int imgCount = items.Count - videoCount;

        if (videoCount > 0 && imgCount > 0)

            TxtWallpaperCount.Text = $"{items.Count} PAPÉIS DE PAREDE ({videoCount} VÍDEOS, {imgCount} IMAGENS)";

        else if (videoCount > 0)

            TxtWallpaperCount.Text = $"{videoCount} PAPÉIS DE PAREDE ANIMADOS (VÍDEO)";

        else

            TxtWallpaperCount.Text = $"{items.Count} PAPÉIS DE PAREDE DISPONÍVEIS";



        TxtWallpaperStatus.Text = $"Pasta: {folder}";



        // Carrega miniaturas e dimensões de forma assíncrona para desempenho instantâneo

        await Task.Run(() =>

        {

            foreach (var it in items)

            {

                it.Thumbnail = WallpaperManager.LoadThumbnail(it.FilePath, 320);

                it.DimensionsText = WallpaperManager.GetDimensions(it.FilePath);

            }

        });



        _wallpapers = items;

        WallpaperGallery.ItemsSource = _wallpapers;

    }



    /// <summary>

    /// Instala a biblioteca oficial de papéis de parede de alta resolução do Lumini Tray.

    /// </summary>

    private async void BtnInstallLibrary_Click(object sender, RoutedEventArgs e)

    {

        BtnInstallLibrary.IsEnabled = false;

        TxtWallpaperStatus.Text = "Gerando biblioteca oficial LUMINI em 1080p...";



        try

        {

            var progress = new Progress<string>(msg =>

            {

                Dispatcher.Invoke(() => TxtWallpaperStatus.Text = msg);

            });



            await Task.Run(() => WallpaperManager.InstallDefaultLibrary(progress));



            _settings.WallpaperFolder = WallpaperManager.DefaultLibraryFolder;

            _settings.Save();

            TxtWallpaperFolder.Text = WallpaperManager.DefaultLibraryFolder;



            RefreshWallpaperList();

        UpdateLivelyWallpaperUi();

            Toast("Biblioteca LUMINI Instalada", "6 papéis de parede em alta resolução foram instalados com sucesso.", "\uEB9F");

        }

        catch (Exception ex)

        {

            MessageBox.Show($"Erro ao instalar biblioteca de wallpapers:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);

        }

        finally

        {

            BtnInstallLibrary.IsEnabled = true;

        }

    }



    /// <summary>

    /// Permite ao usuário escolher qualquer pasta do disco rígido contendo imagens.

    /// </summary>

    private void BtnSelectWallpaperFolder_Click(object sender, RoutedEventArgs e)

    {

        var dlg = new OpenFolderDialog

        {

            Title = "Selecione a pasta com imagens de Wallpaper",

            InitialDirectory = Directory.Exists(_settings.WallpaperFolder)

                ? _settings.WallpaperFolder

                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)

        };



        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))

        {

            _settings.WallpaperFolder = dlg.FolderName;

            _settings.Save();

            TxtWallpaperFolder.Text = dlg.FolderName;

            SetupWallpaperFolderWatcher(dlg.FolderName);

            RefreshWallpaperList();

            UpdateLivelyWallpaperUi();

        }

    }



    /// <summary>

    /// Abre a pasta de wallpapers atual no Windows Explorer.

    /// </summary>

    private void BtnOpenWallpaperFolder_Click(object sender, RoutedEventArgs e)

    {

        string folder = _settings.WallpaperFolder ?? WallpaperManager.DefaultLibraryFolder;

        if (Directory.Exists(folder))

        {

            try

            {

                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });

            }

            catch (Exception ex)

            {

                MessageBox.Show($"Não foi possível abrir a pasta:\n{ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);

            }

        }

        else

        {

            MessageBox.Show("A pasta selecionada não existe no momento.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);

        }

    }



    /// <summary>

    /// Abre o arquivo do wallpaper atual selecionado no Windows Explorer.

    /// </summary>

    private void BtnOpenCurrentInExplorer_Click(object sender, RoutedEventArgs e)

    {

        string? cur = WallpaperManager.GetCurrentWallpaper();

        if (!string.IsNullOrEmpty(cur) && File.Exists(cur))

        {

            try

            {

                Process.Start(new ProcessStartInfo

                {

                    FileName = "explorer.exe",

                    Arguments = $"/select,\"{cur}\"",

                    UseShellExecute = true

                });

            }

            catch (Exception ex)

            {

                MessageBox.Show($"Não foi possível abrir o arquivo:\n{ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);

            }

        }

    }



    /// <summary>

    /// Aplica o wallpaper selecionado com 1 clique.

    /// </summary>

    private void BtnApplyWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WallpaperItem item }) return;

        string? targetDeviceId = (CmbWallpaperMonitor?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? _settings.SelectedWallpaperMonitorId;
        string monitorName = (CmbWallpaperMonitor?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Todos os Monitores";

        if (item.IsVideo)
        {
            LivelyWallpaperService.Instance.PlayFileOnMonitor(item.FilePath, targetDeviceId);
            UpdateLivelyWallpaperUi();
            RefreshCurrentWallpaperPreview();
            Toast("Wallpaper Animado Ativado", $"{item.FileName} está reproduzindo em {monitorName}.", "\uE714");

            foreach (var it in _wallpapers)
                it.IsCurrent = string.Equals(it.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase);

            return;
        }

        try
        {
            WallpaperManager.SetWallpaper(item.FilePath, _settings.WallpaperStyle, targetDeviceId);
            _settings.LastWallpaperPath = item.FilePath;
            if (!string.IsNullOrEmpty(targetDeviceId))
            {
                _settings.StaticWallpaperMonitorPaths[targetDeviceId] = item.FilePath;
            }
            _settings.Save();

            // Interrompe o vídeo apenas no monitor selecionado para liberar a imagem estática
            LivelyWallpaperService.Instance.StopMonitor(targetDeviceId);

            Toast("Wallpaper Aplicado", $"{item.FileName} foi definido para {monitorName}.", "\uEB9F");
            RefreshCurrentWallpaperPreview();
            UpdateLivelyWallpaperUi();

            foreach (var it in _wallpapers)
                it.IsCurrent = string.Equals(it.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Toast("Erro ao Aplicar Wallpaper", ex.Message, "\uE783");
        }
    }



    /// <summary>

    /// Seleciona e aplica um wallpaper aleatório da lista atual.

    /// </summary>

    private void NavigateStaticWallpaper(int direction)
    {
        var staticWallpapers = _wallpapers.Where(w => !w.IsVideo && File.Exists(w.FilePath)).ToList();
        if (staticWallpapers.Count == 0)
        {
            MessageBox.Show("Nenhum papel de parede estático disponível nesta pasta.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int currentIndex = staticWallpapers.FindIndex(w => w.IsCurrent);
        int nextIndex;
        if (currentIndex < 0)
        {
            nextIndex = direction > 0 ? 0 : staticWallpapers.Count - 1;
        }
        else
        {
            nextIndex = (currentIndex + direction + staticWallpapers.Count) % staticWallpapers.Count;
        }

        var chosen = staticWallpapers[nextIndex];
        string? targetDeviceId = (CmbWallpaperMonitor?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? _settings.SelectedWallpaperMonitorId;
        string monitorName = (CmbWallpaperMonitor?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Todos os Monitores";

        try
        {
            WallpaperManager.SetWallpaper(chosen.FilePath, _settings.WallpaperStyle, targetDeviceId);
            _settings.LastWallpaperPath = chosen.FilePath;
            if (!string.IsNullOrEmpty(targetDeviceId))
            {
                _settings.StaticWallpaperMonitorPaths[targetDeviceId] = chosen.FilePath;
            }
            _settings.Save();

            LivelyWallpaperService.Instance.StopMonitor(targetDeviceId);

            Toast("Papel de Parede", $"{chosen.FileName} aplicado para {monitorName}.", "\uEB9F");
            RefreshCurrentWallpaperPreview();
            UpdateLivelyWallpaperUi();

            foreach (var it in _wallpapers)
            {
                it.IsCurrent = string.Equals(it.FilePath, chosen.FilePath, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao aplicar papel de parede:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnPrevStaticWallpaper_Click(object sender, RoutedEventArgs e)
    {
        NavigateStaticWallpaper(-1);
    }

    private void BtnNextStaticWallpaper_Click(object sender, RoutedEventArgs e)
    {
        NavigateStaticWallpaper(1);
    }

    private void BtnRandomWallpaper_Click(object sender, RoutedEventArgs e)

    {

        if (_wallpapers.Count == 0)

        {

            MessageBox.Show("Nenhum papel de parede disponível nesta pasta para escolha aleatória.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }



        var candidates = _wallpapers.Where(w => !w.IsCurrent).ToList();

        var chosenList = candidates.Count > 0 ? candidates : _wallpapers;

        var chosen = chosenList[Random.Shared.Next(chosenList.Count)];



        string? targetDeviceId = (CmbWallpaperMonitor?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? _settings.SelectedWallpaperMonitorId;

        string monitorName = (CmbWallpaperMonitor?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Todos os Monitores";



        try
        {
            WallpaperManager.SetWallpaper(chosen.FilePath, _settings.WallpaperStyle, targetDeviceId);
            _settings.LastWallpaperPath = chosen.FilePath;
            if (!string.IsNullOrEmpty(targetDeviceId))
            {
                _settings.StaticWallpaperMonitorPaths[targetDeviceId] = chosen.FilePath;
            }
            _settings.Save();

            LivelyWallpaperService.Instance.StopMonitor(targetDeviceId);

            Toast("Wallpaper Aleatório", $"{chosen.FileName} foi definido para {monitorName}.", "\uEB9F");
            RefreshCurrentWallpaperPreview();
            UpdateLivelyWallpaperUi();

            foreach (var it in _wallpapers)
                it.IsCurrent = string.Equals(it.FilePath, chosen.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        catch (Exception ex)

        {

            MessageBox.Show($"Não foi possível aplicar o wallpaper aleatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);

        }

    }



    /// <summary>

    /// Altera o estilo do wallpaper no Windows (Fill, Fit, Stretch, Center, Tile, Span).

    /// </summary>

    private void CmbWallpaperStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!_isWallpaperInitialized) return;

        if (CmbWallpaperStyle.SelectedItem is not ComboBoxItem item || item.Tag is not string style) return;



        if (string.Equals(_settings.WallpaperStyle, style, StringComparison.OrdinalIgnoreCase)) return;



        _settings.WallpaperStyle = style;

        _settings.Save();



        if (LivelyWallpaperService.Instance.IsEnabled)

        {

            LivelyWallpaperService.Instance.Enable();

            UpdateLivelyWallpaperUi();

        }



        string? cur = WallpaperManager.GetCurrentWallpaper();

        if (!string.IsNullOrEmpty(cur) && File.Exists(cur))

        {

            try

            {

                WallpaperManager.SetWallpaper(cur, _settings.WallpaperStyle, _settings.SelectedWallpaperMonitorId);

            }

            catch { }

        }

    }



    /// <summary>

    /// Configura o intervalo de troca periódica automática de papel de parede.

    /// </summary>

    private void CmbWallpaperInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!_isWallpaperInitialized) return;

        if (CmbWallpaperInterval.SelectedItem is not ComboBoxItem item || !int.TryParse(item.Tag?.ToString(), out int mins)) return;



        _settings.WallpaperIntervalMinutes = mins;

        _settings.Save();

        ConfigureWallpaperSlideshow(mins);

        LivelyWallpaperService.Instance.SetInterval(mins);

    }



    /// <summary>

    /// Configura ou para o DispatcherTimer de troca periódica de wallpapers.

    /// </summary>

    private void ConfigureWallpaperSlideshow(int minutes)

    {

        if (minutes > 0)

        {

            _wallpaperSlideshowTimer ??= new DispatcherTimer();

            _wallpaperSlideshowTimer.Stop();

            _wallpaperSlideshowTimer.Interval = TimeSpan.FromMinutes(minutes);

            _wallpaperSlideshowTimer.Tick -= WallpaperSlideshow_Tick;

            _wallpaperSlideshowTimer.Tick += WallpaperSlideshow_Tick;

            _wallpaperSlideshowTimer.Start();

        }

        else

        {

            _wallpaperSlideshowTimer?.Stop();

        }

    }



    public void InitializeWallpaperSlideshowOnStartup()
    {
        if (_settings.WallpaperIntervalMinutes > 0)
        {
            ConfigureWallpaperSlideshow(_settings.WallpaperIntervalMinutes);
        }

        if (_settings.AutoChangeWallpaperOnStartup && !_settings.LivelyWallpaperEnabled)
        {
            PerformRandomStaticWallpaperChange();
        }
    }

    private void PerformRandomStaticWallpaperChange()
    {
        try
        {
            string? folder = _settings.WallpaperFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                folder = WallpaperManager.DefaultLibraryFolder;
            }

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            var ext = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
            var images = Directory.GetFiles(folder)
                .Where(f => ext.Contains(Path.GetExtension(f)))
                .ToList();

            if (images.Count == 0) return;

            var candidates = images.Where(f => !string.Equals(f, _settings.LastWallpaperPath, StringComparison.OrdinalIgnoreCase)).ToList();
            string chosen = candidates.Count > 0 ? candidates[Random.Shared.Next(candidates.Count)] : images[Random.Shared.Next(images.Count)];

            WallpaperManager.SetWallpaper(chosen, _settings.WallpaperStyle, _settings.SelectedWallpaperMonitorId);
            _settings.LastWallpaperPath = chosen;
            _settings.Save();

            if (PageSettings != null && PageSettings.Visibility == Visibility.Visible && TabSettingsWallpapers?.IsChecked == true)
            {
                RefreshCurrentWallpaperPreview();
            }
        }
        catch { }
    }

    private void WallpaperSlideshow_Tick(object? sender, EventArgs e)
    {
        // Se wallpapers animados (MPV) não estiverem ativos, rotaciona os wallpapers estáticos do Windows
        if (!_settings.LivelyWallpaperEnabled)
        {
            PerformRandomStaticWallpaperChange();
            return;
        }

        if (_wallpapers.Count <= 1) return;

        var candidates = _wallpapers.Where(w => !w.IsCurrent).ToList();
        var chosenList = candidates.Count > 0 ? candidates : _wallpapers;
        var chosen = chosenList[Random.Shared.Next(chosenList.Count)];

        try
        {
            WallpaperManager.SetWallpaper(chosen.FilePath, _settings.WallpaperStyle, _settings.SelectedWallpaperMonitorId);
            _settings.LastWallpaperPath = chosen.FilePath;
            _settings.Save();

            RefreshCurrentWallpaperPreview();
            foreach (var it in _wallpapers)
                it.IsCurrent = string.Equals(it.FilePath, chosen.FilePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { }
    }

    public void ReleaseWallpaperGalleryMemory()
    {
        if (WallpaperGallery != null && WallpaperGallery.ItemsSource != null)
        {
            WallpaperGallery.ItemsSource = null;
        }
        _wallpapers.Clear();
    }



    /// <summary>

    /// Salva a opção de trocar de wallpaper aleatoriamente na inicialização do Windows.

    /// </summary>

    private void ChkAutoChangeWallpaperOnStartup_Click(object sender, RoutedEventArgs e)

    {

        _settings.AutoChangeWallpaperOnStartup = ChkAutoChangeWallpaperOnStartup.IsChecked == true;

        _settings.Save();

    }



    public void UpdateLivelyWallpaperUi()

    {

        if (BrdLivelyWallpaperStatus == null || TxtLivelyWallpaperStatus == null || BtnToggleLivelyWallpaper == null) return;



        bool active = LivelyWallpaperService.Instance.IsEnabled;

        bool paused = LivelyWallpaperService.Instance.IsPaused;



        if (active)
        {
            string statusStr = paused ? "PAUSADO" : "ATIVO";
            string? targetDeviceId = (CmbWallpaperMonitor?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? _settings.SelectedWallpaperMonitorId;
            string? monVideo = LivelyWallpaperService.Instance.GetVideoPathForMonitor(targetDeviceId);

            if (!string.IsNullOrEmpty(monVideo))
            {
                string file = System.IO.Path.GetFileName(monVideo);
                TxtLivelyWallpaperStatus.Text = $"ATIVO ({statusStr}) · {file}";
                TxtLivelyWallpaperStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                BrdLivelyWallpaperStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 16, 185, 129));
            }
            else
            {
                TxtLivelyWallpaperStatus.Text = "ESTÁTICO NESTE MONITOR · (VÍDEO ATIVO EM OUTRO MONITOR)";
                TxtLivelyWallpaperStatus.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
                BrdLivelyWallpaperStatus.BorderBrush = (System.Windows.Media.Brush)FindResource("LineStrong");
            }

            BtnToggleLivelyWallpaper.Content = "⏹ DESATIVAR";
            BtnToggleLivelyWallpaper.Style = (Style)FindResource("OutlineButton");

            if (BtnPlayPauseLivelyWallpaper != null)
                BtnPlayPauseLivelyWallpaper.Content = paused ? "▶" : "⏸";
        }

        else

        {

            TxtLivelyWallpaperStatus.Text = "DESATIVADO (MÍNIMO CONSUMO)";

            TxtLivelyWallpaperStatus.Foreground = (System.Windows.Media.Brush)FindResource("Muted");

            BrdLivelyWallpaperStatus.BorderBrush = (System.Windows.Media.Brush)FindResource("LineStrong");



            BtnToggleLivelyWallpaper.Content = "▶ ATIVAR WALLPAPERS ANIMADOS";

            BtnToggleLivelyWallpaper.Style = (Style)FindResource("PrimaryButton");



            if (BtnPlayPauseLivelyWallpaper != null)

                BtnPlayPauseLivelyWallpaper.Content = "⏯";

        }



        if (ChkLivelyAutoPauseGames != null)

        {

            ChkLivelyAutoPauseGames.IsChecked = LivelyWallpaperService.Instance.AutoPauseGames;

        }



        if (ChkLivelyPauseOnFocusedApp != null)

        {

            ChkLivelyPauseOnFocusedApp.IsChecked = LivelyWallpaperService.Instance.PauseOnFocusedApp;

            ChkLivelyPauseOnFocusedApp.IsEnabled = LivelyWallpaperService.Instance.AutoPauseGames;

        }



        if (SldLivelyWallpaperVolume != null)

        {

            SldLivelyWallpaperVolume.ValueChanged -= SldLivelyWallpaperVolume_ValueChanged;

            SldLivelyWallpaperVolume.Value = LivelyWallpaperService.Instance.Volume;

            SldLivelyWallpaperVolume.ValueChanged += SldLivelyWallpaperVolume_ValueChanged;

            if (TxtLivelyWallpaperVolume != null)

            {

                TxtLivelyWallpaperVolume.Text = $"{LivelyWallpaperService.Instance.Volume}%";

            }

        }

    }



    private void ChkLivelyAutoPauseGames_Click(object sender, RoutedEventArgs e)

    {

        bool isChecked = ChkLivelyAutoPauseGames.IsChecked == true;

        LivelyWallpaperService.Instance.AutoPauseGames = isChecked;

        if (ChkLivelyPauseOnFocusedApp != null)

        {

            ChkLivelyPauseOnFocusedApp.IsEnabled = isChecked;

        }

        if (_settings != null)

        {

            _settings.LivelyWallpaperAutoPauseGames = isChecked;

            _settings.Save();

        }

    }



    private void ChkLivelyPauseOnFocusedApp_Click(object sender, RoutedEventArgs e)

    {

        bool isChecked = ChkLivelyPauseOnFocusedApp.IsChecked == true;

        LivelyWallpaperService.Instance.PauseOnFocusedApp = isChecked;

        if (_settings != null)

        {

            _settings.LivelyWallpaperPauseOnFocusedApp = isChecked;

            _settings.Save();

        }

    }



    private void SldLivelyWallpaperVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)

    {

        if (TxtLivelyWallpaperVolume == null) return;

        int vol = (int)Math.Round(e.NewValue);

        TxtLivelyWallpaperVolume.Text = $"{vol}%";

        LivelyWallpaperService.Instance.SetVolume(vol);

    }



    private void OnLivelyWallpaperChanged(string filePath)

    {

        Dispatcher.Invoke(() =>

        {

            UpdateLivelyWallpaperUi();

        });

    }



    private void OnLivelyWallpaperMediaError(string error)

    {

        Dispatcher.Invoke(() =>

        {

            Toast("Aviso de Reprodução", $"Falha ao reproduzir vídeo: {error}. Certifique-se de usar vídeos .mp4 (H.264).", "\uE783");

        });

    }



    private void BtnToggleLivelyWallpaper_Click(object sender, RoutedEventArgs e)

    {

        if (LivelyWallpaperService.Instance.IsEnabled)

        {

            LivelyWallpaperService.Instance.Disable();

            Toast("Wallpapers Animados", "Motor Lively desativado. Recursos e memória liberados.", "\uE714");

        }

        else

        {

            if (string.IsNullOrEmpty(LivelyWallpaperService.Instance.CurrentPath) && LivelyWallpaperService.Instance.Playlist.Count == 0)

            {

                BtnAddCustomLivelyWallpaper_Click(sender, e);

                return;

            }



            LivelyWallpaperService.Instance.Enable();

            Toast("Wallpapers Animados", "Motor Lively ativado com suporte completo a ícones da Área de Trabalho.", "\uE714");

        }

        UpdateLivelyWallpaperUi();

    }



    private void BtnPlayPauseLivelyWallpaper_Click(object sender, RoutedEventArgs e)

    {

        LivelyWallpaperService.Instance.TogglePlayPause();

        UpdateLivelyWallpaperUi();

    }



    private void BtnNextLivelyWallpaper_Click(object sender, RoutedEventArgs e)

    {

        LivelyWallpaperService.Instance.NextWallpaper();

        UpdateLivelyWallpaperUi();

    }



    private void BtnPrevLivelyWallpaper_Click(object sender, RoutedEventArgs e)

    {

        LivelyWallpaperService.Instance.PreviousWallpaper();

        UpdateLivelyWallpaperUi();

    }



    private void BtnAddCustomLivelyWallpaper_Click(object sender, RoutedEventArgs e)

    {

        var ofd = new OpenFileDialog

        {

            Title = "Selecionar Vídeo de Wallpaper Animado (Lively)",

            Filter = "Vídeos Suportados (*.mp4;*.webm;*.wmv;*.avi)|*.mp4;*.webm;*.wmv;*.avi|Arquivos MP4 (*.mp4)|*.mp4|Todos os Arquivos (*.*)|*.*",

            Multiselect = true

        };



        if (ofd.ShowDialog(this) == true && ofd.FileNames.Length > 0)

        {

            string? first = null;

            foreach (var file in ofd.FileNames)

            {

                if (System.IO.File.Exists(file))

                {

                    first ??= file;

                    LivelyWallpaperService.Instance.AddToPlaylist(file);

                }

            }



            if (!string.IsNullOrEmpty(first))

            {

                LivelyWallpaperService.Instance.PlayFile(first);

                UpdateLivelyWallpaperUi();

                Toast("Wallpaper Adicionado", $"Vídeo {System.IO.Path.GetFileName(first)} adicionado com reprodução no Desktop.", "\uE714");

            }

        }

    }

}