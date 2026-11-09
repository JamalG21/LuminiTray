using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LuminiTray;

/// <summary>
/// Módulo de Administração Visual de Espaço em Disco (Mapa do Disco) e Limpeza Rápida.
/// </summary>
public partial class MainWindow
{
    private DiskScanResult? _diskScanResult;
    private DiskSpaceItem? _diskSpaceRoot;
    private DiskSpaceItem? _diskSpaceCurrentFolder;
    private readonly Stack<DiskSpaceItem> _diskSpaceNavHistory = new();
    private List<QuickCleanCategory> _quickCleanCategories = new();
    private CancellationTokenSource? _diskSpaceCts;
    private bool _diskSpaceScanning;
    private string? _selectedDrivePath;

    private void InitDiskSpaceAnalyzer()
    {
        PopulateDriveCards();

        if (_diskSpaceRoot == null && !string.IsNullOrEmpty(_selectedDrivePath))
        {
            ScanDiskPathAsync(_selectedDrivePath);
        }
    }

    private void PopulateDriveCards()
    {
        PnlDiskDrives.Children.Clear();
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)).ToList();

        if (string.IsNullOrEmpty(_selectedDrivePath) && drives.Count > 0)
        {
            var cDrive = drives.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase)) ?? drives[0];
            _selectedDrivePath = cDrive.RootDirectory.FullName;
        }

        foreach (var d in drives)
        {
            string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disco Local" : d.VolumeLabel;
            string driveLetter = d.Name.TrimEnd('\\');
            long total = d.TotalSize;
            long free = d.TotalFreeSpace;
            long used = total - free;
            double usedPct = total > 0 ? (double)used / total * 100.0 : 0.0;
            bool isSelected = string.Equals(d.RootDirectory.FullName, _selectedDrivePath, StringComparison.OrdinalIgnoreCase);

            var card = new Border
            {
                Style = (Style)FindResource("CardBorder"),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 10, 8),
                Width = 210,
                Cursor = Cursors.Hand,
                Tag = d.RootDirectory.FullName,
                BorderBrush = isSelected ? (Brush)FindResource("Accent") : (Brush)FindResource("Line"),
                Background = isSelected ? (Brush)FindResource("CardSubtleBg") : (Brush)FindResource("CardSubtleBg")
            };

            var stack = new StackPanel();

            // Linha superior: Ícone e Letra/Nome
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleSp = new StackPanel { Orientation = Orientation.Horizontal };
            titleSp.Children.Add(new TextBlock
            {
                Text = "💽 ",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleSp.Children.Add(new TextBlock
            {
                Text = $"{driveLetter}: {label}",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Fg"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 130,
                VerticalAlignment = VerticalAlignment.Center
            });
            topGrid.Children.Add(titleSp);
            stack.Children.Add(topGrid);

            // Barra de uso
            var pgb = new ProgressBar
            {
                Height = 3.5,
                Minimum = 0,
                Maximum = 100,
                Value = usedPct,
                Background = (Brush)FindResource("Line"),
                Foreground = usedPct > 90.0 ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)) : (Brush)FindResource("Accent"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            stack.Children.Add(pgb);

            // Texto de espaço livre
            var txtFree = new TextBlock
            {
                Text = $"{DiskSpaceAnalyzer.FormatSize(free)} livres de {DiskSpaceAnalyzer.FormatSize(total)}",
                FontSize = 10.5,
                FontFamily = (FontFamily)FindResource("Mono"),
                Foreground = (Brush)FindResource("Muted")
            };
            stack.Children.Add(txtFree);

            card.Child = stack;

            card.MouseLeftButtonDown += (s, e) =>
            {
                _selectedDrivePath = d.RootDirectory.FullName;
                PopulateDriveCards();
                ScanDiskPathAsync(_selectedDrivePath);
            };

            PnlDiskDrives.Children.Add(card);
        }
    }

    private void BtnDiskChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecione uma pasta para mapear espaço em disco",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                _selectedDrivePath = dialog.FolderName;
                PopulateDriveCards();
                ScanDiskPathAsync(dialog.FolderName);
            }
        }
        catch
        {
            // Fallback usando Windows Forms se OpenFolderDialog falhar
            using var fbd = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione uma pasta para mapear espaço em disco",
                UseDescriptionForTitle = true
            };
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                _selectedDrivePath = fbd.SelectedPath;
                PopulateDriveCards();
                ScanDiskPathAsync(fbd.SelectedPath);
            }
        }
    }

    private async void ScanDiskPathAsync(string rootPath)
    {
        if (_diskSpaceScanning)
        {
            _diskSpaceCts?.Cancel();
            return;
        }

        _diskSpaceScanning = true;
        _diskSpaceCts = new CancellationTokenSource();
        PgbDiskSpaceScan.Visibility = Visibility.Visible;
        TxtDiskFolderTotalSize.Text = "Lendo…";
        TxtDiskSpaceBreadcrumb.Text = $"Varrendo {rootPath}…";
        TxtTreemapEmpty.Text = "Indexando pastas e calculando volumes… Aguarde.";
        TxtTreemapEmpty.Visibility = Visibility.Visible;

        try
        {
            var result = await DiskSpaceAnalyzer.ScanAsync(
                rootPath,
                (currentPath, count) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtDiskSpaceBreadcrumb.Text = $"Analisando ({count:N0} itens): {currentPath}";
                    });
                },
                _diskSpaceCts.Token);

            _diskScanResult = result;
            _diskSpaceRoot = result.Root;
            _diskSpaceCurrentFolder = result.Root;
            _diskSpaceNavHistory.Clear();

            UpdateDiskSpaceFolderView(result.Root);

            TxtDiskScanStats.Text = $"{result.Root.FileCount:N0} arquivos · {result.Root.FolderCount:N0} pastas · lido em {result.Elapsed.TotalSeconds:N1} s · {result.AccessDeniedCount} pastas sem acesso · {result.ReparseCount} atalhos de pasta não seguidos";
        }
        catch (OperationCanceledException)
        {
            TxtDiskSpaceBreadcrumb.Text = "Varredura interrompida pelo usuário.";
            TxtTreemapEmpty.Text = "Varredura cancelada.";
        }
        catch (Exception ex)
        {
            TxtDiskSpaceBreadcrumb.Text = $"Erro na varredura: {ex.Message}";
            TxtTreemapEmpty.Text = $"Erro: {ex.Message}";
        }
        finally
        {
            _diskSpaceScanning = false;
            PgbDiskSpaceScan.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateDiskSpaceFolderView(DiskSpaceItem folder)
    {
        _diskSpaceCurrentFolder = folder;
        TxtDiskFolderTotalSize.Text = folder.FormattedSize;
        TxtDiskSpaceBreadcrumb.Text = folder.FullPath;
        BtnDiskBack.IsEnabled = _diskSpaceNavHistory.Count > 0;

        RenderCategoryLegend(folder);
        RenderTreemap(folder);
        RefreshTopItemsList();
    }

    private void RenderCategoryLegend(DiskSpaceItem folder)
    {
        PnlCategoryLegend.Children.Clear();
        if (folder == null || folder.Size <= 0) return;

        var dist = DiskSpaceAnalyzer.ComputeCategoryDistribution(folder);
        long totalSize = folder.Size;

        foreach (var (cat, bytes) in dist)
        {
            if (bytes <= 0 && dist.Values.Count(v => v > 0) > 4) continue;

            double pct = totalSize > 0 ? (double)bytes / totalSize * 100.0 : 0.0;
            var color = TreemapLayoutEngine.GetCategoryColor(cat);

            var pill = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = (Brush)FindResource("CardSubtleBg"),
                BorderBrush = (Brush)FindResource("Line"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 10, 3),
                Margin = new Thickness(0, 0, 7, 6)
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(color),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(dot);

            sp.Children.Add(new TextBlock
            {
                Text = $"{cat} {DiskSpaceAnalyzer.FormatSize(bytes)} {pct:F1}%",
                FontSize = 11,
                Foreground = (Brush)FindResource("Fg"),
                VerticalAlignment = VerticalAlignment.Center
            });

            pill.Child = sp;
            PnlCategoryLegend.Children.Add(pill);
        }
    }

    private void RenderTreemap(DiskSpaceItem folder)
    {
        CnvTreemap.Children.Clear();
        if (folder == null || folder.Children.Count == 0 || folder.Size <= 0)
        {
            TxtTreemapEmpty.Visibility = Visibility.Visible;
            TxtTreemapEmpty.Text = "Esta pasta está vazia ou não contém itens ocupando espaço.";
            return;
        }

        TxtTreemapEmpty.Visibility = Visibility.Collapsed;

        double width = CnvTreemap.ActualWidth > 50 ? CnvTreemap.ActualWidth : 680;
        double height = CnvTreemap.ActualHeight > 50 ? CnvTreemap.ActualHeight : 420;

        var tiles = TreemapLayoutEngine.CalculateLayout(folder, new Rect(0, 0, width, height), maxDepth: 2);
        RenderTilesRecursive(tiles);
    }

    private void RenderTilesRecursive(IEnumerable<TreemapTile> tiles)
    {
        foreach (var tile in tiles)
        {
            if (tile.Bounds.Width <= 2 || tile.Bounds.Height <= 2) continue;

            var border = new Border
            {
                Width = Math.Max(1, tile.Bounds.Width - 1),
                Height = Math.Max(1, tile.Bounds.Height - 1),
                Background = tile.FillBrush,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                ToolTip = $"{tile.Item.Name}\nTamanho: {tile.Item.FormattedSize} ({tile.Item.FormattedPercentage})\nCategoria: {tile.Item.Category}\nCaminho: {tile.Item.FullPath}"
            };

            Canvas.SetLeft(border, tile.Bounds.X);
            Canvas.SetTop(border, tile.Bounds.Y);

            // Conteúdo visual interno
            if (tile.Children.Count > 0)
            {
                var canvasInner = new Canvas { ClipToBounds = true };
                var headerText = new TextBlock
                {
                    Text = $"{tile.Item.Name} · {tile.Item.FormattedSize}",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(4, 2, 4, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                canvasInner.Children.Add(headerText);
                border.Child = canvasInner;

                CnvTreemap.Children.Add(border);
                RenderTilesRecursive(tile.Children);
            }
            else
            {
                if (tile.Bounds.Width >= 38 && tile.Bounds.Height >= 20)
                {
                    var stack = new StackPanel
                    {
                        Margin = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = VerticalAlignment.Top
                    };

                    stack.Children.Add(new TextBlock
                    {
                        Text = tile.Item.Name,
                        FontSize = tile.Bounds.Width > 70 ? 11 : 9.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    if (tile.Bounds.Height >= 34)
                    {
                        stack.Children.Add(new TextBlock
                        {
                            Text = tile.Item.FormattedSize,
                            FontSize = 9.5,
                            Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                    }

                    border.Child = stack;
                }

                CnvTreemap.Children.Add(border);
            }

            // Ações de clique
            if (tile.Item.IsDirectory)
            {
                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (_diskSpaceCurrentFolder != null)
                    {
                        _diskSpaceNavHistory.Push(_diskSpaceCurrentFolder);
                        UpdateDiskSpaceFolderView(tile.Item);
                    }
                };
            }

            // Menu de contexto
            var cm = new ContextMenu();
            var miOpen = new MenuItem { Header = "📂 Abrir no Windows Explorer" };
            miOpen.Click += (_, _) => DiskSpaceAnalyzer.OpenInExplorer(tile.Item.FullPath);
            cm.Items.Add(miOpen);

            var miCopy = new MenuItem { Header = "📋 Copiar Caminho Completo" };
            miCopy.Click += (_, _) => Clipboard.SetText(tile.Item.FullPath);
            cm.Items.Add(miCopy);

            cm.Items.Add(new Separator());

            var miRecycle = new MenuItem { Header = "🗑️ Mover para Lixeira" };
            miRecycle.Click += (_, _) =>
            {
                if (MessageBox.Show($"Mover \"{tile.Item.Name}\" ({tile.Item.FormattedSize}) para a Lixeira?", "Lixeira do Windows",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    if (DiskSpaceAnalyzer.MoveToRecycleBin(tile.Item.FullPath))
                    {
                        _diskSpaceCurrentFolder?.Children.Remove(tile.Item);
                        UpdateDiskSpaceFolderView(_diskSpaceCurrentFolder!);
                    }
                }
            };
            cm.Items.Add(miRecycle);

            var miDel = new MenuItem { Header = "⚠️ Excluir Permanentemente" };
            miDel.Click += (_, _) =>
            {
                if (MessageBox.Show($"EXCLUIR PERMANENTEMENTE \"{tile.Item.Name}\" ({tile.Item.FormattedSize})?\nEsta ação não pode ser desfeita!", "Exclusão Permanente",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    if (DiskSpaceAnalyzer.DeletePermanently(tile.Item.FullPath))
                    {
                        _diskSpaceCurrentFolder?.Children.Remove(tile.Item);
                        UpdateDiskSpaceFolderView(_diskSpaceCurrentFolder!);
                    }
                }
            };
            cm.Items.Add(miDel);

            border.ContextMenu = cm;
        }
    }

    private void CnvTreemap_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_diskSpaceCurrentFolder != null && !_diskSpaceScanning)
        {
            RenderTreemap(_diskSpaceCurrentFolder);
        }
    }

    private void TopItemsTab_Checked(object sender, RoutedEventArgs e)
    {
        RefreshTopItemsList();
    }

    private void RefreshTopItemsList()
    {
        if (LstDiskTopItems == null || _diskSpaceCurrentFolder == null) return;

        bool showFolders = BtnTabTopFolders?.IsChecked == true;

        if (showFolders)
        {
            var folders = _diskSpaceCurrentFolder.Children
                .Where(c => c.IsDirectory)
                .OrderByDescending(c => c.Size)
                .Take(50)
                .ToList();
            LstDiskTopItems.ItemsSource = folders;
        }
        else
        {
            var files = _diskSpaceCurrentFolder.Children
                .Where(c => !c.IsDirectory)
                .OrderByDescending(c => c.Size)
                .Take(50)
                .ToList();
            LstDiskTopItems.ItemsSource = files;
        }
    }

    private void LstDiskTopItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstDiskTopItems.SelectedItem is DiskSpaceItem item && item.IsDirectory)
        {
            if (_diskSpaceCurrentFolder != null)
            {
                _diskSpaceNavHistory.Push(_diskSpaceCurrentFolder);
                UpdateDiskSpaceFolderView(item);
            }
        }
    }

    private void BtnDiskSpaceUp_Click(object sender, RoutedEventArgs e)
    {
        if (_diskSpaceNavHistory.Count > 0)
        {
            var parent = _diskSpaceNavHistory.Pop();
            UpdateDiskSpaceFolderView(parent);
        }
    }

    private void BtnDiskOpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_diskSpaceCurrentFolder != null)
        {
            DiskSpaceAnalyzer.OpenInExplorer(_diskSpaceCurrentFolder.FullPath);
        }
    }

    private void BtnTipLimpeza_Click(object sender, RoutedEventArgs e)
    {
        BtnToolDiskClean_Click(sender, e);
    }

    private void BtnTipDesinstalador_Click(object sender, RoutedEventArgs e)
    {
        ShowPage("PageUninstaller");
    }

    private void BtnTipDuplicados_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("A busca por arquivos duplicados está integrada na Limpeza Rápida e Varredura de Sobras do Lumini Tray.", "Arquivos Duplicados", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------- Limpeza Rápida de Disco ----------------

    private async void RefreshQuickClean()
    {
        BtnQuickCleanScan.IsEnabled = false;
        TxtQuickCleanStatus.Text = "Analisando arquivos temporários e caches de sombras...";
        _quickCleanCategories = await DiskSpaceAnalyzer.ScanQuickCleanAsync();
        LstQuickClean.ItemsSource = null;
        LstQuickClean.ItemsSource = _quickCleanCategories;

        long totalReclaimable = _quickCleanCategories.Sum(c => c.Size);
        TxtQuickCleanTotal.Text = DiskSpaceAnalyzer.FormatSize(totalReclaimable);
        TxtQuickCleanStatus.Text = $"{_quickCleanCategories.Count} categorias detectadas · {DiskSpaceAnalyzer.FormatSize(totalReclaimable)} recuperáveis.";
        BtnQuickCleanScan.IsEnabled = true;
        BtnQuickCleanNow.IsEnabled = _quickCleanCategories.Count > 0;
    }

    private void BtnQuickCleanScan_Click(object sender, RoutedEventArgs e) => RefreshQuickClean();

    private async void BtnQuickCleanNow_Click(object sender, RoutedEventArgs e)
    {
        var selected = _quickCleanCategories.Where(c => c.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Nenhuma categoria selecionada para limpeza.", "Limpeza de Disco", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnQuickCleanNow.IsEnabled = false;
        BtnQuickCleanScan.IsEnabled = false;
        int totalFiles = 0;
        long totalBytes = 0;

        foreach (var cat in selected)
        {
            TxtQuickCleanStatus.Text = $"Limpando {cat.Name}…";
            var (files, bytes) = await DiskSpaceAnalyzer.CleanCategoryAsync(cat);
            totalFiles += files;
            totalBytes += bytes;
        }

        RefreshQuickClean();
        MessageBox.Show($"Limpeza concluída com sucesso!\n{totalFiles:N0} arquivos removidos ({DiskSpaceAnalyzer.FormatSize(totalBytes)} de espaço liberado).",
            "Limpeza de Disco Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
