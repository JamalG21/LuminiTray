using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Janela moderna de seleção de aplicativos e processos elegíveis para blindagem/exclusão.
/// Executa varredura exclusivamente sob demanda (on-demand) ao ser aberta e libera a memória ao fechar.
/// </summary>
public partial class AppPickerWindow : Window
{
    private readonly IEnumerable<string>? _existingExclusions;
    private readonly CancellationTokenSource _cts = new();
    private List<SelectableAppItem> _allApps = new();
    private List<SelectableAppItem> _filteredApps = new();
    private string _activeFilterTag = "All";

    public List<string> SelectedExecutables { get; } = new();
    public List<SelectableAppItem> SelectedAppItems { get; } = new();

    public AppPickerWindow(IEnumerable<string>? existingExclusions = null)
    {
        InitializeComponent();
        _existingExclusions = existingExclusions;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PanelLoading.Visibility = Visibility.Visible;
        LstApps.Visibility = Visibility.Collapsed;
        PanelEmpty.Visibility = Visibility.Collapsed;

        try
        {
            // Varredura exclusivamente pontual sob demanda
            _allApps = await AppScannerService.ScanAllAppsAsync(_existingExclusions, _cts.Token);

            int runningCount = _allApps.Count(x => x.IsRunning);
            int gamesCount = _allApps.Count(x => x.IsGame);
            int installedCount = _allApps.Count(x => !x.IsRunning && !x.IsGame);

            BtnFilterAll.Content = $"Todos ({_allApps.Count})";
            BtnFilterRunning.Content = $"⚡ Em Execução ({runningCount})";
            BtnFilterGames.Content = $"🎮 Jogos ({gamesCount})";
            BtnFilterInstalled.Content = $"💻 Programas Instalados ({installedCount})";

            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro ao listar os programas: {ex.Message}", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            PanelLoading.Visibility = Visibility.Collapsed;
            LstApps.Visibility = Visibility.Visible;
            TxtSearch.Focus();
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(TxtSearch.Text))
            {
                TxtSearch.Text = "";
                e.Handled = true;
            }
            else
            {
                Close();
                e.Handled = true;
            }
        }
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeFilterTag = tag;

            // Atualiza o estilo visual dos botões de filtro
            var activeStyle = (Style)FindResource("OutlineButton");
            var inactiveStyle = (Style)FindResource("GhostButton");

            BtnFilterAll.Style = _activeFilterTag == "All" ? activeStyle : inactiveStyle;
            BtnFilterRunning.Style = _activeFilterTag == "Running" ? activeStyle : inactiveStyle;
            BtnFilterGames.Style = _activeFilterTag == "Games" ? activeStyle : inactiveStyle;
            BtnFilterInstalled.Style = _activeFilterTag == "Installed" ? activeStyle : inactiveStyle;

            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        string query = TxtSearch?.Text?.Trim() ?? "";
        IEnumerable<SelectableAppItem> stream = _allApps;

        // Filtro por Categoria
        stream = _activeFilterTag switch
        {
            "Running" => stream.Where(x => x.IsRunning),
            "Games" => stream.Where(x => x.IsGame),
            "Installed" => stream.Where(x => !x.IsRunning && !x.IsGame),
            _ => stream
        };

        // Filtro por Texto digitado
        if (!string.IsNullOrWhiteSpace(query))
        {
            stream = stream.Where(x =>
                x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.ExeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Detail.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _filteredApps = stream.ToList();
        LstApps.ItemsSource = _filteredApps;

        PanelEmpty.Visibility = _filteredApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AppCheckbox_Click(object sender, RoutedEventArgs e)
    {
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        int count = _allApps.Count(x => x.IsSelected);
        TxtSelectionCount.Text = $"{count} aplicativo(s) selecionado(s)";
        BtnAddSelected.IsEnabled = count > 0;
    }

    private void LstApps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstApps.SelectedItem is SelectableAppItem item && item.CanSelect)
        {
            SelectedExecutables.Clear();
            SelectedExecutables.Add(item.ExeName);
            SelectedAppItems.Clear();
            SelectedAppItems.Add(item);
            DialogResult = true;
            Close();
        }
    }

    private void BtnBrowseCustomExe_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Selecionar Arquivo Executável para Blindagem",
            Filter = "Aplicativos Executáveis (*.exe)|*.exe|Todos os Arquivos (*.*)|*.*",
            Multiselect = true
        };

        if (ofd.ShowDialog(this) == true && ofd.FileNames.Length > 0)
        {
            foreach (var file in ofd.FileNames)
            {
                if (!File.Exists(file)) continue;

                string exeName = AppScannerService.NormalizeExe(Path.GetFileName(file));
                var existing = _allApps.FirstOrDefault(x => string.Equals(x.ExeName, exeName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (existing.CanSelect) existing.IsSelected = true;
                }
                else
                {
                    var newItem = new SelectableAppItem
                    {
                        ExeName = exeName,
                        DisplayName = Path.GetFileNameWithoutExtension(file),
                        Detail = $"Arquivo local · {Path.GetDirectoryName(file)}",
                        ExecutablePath = file,
                        Category = "Programas Instalados",
                        IsRunning = false,
                        IsGame = false,
                        IsAlreadyExcluded = false,
                        IsSelected = true,
                        IconSource = AppScannerService.GetOrExtractIcon(file)
                    };
                    _allApps.Insert(0, newItem);
                }
            }

            ApplyFilter();
            UpdateSelectionCount();
        }
    }

    private void BtnAddSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allApps.Where(x => x.IsSelected && x.CanSelect).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Nenhum aplicativo foi selecionado.", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedExecutables.Clear();
        SelectedAppItems.Clear();
        foreach (var s in selected)
        {
            SelectedExecutables.Add(s.ExeName);
            SelectedAppItems.Add(s);
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        _allApps.Clear();
        _filteredApps.Clear();
        base.OnClosed(e);
    }
}
